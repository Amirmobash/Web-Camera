using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace UsbCameraOrangeDeutsch
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
                Name = name;
                DevicePath = path;
                Moniker = moniker;
            }

            public string Name { get; }
            public string DevicePath { get; }
            internal IMoniker Moniker { get; }

            public override string ToString()
            {
                return string.IsNullOrWhiteSpace(DevicePath) ? Name : Name + " [" + DevicePath + "]";
            }
        }

        public DirectShowCamera(CameraDevice device)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
        }

        public static List<CameraDevice> EnumerateVideoDevices()
        {
            var result = new List<CameraDevice>();
            ICreateDevEnum devEnum = null;
            IEnumMoniker enumMoniker = null;

            try
            {
                devEnum = (ICreateDevEnum)new CreateDevEnum();
                var category = DirectShowGuids.CLSID_VideoInputDeviceCategory;
                var hr = devEnum.CreateClassEnumerator(ref category, out enumMoniker, 0);

                if (hr != 0 || enumMoniker == null)
                    return result;

                var monikers = new IMoniker[1];
                while (enumMoniker.Next(1, monikers, IntPtr.Zero) == 0)
                {
                    var moniker = monikers[0];
                    var name = ReadMonikerProperty(moniker, "FriendlyName") ?? "Unbekannte Kamera";
                    var path = ReadMonikerProperty(moniker, "DevicePath") ?? string.Empty;
                    result.Add(new CameraDevice(name, path, moniker));
                    monikers[0] = null;
                }
            }
            finally
            {
                ReleaseComObject(enumMoniker);
                ReleaseComObject(devEnum);
            }

            return result;
        }

        public void StartPreview(IntPtr previewWindowHandle, System.Drawing.Rectangle previewBounds)
        {
            ThrowIfDisposed();

            _graph = (IGraphBuilder)new FilterGraph();
            _captureGraph = (ICaptureGraphBuilder2)new CaptureGraphBuilder2();

            CheckHr(_captureGraph.SetFiltergraph(_graph), "CaptureGraphBuilder konnte nicht initialisiert werden.");

            object sourceObject;
            var baseFilterGuid = typeof(IBaseFilter).GUID;
            _device.Moniker.BindToObject(null, null, ref baseFilterGuid, out sourceObject);
            _sourceFilter = (IBaseFilter)sourceObject;

            CheckHr(_graph.AddFilter(_sourceFilter, "Ausgewählte USB-Kamera"), "Kamera konnte nicht zum DirectShow-Graph hinzugefügt werden.");

            var pinCategory = DirectShowGuids.PIN_CATEGORY_PREVIEW;
            var mediaType = DirectShowGuids.MEDIATYPE_Video;

            var renderHr = _captureGraph.RenderStream(ref pinCategory, ref mediaType, _sourceFilter, null, null);
            if (renderHr < 0)
            {
                pinCategory = DirectShowGuids.PIN_CATEGORY_CAPTURE;
                renderHr = _captureGraph.RenderStream(ref pinCategory, ref mediaType, _sourceFilter, null, null);
            }
            CheckHr(renderHr, "Videostream konnte nicht verbunden werden.");

            _mediaControl = (IMediaControl)_graph;
            _videoWindow = (IVideoWindow)_graph;

            const int WS_CHILD = 0x40000000;
            const int WS_CLIPSIBLINGS = 0x04000000;
            const int OATRUE = -1;

            CheckHr(_videoWindow.put_Owner(previewWindowHandle), "Vorschaufenster konnte nicht gesetzt werden.");
            CheckHr(_videoWindow.put_WindowStyle(WS_CHILD | WS_CLIPSIBLINGS), "Fensterstil konnte nicht gesetzt werden.");
            CheckHr(_videoWindow.put_Visible(OATRUE), "Vorschau konnte nicht sichtbar gemacht werden.");
            ResizeVideo(previewBounds);

            CheckHr(_mediaControl.Run(), "Kamera konnte nicht gestartet werden.");
        }

        public void ResizeVideo(System.Drawing.Rectangle previewBounds)
        {
            if (_videoWindow == null)
                return;

            _videoWindow.SetWindowPosition(0, 0, Math.Max(1, previewBounds.Width), Math.Max(1, previewBounds.Height));
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            try
            {
                if (_mediaControl != null)
                    _mediaControl.Stop();
            }
            catch
            {
                // Ignore cleanup errors.
            }

            try
            {
                if (_videoWindow != null)
                {
                    const int OAFALSE = 0;
                    _videoWindow.put_Visible(OAFALSE);
                    _videoWindow.put_Owner(IntPtr.Zero);
                }
            }
            catch
            {
                // Ignore cleanup errors.
            }

            ReleaseComObject(_sourceFilter);
            ReleaseComObject(_captureGraph);
            ReleaseComObject(_graph);

            _sourceFilter = null;
            _captureGraph = null;
            _graph = null;
            _mediaControl = null;
            _videoWindow = null;
            _disposed = true;
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

                object value;
                var hr = bag.Read(propertyName, out value, IntPtr.Zero);
                if (hr == 0 && value != null)
                    return value.ToString();
            }
            catch
            {
                // Some drivers do not expose all properties.
            }
            finally
            {
                ReleaseComObject(bagObject);
            }

            return null;
        }

        private static void CheckHr(int hr, string message)
        {
            if (hr < 0)
                throw new InvalidOperationException(message + " HRESULT: 0x" + hr.ToString("X8"));
        }

        private static void ReleaseComObject(object comObject)
        {
            if (comObject != null && Marshal.IsComObject(comObject))
            {
                try
                {
                    Marshal.ReleaseComObject(comObject);
                }
                catch
                {
                    // Ignore cleanup errors.
                }
            }
        }

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
            int CreateClassEnumerator(ref Guid pType, out IEnumMoniker ppEnumMoniker, int dwFlags);
        }

        [ComImport]
        [Guid("55272A00-42CB-11CE-8135-00AA004BB851")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyBag
        {
            [PreserveSig]
            int Read(
                [MarshalAs(UnmanagedType.LPWStr)] string pszPropName,
                [MarshalAs(UnmanagedType.Struct)] out object pVar,
                IntPtr pErrorLog);

            [PreserveSig]
            int Write(
                [MarshalAs(UnmanagedType.LPWStr)] string pszPropName,
                [MarshalAs(UnmanagedType.Struct)] ref object pVar);
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
            int AddFilter(IBaseFilter pFilter, [MarshalAs(UnmanagedType.LPWStr)] string pName);

            [PreserveSig]
            int RemoveFilter(IBaseFilter pFilter);

            [PreserveSig]
            int EnumFilters(out object ppEnum);

            [PreserveSig]
            int FindFilterByName([MarshalAs(UnmanagedType.LPWStr)] string pName, out IBaseFilter ppFilter);

            [PreserveSig]
            int ConnectDirect(object ppinOut, object ppinIn, IntPtr pmt);

            [PreserveSig]
            int Reconnect(object ppin);

            [PreserveSig]
            int Disconnect(object ppin);

            [PreserveSig]
            int SetDefaultSyncSource();
        }

        [ComImport]
        [Guid("56A868A9-0AD4-11CE-B03A-0020AF0BA770")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IGraphBuilder : IFilterGraph
        {
            [PreserveSig]
            new int AddFilter(IBaseFilter pFilter, [MarshalAs(UnmanagedType.LPWStr)] string pName);

            [PreserveSig]
            new int RemoveFilter(IBaseFilter pFilter);

            [PreserveSig]
            new int EnumFilters(out object ppEnum);

            [PreserveSig]
            new int FindFilterByName([MarshalAs(UnmanagedType.LPWStr)] string pName, out IBaseFilter ppFilter);

            [PreserveSig]
            new int ConnectDirect(object ppinOut, object ppinIn, IntPtr pmt);

            [PreserveSig]
            new int Reconnect(object ppin);

            [PreserveSig]
            new int Disconnect(object ppin);

            [PreserveSig]
            new int SetDefaultSyncSource();

            [PreserveSig]
            int Connect(object ppinOut, object ppinIn);

            [PreserveSig]
            int Render(object ppinOut);

            [PreserveSig]
            int RenderFile([MarshalAs(UnmanagedType.LPWStr)] string lpcwstrFile, [MarshalAs(UnmanagedType.LPWStr)] string lpcwstrPlayList);

            [PreserveSig]
            int AddSourceFilter([MarshalAs(UnmanagedType.LPWStr)] string lpcwstrFileName, [MarshalAs(UnmanagedType.LPWStr)] string lpcwstrFilterName, out IBaseFilter ppFilter);

            [PreserveSig]
            int SetLogFile(IntPtr hFile);

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
            int SetFiltergraph(IGraphBuilder pfg);

            [PreserveSig]
            int GetFiltergraph(out IGraphBuilder ppfg);

            [PreserveSig]
            int SetOutputFileName(ref Guid pType, [MarshalAs(UnmanagedType.LPWStr)] string lpstrFile, out IBaseFilter ppbf, out object ppSink);

            [PreserveSig]
            int FindInterface(ref Guid pCategory, ref Guid pType, IBaseFilter pf, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppint);

            [PreserveSig]
            int RenderStream(ref Guid pCategory, ref Guid pType, [MarshalAs(UnmanagedType.Interface)] object pSource, IBaseFilter pfCompressor, IBaseFilter pfRenderer);
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
            int GetState(int msTimeout, out int pfs);

            [PreserveSig]
            int RenderFile([MarshalAs(UnmanagedType.BStr)] string strFilename);

            [PreserveSig]
            int AddSourceFilter([MarshalAs(UnmanagedType.BStr)] string strFilename, [MarshalAs(UnmanagedType.IDispatch)] out object ppUnk);

            [PreserveSig]
            int get_FilterCollection([MarshalAs(UnmanagedType.IDispatch)] out object ppUnk);

            [PreserveSig]
            int get_RegFilterCollection([MarshalAs(UnmanagedType.IDispatch)] out object ppUnk);

            [PreserveSig]
            int StopWhenReady();
        }

        [ComImport]
        [Guid("56A868B4-0AD4-11CE-B03A-0020AF0BA770")]
        [InterfaceType(ComInterfaceType.InterfaceIsDual)]
        private interface IVideoWindow
        {
            [PreserveSig]
            int put_Caption([MarshalAs(UnmanagedType.BStr)] string strCaption);

            [PreserveSig]
            int get_Caption([MarshalAs(UnmanagedType.BStr)] out string strCaption);

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
            int NotifyOwnerMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

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
