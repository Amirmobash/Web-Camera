using System;
using System.Configuration;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace UsbCameraOrangeDeutsch
{
    public class MainForm : Form
    {
        private readonly Panel _headerPanel;
        private readonly Label _titleLabel;
        private readonly Label _statusLabel;
        private readonly Panel _previewPanel;
        private readonly Panel _footerPanel;
        private readonly Button _startButton;
        private readonly Button _stopButton;
        private readonly Button _refreshButton;
        private readonly Label _hintLabel;

        private DirectShowCamera _camera;
        private DirectShowCamera.CameraDevice _currentCamera;

        private static readonly Color Orange = Color.FromArgb(255, 128, 0);
        private static readonly Color OrangeDark = Color.FromArgb(210, 90, 0);
        private static readonly Color OrangeLight = Color.FromArgb(255, 179, 71);
        private static readonly Color Background = Color.FromArgb(28, 28, 28);
        private static readonly Color PanelDark = Color.FromArgb(38, 38, 38);

        public MainForm()
        {
            Text = "USB Camera Preview";
            MinimumSize = new Size(860, 560);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Background;
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            _headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 92,
                BackColor = Orange
            };

            _titleLabel = new Label
            {
                AutoSize = false,
                Text = "USB Camera Preview",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 20F, FontStyle.Bold, GraphicsUnit.Point),
                Location = new Point(24, 14),
                Size = new Size(760, 38)
            };

            _statusLabel = new Label
            {
                AutoSize = false,
                Text = "Looking for camera...",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point),
                Location = new Point(27, 55),
                Size = new Size(760, 24)
            };

            _headerPanel.Controls.Add(_titleLabel);
            _headerPanel.Controls.Add(_statusLabel);

            _footerPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 82,
                BackColor = PanelDark,
                Padding = new Padding(18)
            };

            _refreshButton = CreateButton("Refresh");
            _refreshButton.Location = new Point(18, 20);
            _refreshButton.Click += (s, e) => RestartCamera();

            _startButton = CreateButton("Start");
            _startButton.Location = new Point(158, 20);
            _startButton.Click += (s, e) => StartCamera();

            _stopButton = CreateButton("Stop");
            _stopButton.Location = new Point(298, 20);
            _stopButton.Enabled = false;
            _stopButton.Click += (s, e) => StopCamera("Camera stopped.");

            _hintLabel = new Label
            {
                AutoSize = false,
                Text = "Camera selection is locked. Set the target camera in App.config.",
                ForeColor = Color.Gainsboro,
                Location = new Point(452, 25),
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
                Size = new Size(360, 30)
            };

            _footerPanel.Controls.Add(_refreshButton);
            _footerPanel.Controls.Add(_startButton);
            _footerPanel.Controls.Add(_stopButton);
            _footerPanel.Controls.Add(_hintLabel);

            _previewPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Black
            };

            _previewPanel.Resize += (s, e) =>
            {
                _camera?.ResizeVideo(_previewPanel.ClientRectangle);
            };

            var previewFrame = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(16),
                BackColor = Background
            };

            previewFrame.Controls.Add(_previewPanel);

            Controls.Add(previewFrame);
            Controls.Add(_footerPanel);
            Controls.Add(_headerPanel);

            Shown += (s, e) => StartCamera();
            FormClosing += (s, e) => CleanupCamera();
        }

        private Button CreateButton(string text)
        {
            var button = new Button
            {
                Text = text,
                Width = 122,
                Height = 40,
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
            StopCamera("Reloading camera...");
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
                    _startButton.Enabled = true;
                    _stopButton.Enabled = false;
                    return;
                }

                _camera = new DirectShowCamera(_currentCamera);
                _camera.StartPreview(_previewPanel.Handle, _previewPanel.ClientRectangle);

                SetStatus("Active: " + _currentCamera.Name);

                _startButton.Enabled = false;
                _stopButton.Enabled = true;
            }
            catch (Exception ex)
            {
                CleanupCamera();

                _startButton.Enabled = true;
                _stopButton.Enabled = false;

                SetStatus("Error: " + ex.Message);

                MessageBox.Show(
                    this,
                    ex.Message,
                    "Camera Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private DirectShowCamera.CameraDevice FindCamera()
        {
            var cameras = DirectShowCamera.EnumerateVideoDevices();

            if (cameras.Count == 0)
            {
                SetStatus("No USB or webcam device found.");

                MessageBox.Show(
                    this,
                    "No USB or webcam device was found.",
                    "Camera",
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

            SetStatus("Target camera not found: " + targetName);

            MessageBox.Show(
                this,
                "The target camera was not found:" +
                Environment.NewLine +
                targetName +
                Environment.NewLine +
                Environment.NewLine +
                "Found cameras:" +
                Environment.NewLine +
                foundCameras,
                "Camera Not Found",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

            return null;
        }

        private static string GetSetting(string key, string fallback = "")
        {
            var value = ConfigurationManager.AppSettings[key];

            if (string.IsNullOrWhiteSpace(value))
                return fallback;

            return value.Trim();
        }

        private void StopCamera(string status)
        {
            CleanupCamera();

            SetStatus(status);

            _startButton.Enabled = true;
            _stopButton.Enabled = false;
        }

        private void CleanupCamera()
        {
            if (_camera == null)
                return;

            _camera.Dispose();
            _camera = null;
        }

        private void SetStatus(string text)
        {
            _statusLabel.Text = text;
        }
    }
}
