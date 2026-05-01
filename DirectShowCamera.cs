using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace UsbCameraPreview
{
    public sealed class DirectShowCamera : IDisposable
    {
        private readonly CameraDevice _device;
        private IGraphBuilder _graph;
        private ICaptureGraphBuilder2 _captureGraph;
        private IBaseFilter _sourceFilter;
        private IMediaControl _mediaControl;
        private IVideoWindow _videoWindow;
        private bool _disposed;

        public sealed class CameraDevice
        {
            internal CameraDevice(string name, string path, IMoniker moniker)
            {
                Name = string.IsNullOrWhiteSpace(name) ? "Unknown Camera" : name;
                DevicePath = path ?? string.Empty;
                Moniker = moniker ?? throw new ArgumentNullException(nameof(moniker));
            }

            public string Name { get; }
            public string DevicePath { get; }
            internal IMoniker Moniker { get; }

            public override string ToString()
            {
                return string.IsNullOrWhiteSpace(DevicePath)
                    ? Name
                    : $"{Name} [{DevicePath}]";
            }
        }

        public DirectShowCamera(CameraDevice device)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
        }

        public static List<CameraDevice> EnumerateVideoDevices()
        {
            var devices = new List<CameraDevice>();
            ICreateDevEnum devEnum = null;
            IEnumMoniker enumMoniker = null;

            try
            {
                devEnum = (ICreateDevEnum)new CreateDevEnum();

                var category = DirectShowGuids.CLSID_VideoInputDeviceCategory;
                var hr = devEnum.CreateClassEnumerator(ref category, out enumMoniker, 0);

                if (hr != 0 || enumMoniker == null)
                    return devices;

                var monikers = new IMoniker[1];

                while (enumMoniker.Next(1, monikers, IntPtr.Zero) == 0)
                {
                    var moniker = monikers[0];

                    var name = ReadMonikerProperty(moniker, "FriendlyName") ?? "Unknown Camera";
                    var path = ReadMonikerProperty(moniker, "DevicePath") ?? GetMonikerDisplayName(moniker) ?? string.Empty;

                    devices.Add(new CameraDevice(name, path, moniker));
                    monikers[0] = null;
                }
            }
            finally
            {
                ReleaseComObject(enumMoniker);
                ReleaseComObject(devEnum);
            }

            return devices;
        }

        public void StartPreview(IntPtr previewWindowHandle, Rectangle previewBounds)
        {
            ThrowIfDisposed();

            if (previewWindowHandle == IntPtr.Zero)
                throw new ArgumentException("Preview window handle cannot be empty.", nameof(previewWindowHandle));

            StopPreview();

            try
            {
                _graph = (IGraphBuilder)new FilterGraph();
                _captureGraph = (ICaptureGraphBuilder2)new CaptureGraphBuilder2();

                CheckHr(_captureGraph.SetFiltergraph(_graph), "Could not initialize the capture graph builder.");

                object sourceObject;
                var baseFilterGuid = typeof(IBaseFilter).GUID;

                _device.Moniker.BindToObject(null, null, ref baseFilterGuid, out sourceObject);
                _sourceFilter = (IBaseFilter)sourceObject;

                CheckHr(_graph.AddFilter(_sourceFilter, "Selected USB Camera"), "Could not add the camera to the DirectShow graph.");

                var pinCategory = DirectShowGuids.PIN_CATEGORY_PREVIEW;
                var mediaType = DirectShowGuids.MEDIATYPE_Video;

                var hr = _captureGraph.RenderStream(ref pinCategory, ref mediaType, _sourceFilter, null, null);

                if (hr < 0)
                {
                    pinCategory = DirectShowGuids.PIN_CATEGORY_CAPTURE;
                    hr = _captureGraph.RenderStream(ref pinCategory, ref mediaType, _sourceFilter, null, null);
                }

                CheckHr(hr, "Could not connect the video stream.");

                _mediaControl = (IMediaControl)_graph;
                _videoWindow = (IVideoWindow)_graph;

                const int wsChild = 0x40000000;
                const int wsClipSiblings = 0x04000000;
                const int wsClipChildren = 0x02000000;
                const int oaTrue = -1;

                CheckHr(_videoWindow.put_Owner(previewWindowHandle), "Could not set the preview window owner.");
                CheckHr(_videoWindow.put_MessageDrain(previewWindowHandle), "Could not set the preview message drain.");
                CheckHr(_videoWindow.put_WindowStyle(wsChild | wsClipSiblings | wsClipChildren), "Could not set the preview window style.");
                CheckHr(_videoWindow.put_Visible(oaTrue), "Could not show the preview window.");

                ResizeVideo(previewBounds);

                CheckHr(_mediaControl.Run(), "Could not start the camera.");
            }
            catch
            {
                StopPreview();
                throw;
            }
        }

        public void ResizeVideo(Rectangle previewBounds)
        {
            if (_videoWindow == null)
                return;

            var width = Math.Max(1, previewBounds.Width);
            var height = Math.Max(1, previewBounds.Height);

            CheckHr(_videoWindow.SetWindowPosition(0, 0, width, height), "Could not resize the preview window.");
        }

        public void StopPreview()
        {
            try
            {
                if (_mediaControl != null)
                    _mediaControl.Stop();
            }
            catch
            {
            }

            try
            {
                if (_videoWindow != null)
                {
                    const int oaFalse = 0;

                    _videoWindow.put_Visible(oaFalse);
                    _videoWindow.put_MessageDrain(IntPtr.Zero);
                    _videoWindow.put_Owner(IntPtr.Zero);
                }
            }
            catch
            {
            }

            ReleaseComObject(_sourceFilter);
            ReleaseComObject(_captureGraph);
            ReleaseComObject(_graph);

            _sourceFilter = null;
            _captureGraph = null;
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

        private static string ReadMonikerProperty(IMoniker moniker, string propertyName)
        {
            object bagObject = null;

            try
            {
                var bagGuid = typeof(IPropertyBag).GUID;

                moniker.BindToStorage(null, null, ref bagGuid, out bagObject);

                var bag = (IPropertyBag)bagObject;
                var hr = bag.Read(propertyName, out var value, IntPtr.Zero);

                return hr == 0 && value != null ? value.ToString() : null;
            }
            catch
            {
                return null;
            }
            finally
            {
                ReleaseComObject(bagObject);
            }
        }

        private static string GetMonikerDisplayName(IMoniker moniker)
        {
            IBindCtx bindContext = null;

            try
            {
                var hr = CreateBindCtx(0, out bindContext);

                if (hr < 0 || bindContext == null)
                    return null;

                moniker.GetDisplayName(bindContext, null, out var displayName);
                return displayName;
            }
            catch
            {
                return null;
            }
            finally
            {
                ReleaseComObject(bindContext);
            }
        }

        private static void CheckHr(int hr, string message)
        {
            if (hr < 0)
                throw new InvalidOperationException($"{message} HRESULT: 0x{unchecked((uint)hr):X8}");
        }

        private static void ReleaseComObject(object comObject)
        {
            if (comObject == null || !Marshal.IsComObject(comObject))
                return;

            try
            {
                Marshal.ReleaseComObject(comObject);
            }
            catch
            {
            }
        }

        [DllImport("ole32.dll")]
        private static extern int CreateBindCtx(int reserved, out IBindCtx bindContext);

        private static class DirectShowGuids
        {
            public static readonly Guid CLSID_VideoInputDeviceCategory = new Guid("860BB310-5D01-11D0-BD3B-00A0C911CE86");
            public static readonly Guid MEDIATYPE_Video = new Guid("73646976-0000-0010-8000-00AA00389B71");
            public static readonly Guid PIN_CATEGORY_PREVIEW = new Guid("FB6C4282-0353-11D1-905F-0000C0CC16BA");
            public static readonly Guid PIN_CATEGORY_CAPTURE = new Guid("FB6C4281-0353-11D1-905F-0000C0CC16BA");
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
            int AddSourceFilter([MarshalAs(UnmanagedType.BStr)] string fileName, [MarshalAs(UnmanagedType.IDispatch)] out object filter);

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
