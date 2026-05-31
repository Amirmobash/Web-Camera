using System;
using System.Configuration;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace GabelstaplerKameraMonitor
{
    public class MainForm : Form
    {
        private readonly Panel _headerPanel;
        private readonly Label _titleLabel;
        private readonly Label _statusLabel;
        private readonly PreviewPanel _previewPanel;
        private readonly Panel _footerPanel;
        private readonly Button _refreshButton;
        private readonly Button _startButton;
        private readonly Button _stopButton;
        private readonly Button _recordButton;
        private readonly Button _lineUpButton;
        private readonly Button _lineDownButton;
        private readonly Label _hintLabel;
        private readonly Label _recordingLabel;
        private readonly Panel _forkLine;
        private readonly Label _forkLineText;
        private readonly Timer _recordTimer;

        private DirectShowCamera _camera;
        private DirectShowCamera.CameraDevice _currentCamera;
        private MjpegAviRecorder _recorder;
        private string _currentRecordingPath;
        private int _forkLineY;
        private DateTime _recordingStarted;

        private static readonly Color Orange = Color.FromArgb(255, 122, 0);
        private static readonly Color OrangeDark = Color.FromArgb(190, 74, 0);
        private static readonly Color OrangeLight = Color.FromArgb(255, 178, 78);
        private static readonly Color Background = Color.FromArgb(25, 25, 25);
        private static readonly Color PanelDark = Color.FromArgb(37, 37, 37);
        private static readonly Color Red = Color.FromArgb(230, 0, 0);

        public MainForm()
        {
            Text = "Gabelstapler Kamera-Monitor";
            MinimumSize = new Size(1020, 640);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Background;
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            _headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 104,
                BackColor = Orange
            };

            _titleLabel = new Label
            {
                AutoSize = false,
                Text = "Gabelstapler Kamera-Monitor",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 22F, FontStyle.Bold, GraphicsUnit.Point),
                Location = new Point(24, 14),
                Size = new Size(900, 42)
            };

            _statusLabel = new Label
            {
                AutoSize = false,
                Text = "Kamera wird gesucht...",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10.5F, FontStyle.Regular, GraphicsUnit.Point),
                Location = new Point(27, 61),
                Size = new Size(900, 26)
            };

            _headerPanel.Controls.Add(_titleLabel);
            _headerPanel.Controls.Add(_statusLabel);

            _footerPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 124,
                BackColor = PanelDark,
                Padding = new Padding(18)
            };

            _refreshButton = CreateButton("Aktualisieren", 122);
            _refreshButton.Location = new Point(18, 18);
            _refreshButton.Click += (s, e) => RestartCamera();

            _startButton = CreateButton("Start", 108);
            _startButton.Location = new Point(150, 18);
            _startButton.Click += (s, e) => StartCamera();

            _stopButton = CreateButton("Stop", 108);
            _stopButton.Location = new Point(268, 18);
            _stopButton.Enabled = false;
            _stopButton.Click += (s, e) => StopCamera("Kamera gestoppt.");

            _recordButton = CreateButton("Aufnahme starten", 168);
            _recordButton.Location = new Point(386, 18);
            _recordButton.Enabled = false;
            _recordButton.Click += (s, e) => ToggleRecording();

            _lineUpButton = CreateButton("Linie hoch", 112);
            _lineUpButton.Location = new Point(564, 18);
            _lineUpButton.Click += (s, e) => MoveForkLine(-LineStep());

            _lineDownButton = CreateButton("Linie runter", 122);
            _lineDownButton.Location = new Point(686, 18);
            _lineDownButton.Click += (s, e) => MoveForkLine(LineStep());

            _recordingLabel = new Label
            {
                AutoSize = false,
                Text = "Keine Aufnahme aktiv.",
                ForeColor = Color.Gainsboro,
                Location = new Point(18, 70),
                Size = new Size(430, 24)
            };

            _hintLabel = new Label
            {
                AutoSize = false,
                Text = "Mausrad im Kamerabild bewegt die rote Gabelspitzen-Linie nach oben oder unten.",
                ForeColor = Color.Gainsboro,
                Location = new Point(458, 70),
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
                Size = new Size(520, 32)
            };

            _footerPanel.Controls.Add(_refreshButton);
            _footerPanel.Controls.Add(_startButton);
            _footerPanel.Controls.Add(_stopButton);
            _footerPanel.Controls.Add(_recordButton);
            _footerPanel.Controls.Add(_lineUpButton);
            _footerPanel.Controls.Add(_lineDownButton);
            _footerPanel.Controls.Add(_recordingLabel);
            _footerPanel.Controls.Add(_hintLabel);

            _previewPanel = new PreviewPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Black
            };

            _previewPanel.WheelMoved += delta =>
            {
                if (delta == 0)
                    return;

                MoveForkLine(-Math.Sign(delta) * LineStep());
            };

            _previewPanel.Resize += (s, e) =>
            {
                _camera?.ResizeVideo(_previewPanel.ClientRectangle);
                KeepForkLineInside();
                UpdateForkLine();
            };

            _forkLine = new Panel
            {
                BackColor = Red,
                Height = 4,
                Cursor = Cursors.SizeNS
            };

            _forkLineText = new Label
            {
                AutoSize = false,
                Text = "GABELSPITZE",
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Red,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold, GraphicsUnit.Point),
                Size = new Size(136, 24),
                Cursor = Cursors.SizeNS
            };

            _forkLine.MouseWheel += (s, e) => MoveForkLine(-Math.Sign(e.Delta) * LineStep());
            _forkLineText.MouseWheel += (s, e) => MoveForkLine(-Math.Sign(e.Delta) * LineStep());
            _forkLine.MouseDown += (s, e) => _previewPanel.Focus();
            _forkLineText.MouseDown += (s, e) => _previewPanel.Focus();

            _previewPanel.Controls.Add(_forkLine);
            _previewPanel.Controls.Add(_forkLineText);

            var previewFrame = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(18),
                BackColor = Background
            };

            previewFrame.Controls.Add(_previewPanel);

            _recordTimer = new Timer();
            _recordTimer.Tick += (s, e) => CaptureRecordingFrame();

            Controls.Add(previewFrame);
            Controls.Add(_footerPanel);
            Controls.Add(_headerPanel);

            Shown += (s, e) =>
            {
                SetInitialForkLine();
                StartCamera();
            };

            FormClosing += (s, e) =>
            {
                StopRecording(false);
                CleanupCamera();
            };
        }

        private Button CreateButton(string text, int width)
        {
            var button = new Button
            {
                Text = text,
                Width = width,
                Height = 42,
                FlatStyle = FlatStyle.Flat,
                BackColor = Orange,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold, GraphicsUnit.Point),
                Cursor = Cursors.Hand
            };

            button.FlatAppearance.BorderColor = OrangeLight;
            button.FlatAppearance.MouseOverBackColor = OrangeLight;
            button.FlatAppearance.MouseDownBackColor = OrangeDark;

            return button;
        }

        private void RestartCamera()
        {
            var wasRecording = _recorder != null;

            if (wasRecording)
                StopRecording(false);

            StopCamera("Kamera wird neu geladen...");
            StartCamera();
        }

        private void StartCamera()
        {
            try
            {
                CleanupCamera();

                _currentCamera = FindCamera();

                if (_currentCamera == null)
                {
                    UpdateButtons();
                    return;
                }

                _camera = new DirectShowCamera(_currentCamera);
                _camera.StartPreview(_previewPanel.Handle, _previewPanel.ClientRectangle);

                BringForkLineToFront();
                SetStatus("Aktiv: " + _currentCamera.Name + " | Gabelspitzen-Kamera bereit.");
                UpdateButtons();
            }
            catch (Exception ex)
            {
                CleanupCamera();
                UpdateButtons();
                SetStatus("Fehler: " + ex.Message);

                MessageBox.Show(
                    this,
                    ex.Message,
                    "Kamera-Fehler",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private DirectShowCamera.CameraDevice FindCamera()
        {
            var cameras = DirectShowCamera.EnumerateVideoDevices();

            if (cameras.Count == 0)
            {
                SetStatus("Keine USB- oder Webkamera gefunden.");

                MessageBox.Show(
                    this,
                    "Keine USB- oder Webkamera gefunden.",
                    "Kamera",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                return null;
            }

            var targetName = GetSetting("TargetCameraName");
            var matchMode = GetSetting("CameraMatchMode", "Contains");

            if (string.IsNullOrWhiteSpace(targetName))
                return cameras[0];

            var comparison = StringComparison.OrdinalIgnoreCase;

            var camera = matchMode.Equals("Exact", comparison)
                ? cameras.FirstOrDefault(x => string.Equals(x.Name, targetName, comparison))
                : cameras.FirstOrDefault(x => x.Name.IndexOf(targetName, comparison) >= 0);

            if (camera != null)
                return camera;

            var foundCameras = string.Join(Environment.NewLine, cameras.Select(x => "- " + x.Name));

            SetStatus("Zielkamera nicht gefunden: " + targetName);

            MessageBox.Show(
                this,
                "Die eingestellte Gabelstapler-Kamera wurde nicht gefunden:" +
                Environment.NewLine +
                targetName +
                Environment.NewLine +
                Environment.NewLine +
                "Gefundene Kameras:" +
                Environment.NewLine +
                foundCameras,
                "Kamera nicht gefunden",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

            return null;
        }

        private void StopCamera(string status)
        {
            StopRecording(false);
            CleanupCamera();
            SetStatus(status);
            UpdateButtons();
        }

        private void CleanupCamera()
        {
            if (_camera == null)
                return;

            _camera.Dispose();
            _camera = null;
        }

        private void ToggleRecording()
        {
            if (_recorder == null)
                StartRecording();
            else
                StopRecording(true);
        }

        private void StartRecording()
        {
            try
            {
                if (_camera == null)
                    StartCamera();

                if (_camera == null)
                    return;

                var size = _previewPanel.ClientSize;

                if (size.Width <= 0 || size.Height <= 0)
                    throw new InvalidOperationException("Das Kamerabild ist noch nicht bereit.");

                var folder = GetRecordingFolder();
                var fileName = "Stapler_Aufnahme_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".avi";
                _currentRecordingPath = Path.Combine(folder, fileName);

                var fps = ReadIntSetting("RecordingFps", 15, 1, 30);
                var quality = ReadIntSetting("RecordingQuality", 85, 1, 100);

                _recorder = new MjpegAviRecorder(_currentRecordingPath, size.Width, size.Height, fps, quality);
                _recordTimer.Interval = Math.Max(1, 1000 / fps);
                _recordTimer.Start();
                _recordingStarted = DateTime.Now;

                _recordButton.Text = "Aufnahme stoppen";
                _recordingLabel.Text = "Aufnahme läuft: " + fileName;
                SetStatus("Aufnahme gestartet: " + fileName);
                UpdateButtons();
            }
            catch (Exception ex)
            {
                StopRecording(false);

                MessageBox.Show(
                    this,
                    ex.Message,
                    "Aufnahme-Fehler",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void StopRecording(bool showMessage)
        {
            if (_recorder == null)
                return;

            var path = _currentRecordingPath;

            try
            {
                _recordTimer.Stop();
                _recorder.Dispose();
            }
            finally
            {
                _recorder = null;
                _currentRecordingPath = null;
                _recordButton.Text = "Aufnahme starten";
                _recordingLabel.Text = "Keine Aufnahme aktiv.";
                UpdateButtons();
            }

            if (showMessage && !string.IsNullOrWhiteSpace(path))
            {
                SetStatus("Aufnahme gespeichert: " + path);

                MessageBox.Show(
                    this,
                    "Aufnahme gespeichert:" + Environment.NewLine + path,
                    "Aufnahme",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }

        private void CaptureRecordingFrame()
        {
            if (_recorder == null)
                return;

            try
            {
                var size = _previewPanel.ClientSize;

                if (size.Width <= 0 || size.Height <= 0)
                    return;

                using (var bitmap = new Bitmap(size.Width, size.Height))
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    var location = _previewPanel.PointToScreen(Point.Empty);
                    graphics.CopyFromScreen(location, Point.Empty, size, CopyPixelOperation.SourceCopy);
                    _recorder.WriteFrame(bitmap);
                }

                var elapsed = DateTime.Now - _recordingStarted;
                _recordingLabel.Text = "Aufnahme läuft: " + elapsed.ToString(@"hh\:mm\:ss") + " | Frames: " + _recorder.FrameCount;
            }
            catch (Exception ex)
            {
                StopRecording(false);
                SetStatus("Aufnahme gestoppt: " + ex.Message);
            }
        }

        private string GetRecordingFolder()
        {
            var configured = GetSetting("RecordingFolder", "Aufnahmen");
            var folder = Path.IsPathRooted(configured)
                ? configured
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, configured);

            Directory.CreateDirectory(folder);
            return folder;
        }

        private void SetInitialForkLine()
        {
            var percent = ReadIntSetting("InitialForkLinePercent", 62, 0, 100);
            var height = Math.Max(1, _previewPanel.ClientSize.Height);
            _forkLineY = height * percent / 100;
            KeepForkLineInside();
            UpdateForkLine();
        }

        private void MoveForkLine(int pixels)
        {
            if (_previewPanel.ClientSize.Height <= 0)
                return;

            _forkLineY += pixels;
            KeepForkLineInside();
            UpdateForkLine();
            _previewPanel.Focus();
        }

        private void KeepForkLineInside()
        {
            var max = Math.Max(0, _previewPanel.ClientSize.Height - _forkLine.Height);
            _forkLineY = Math.Max(0, Math.Min(max, _forkLineY));
        }

        private void UpdateForkLine()
        {
            var width = Math.Max(1, _previewPanel.ClientSize.Width);
            var y = Math.Max(0, Math.Min(Math.Max(0, _previewPanel.ClientSize.Height - _forkLine.Height), _forkLineY));

            _forkLine.SetBounds(0, y, width, 4);
            _forkLineText.SetBounds(14, Math.Max(0, y - 28), 136, 24);
            BringForkLineToFront();
        }

        private void BringForkLineToFront()
        {
            _forkLine.BringToFront();
            _forkLineText.BringToFront();
        }

        private int LineStep()
        {
            return ReadIntSetting("ForkLineStepPixels", 8, 1, 80);
        }

        private void UpdateButtons()
        {
            var hasCamera = _camera != null;
            var isRecording = _recorder != null;

            _startButton.Enabled = !hasCamera;
            _stopButton.Enabled = hasCamera;
            _recordButton.Enabled = hasCamera;
            _refreshButton.Enabled = !isRecording;
            _lineUpButton.Enabled = true;
            _lineDownButton.Enabled = true;
        }

        private void SetStatus(string text)
        {
            _statusLabel.Text = text;
        }

        private static string GetSetting(string key, string fallback = "")
        {
            var value = ConfigurationManager.AppSettings[key];

            if (string.IsNullOrWhiteSpace(value))
                return fallback;

            return value.Trim();
        }

        private static int ReadIntSetting(string key, int fallback, int min, int max)
        {
            var raw = GetSetting(key);

            if (!int.TryParse(raw, out var value))
                value = fallback;

            return Math.Max(min, Math.Min(max, value));
        }

        private sealed class PreviewPanel : Panel
        {
            private const int MouseWheelMessage = 0x020A;

            public event Action<int> WheelMoved;

            public PreviewPanel()
            {
                SetStyle(ControlStyles.Selectable, true);
                TabStop = true;
            }

            protected override void OnMouseEnter(EventArgs e)
            {
                Focus();
                base.OnMouseEnter(e);
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                Focus();
                base.OnMouseDown(e);
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == MouseWheelMessage)
                {
                    var delta = unchecked((short)((m.WParam.ToInt64() >> 16) & 0xffff));
                    WheelMoved?.Invoke(delta);
                    return;
                }

                base.WndProc(ref m);
            }
        }
    }
}
