using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace GabelstaplerKameraMonitor
{
    public sealed class DirectShowCamera : IDisposable
    {
        private readonly CameraDevice _device;

        private IGraphBuilder _graph;
        private ICaptureGraphBuilder2 _builder;
        private IBaseFilter _cameraFilter;
        private IMediaControl _mediaControl;
        private IVideoWindow _videoWindow;

        private bool _disposed;

        public sealed class CameraDevice
        {
            internal CameraDevice(string name, string path, IMoniker moniker)
            {
                Name = string.IsNullOrWhiteSpace(name) ? "Unknown Camera" : name;
                DevicePath = path ?? string.Empty;
                Moniker = moniker;
            }

            public string Name { get; }
            public string DevicePath { get; }
            internal IMoniker Moniker { get; }

            public override string ToString()
            {
                if (string.IsNullOrWhiteSpace(DevicePath))
                    return Name;

                return $"{Name} [{DevicePath}]";
            }
        }

        public DirectShowCamera(CameraDevice device)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
        }

        public static List<CameraDevice> EnumerateVideoDevices()
        {
            var cameras = new List<CameraDevice>();

            ICreateDevEnum devEnum = null;
            IEnumMoniker enumMoniker = null;

            try
            {
                devEnum = (ICreateDevEnum)new CreateDevEnum();

                var category = DirectShowGuids.VideoInputDeviceCategory;
                var hr = devEnum.CreateClassEnumerator(ref category, out enumMoniker, 0);

                if (hr != 0 || enumMoniker == null)
                    return cameras;

                var monikers = new IMoniker[1];

                while (enumMoniker.Next(1, monikers, IntPtr.Zero) == 0)
                {
                    var moniker = monikers[0];

                    var name = ReadProperty(moniker, "FriendlyName") ?? "Unknown Camera";
                    var path = ReadProperty(moniker, "DevicePath") ?? string.Empty;

                    cameras.Add(new CameraDevice(name, path, moniker));

                    monikers[0] = null;
                }
            }
            finally
            {
                Release(enumMoniker);
                Release(devEnum);
            }

            return cameras;
        }

        public void StartPreview(IntPtr parentHandle, Rectangle bounds)
        {
            ThrowIfDisposed();

            if (parentHandle == IntPtr.Zero)
                throw new ArgumentException("Invalid preview window handle.", nameof(parentHandle));

            StopPreview();

            try
            {
                _graph = (IGraphBuilder)new FilterGraph();
                _builder = (ICaptureGraphBuilder2)new CaptureGraphBuilder2();

                Check(_builder.SetFiltergraph(_graph), "Could not create camera graph.");

                object filterObject;
                var filterId = typeof(IBaseFilter).GUID;

                _device.Moniker.BindToObject(null, null, ref filterId, out filterObject);
                _cameraFilter = (IBaseFilter)filterObject;

                Check(_graph.AddFilter(_cameraFilter, "USB Camera"), "Could not add camera filter.");

                var pinCategory = DirectShowGuids.PreviewPin;
                var mediaType = DirectShowGuids.VideoMediaType;

                var hr = _builder.RenderStream(ref pinCategory, ref mediaType, _cameraFilter, null, null);

                if (hr < 0)
                {
                    pinCategory = DirectShowGuids.CapturePin;
                    hr = _builder.RenderStream(ref pinCategory, ref mediaType, _cameraFilter, null, null);
                }

                Check(hr, "Could not render camera stream.");

                _mediaControl = (IMediaControl)_graph;
                _videoWindow = (IVideoWindow)_graph;

                const int wsChild = 0x40000000;
                const int wsClipSiblings = 0x04000000;
                const int wsClipChildren = 0x02000000;
                const int visible = -1;

                Check(_videoWindow.put_Owner(parentHandle), "Could not set preview owner.");
                Check(_videoWindow.put_MessageDrain(parentHandle), "Could not set message drain.");
                Check(_videoWindow.put_WindowStyle(wsChild | wsClipSiblings | wsClipChildren), "Could not set preview style.");
                Check(_videoWindow.put_Visible(visible), "Could not show preview.");

                ResizeVideo(bounds);

                Check(_mediaControl.Run(), "Could not start camera.");
            }
            catch
            {
                StopPreview();
                throw;
            }
        }

        public void ResizeVideo(Rectangle bounds)
        {
            if (_videoWindow == null)
                return;

            var width = Math.Max(1, bounds.Width);
            var height = Math.Max(1, bounds.Height);

            _videoWindow.SetWindowPosition(0, 0, width, height);
        }

        public void StopPreview()
        {
            try
            {
                _mediaControl?.Stop();
            }
            catch
            {
            }

            try
            {
                if (_videoWindow != null)
                {
                    _videoWindow.put_Visible(0);
                    _videoWindow.put_MessageDrain(IntPtr.Zero);
                    _videoWindow.put_Owner(IntPtr.Zero);
                }
            }
            catch
            {
            }

            Release(_cameraFilter);
            Release(_builder);
            Release(_graph);

            _cameraFilter = null;
            _builder = null;
            _graph = null;
            _mediaControl = null;
            _videoWindow = null;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            StopPreview();

            _disposed = true;
            GC.SuppressFinalize(this);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(DirectShowCamera));
        }

        private static string ReadProperty(IMoniker moniker, string propertyName)
        {
            object bagObject = null;

            try
            {
                var bagId = typeof(IPropertyBag).GUID;

                moniker.BindToStorage(null, null, ref bagId, out bagObject);

                var bag = (IPropertyBag)bagObject;
                var hr = bag.Read(propertyName, out var value, IntPtr.Zero);

                if (hr == 0 && value != null)
                    return value.ToString();
            }
            catch
            {
            }
            finally
            {
                Release(bagObject);
            }

            return null;
        }

        private static void Check(int hr, string message)
        {
            if (hr < 0)
                throw new InvalidOperationException($"{message} HRESULT: 0x{unchecked((uint)hr):X8}");
        }

        private static void Release(object comObject)
        {
            if (comObject == null)
                return;

            if (!Marshal.IsComObject(comObject))
                return;

            try
            {
                Marshal.ReleaseComObject(comObject);
            }
            catch
            {
            }
        }

        private static class DirectShowGuids
        {
            public static readonly Guid VideoInputDeviceCategory = new Guid("860BB310-5D01-11D0-BD3B-00A0C911CE86");
            public static readonly Guid VideoMediaType = new Guid("73646976-0000-0010-8000-00AA00389B71");
            public static readonly Guid PreviewPin = new Guid("FB6C4282-0353-11D1-905F-0000C0CC16BA");
            public static readonly Guid CapturePin = new Guid("FB6C4281-0353-11D1-905F-0000C0CC16BA");
        }

        [ComImport]
        [Guid("62BE5D10-60EB-11D0-BD3B-00A0C911CE86")]
        private class CreateDevEnum
        {
        }

        [ComImport]
        [Guid("E436EBB3-524F-11CE-9F53-0020AF0BA770")]
        private class FilterGraph
        {
        }

        [ComImport]
        [Guid("BF87B6E1-8C27-11D0-B3F0-00AA003761C5")]
        private class CaptureGraphBuilder2
        {
        }

        [ComImport]
        [Guid("29840822-5B84-11D0-BD3B-00A0C911CE86")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ICreateDevEnum
        {
            [PreserveSig]
            int CreateClassEnumerator(ref Guid category, out IEnumMoniker enumMoniker, int flags);
        }

        [ComImport]
        [Guid("55272A00-42CB-11CE-8135-00AA004BB851")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyBag
        {
            [PreserveSig]
            int Read(
                [MarshalAs(UnmanagedType.LPWStr)] string propertyName,
                [MarshalAs(UnmanagedType.Struct)] out object value,
                IntPtr errorLog);

            [PreserveSig]
            int Write(
                [MarshalAs(UnmanagedType.LPWStr)] string propertyName,
                [MarshalAs(UnmanagedType.Struct)] ref object value);
        }

        [ComImport]
        [Guid("56A86895-0AD4-11CE-B03A-0020AF0BA770")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IBaseFilter
        {
        }

        [ComImport]
        [Guid("56A8689F-0AD4-11CE-B03A-0020AF0BA770")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFilterGraph
        {
            [PreserveSig]
            int AddFilter(IBaseFilter filter, [MarshalAs(UnmanagedType.LPWStr)] string name);

            [PreserveSig]
            int RemoveFilter(IBaseFilter filter);

            [PreserveSig]
            int EnumFilters(out object enumFilters);

            [PreserveSig]
            int FindFilterByName([MarshalAs(UnmanagedType.LPWStr)] string name, out IBaseFilter filter);

            [PreserveSig]
            int ConnectDirect(object outputPin, object inputPin, IntPtr mediaType);

            [PreserveSig]
            int Reconnect(object pin);

            [PreserveSig]
            int Disconnect(object pin);

            [PreserveSig]
            int SetDefaultSyncSource();
        }

        [ComImport]
        [Guid("56A868A9-0AD4-11CE-B03A-0020AF0BA770")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IGraphBuilder : IFilterGraph
        {
            [PreserveSig]
            new int AddFilter(IBaseFilter filter, [MarshalAs(UnmanagedType.LPWStr)] string name);

            [PreserveSig]
            new int RemoveFilter(IBaseFilter filter);

            [PreserveSig]
            new int EnumFilters(out object enumFilters);

            [PreserveSig]
            new int FindFilterByName([MarshalAs(UnmanagedType.LPWStr)] string name, out IBaseFilter filter);

            [PreserveSig]
            new int ConnectDirect(object outputPin, object inputPin, IntPtr mediaType);

            [PreserveSig]
            new int Reconnect(object pin);

            [PreserveSig]
            new int Disconnect(object pin);

            [PreserveSig]
            new int SetDefaultSyncSource();

            [PreserveSig]
            int Connect(object outputPin, object inputPin);

            [PreserveSig]
            int Render(object outputPin);

            [PreserveSig]
            int RenderFile(
                [MarshalAs(UnmanagedType.LPWStr)] string fileName,
                [MarshalAs(UnmanagedType.LPWStr)] string playList);

            [PreserveSig]
            int AddSourceFilter(
                [MarshalAs(UnmanagedType.LPWStr)] string fileName,
                [MarshalAs(UnmanagedType.LPWStr)] string filterName,
                out IBaseFilter filter);

            [PreserveSig]
            int SetLogFile(IntPtr fileHandle);

            [PreserveSig]
            int Abort();

            [PreserveSig]
            int ShouldOperationContinue();
        }

        [ComImport]
        [Guid("93E5A4E0-2D50-11D2-ABFA-00A0C9C6E38D")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ICaptureGraphBuilder2
        {
            [PreserveSig]
            int SetFiltergraph(IGraphBuilder graph);

            [PreserveSig]
            int GetFiltergraph(out IGraphBuilder graph);

            [PreserveSig]
            int SetOutputFileName(
                ref Guid type,
                [MarshalAs(UnmanagedType.LPWStr)] string fileName,
                out IBaseFilter filter,
                out object sink);

            [PreserveSig]
            int FindInterface(
                ref Guid category,
                ref Guid type,
                IBaseFilter filter,
                ref Guid interfaceId,
                [MarshalAs(UnmanagedType.IUnknown)] out object result);

            [PreserveSig]
            int RenderStream(
                ref Guid category,
                ref Guid type,
                [MarshalAs(UnmanagedType.Interface)] object source,
                IBaseFilter compressor,
                IBaseFilter renderer);
        }

        [ComImport]
        [Guid("56A868B1-0AD4-11CE-B03A-0020AF0BA770")]
        [InterfaceType(ComInterfaceType.InterfaceIsDual)]
        private interface IMediaControl
        {
            [PreserveSig]
            int Run();

            [PreserveSig]
            int Pause();

            [PreserveSig]
            int Stop();

            [PreserveSig]
            int GetState(int timeout, out int state);

            [PreserveSig]
            int RenderFile([MarshalAs(UnmanagedType.BStr)] string fileName);

            [PreserveSig]
            int AddSourceFilter(
                [MarshalAs(UnmanagedType.BStr)] string fileName,
                [MarshalAs(UnmanagedType.IDispatch)] out object filter);

            [PreserveSig]
            int get_FilterCollection([MarshalAs(UnmanagedType.IDispatch)] out object collection);

            [PreserveSig]
            int get_RegFilterCollection([MarshalAs(UnmanagedType.IDispatch)] out object collection);

            [PreserveSig]
            int StopWhenReady();
        }

        [ComImport]
        [Guid("56A868B4-0AD4-11CE-B03A-0020AF0BA770")]
        [InterfaceType(ComInterfaceType.InterfaceIsDual)]
        private interface IVideoWindow
        {
            [PreserveSig]
            int put_Caption([MarshalAs(UnmanagedType.BStr)] string caption);

            [PreserveSig]
            int get_Caption([MarshalAs(UnmanagedType.BStr)] out string caption);

            [PreserveSig]
            int put_WindowStyle(int windowStyle);

            [PreserveSig]
            int get_WindowStyle(out int windowStyle);

            [PreserveSig]
            int put_WindowStyleEx(int windowStyleEx);

            [PreserveSig]
            int get_WindowStyleEx(out int windowStyleEx);

            [PreserveSig]
            int put_AutoShow(int autoShow);

            [PreserveSig]
            int get_AutoShow(out int autoShow);

            [PreserveSig]
            int put_WindowState(int windowState);

            [PreserveSig]
            int get_WindowState(out int windowState);

            [PreserveSig]
            int put_BackgroundPalette(int backgroundPalette);

            [PreserveSig]
            int get_BackgroundPalette(out int backgroundPalette);

            [PreserveSig]
            int put_Visible(int visible);

            [PreserveSig]
            int get_Visible(out int visible);

            [PreserveSig]
            int put_Left(int left);

            [PreserveSig]
            int get_Left(out int left);

            [PreserveSig]
            int put_Width(int width);

            [PreserveSig]
            int get_Width(out int width);

            [PreserveSig]
            int put_Top(int top);

            [PreserveSig]
            int get_Top(out int top);

            [PreserveSig]
            int put_Height(int height);

            [PreserveSig]
            int get_Height(out int height);

            [PreserveSig]
            int put_Owner(IntPtr owner);

            [PreserveSig]
            int get_Owner(out IntPtr owner);

            [PreserveSig]
            int put_MessageDrain(IntPtr drain);

            [PreserveSig]
            int get_MessageDrain(out IntPtr drain);

            [PreserveSig]
            int get_BorderColor(out int color);

            [PreserveSig]
            int put_BorderColor(int color);

            [PreserveSig]
            int get_FullScreenMode(out int fullScreenMode);

            [PreserveSig]
            int put_FullScreenMode(int fullScreenMode);

            [PreserveSig]
            int SetWindowForeground(int focus);

            [PreserveSig]
            int NotifyOwnerMessage(IntPtr windowHandle, int message, IntPtr wParam, IntPtr lParam);

            [PreserveSig]
            int SetWindowPosition(int left, int top, int width, int height);

            [PreserveSig]
            int GetWindowPosition(out int left, out int top, out int width, out int height);

            [PreserveSig]
            int GetMinIdealImageSize(out int width, out int height);

            [PreserveSig]
            int GetMaxIdealImageSize(out int width, out int height);

            [PreserveSig]
            int GetRestorePosition(out int left, out int top, out int width, out int height);

            [PreserveSig]
            int HideCursor(int hideCursor);

            [PreserveSig]
            int IsCursorHidden(out int cursorHidden);
        }
    }
}
