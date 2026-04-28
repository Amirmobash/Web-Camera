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
        private DirectShowCamera.CameraDevice _selectedDevice;

        private static readonly Color Orange = Color.FromArgb(255, 128, 0);
        private static readonly Color OrangeDark = Color.FromArgb(210, 90, 0);
        private static readonly Color OrangeLight = Color.FromArgb(255, 179, 71);
        private static readonly Color Background = Color.FromArgb(28, 28, 28);
        private static readonly Color PanelDark = Color.FromArgb(38, 38, 38);

        public MainForm()
        {
            Text = "USB-Kamera Vorschau";
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
                Text = "USB-Kamera Vorschau",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 20F, FontStyle.Bold, GraphicsUnit.Point),
                Location = new Point(24, 14),
                Size = new Size(760, 38)
            };

            _statusLabel = new Label
            {
                AutoSize = false,
                Text = "Kamera wird gesucht...",
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

            _refreshButton = CreateOrangeButton("Aktualisieren");
            _refreshButton.Location = new Point(18, 20);
            _refreshButton.Click += (s, e) => RestartCamera();

            _startButton = CreateOrangeButton("Starten");
            _startButton.Location = new Point(158, 20);
            _startButton.Click += (s, e) => StartCamera();

            _stopButton = CreateOrangeButton("Stoppen");
            _stopButton.Location = new Point(298, 20);
            _stopButton.Click += (s, e) => StopCamera("Kamera gestoppt.");

            _hintLabel = new Label
            {
                AutoSize = false,
                Text = "Hinweis: Die Kamera-Auswahl ist absichtlich deaktiviert. Zielkamera in App.config einstellen.",
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
                BackColor = Color.Black,
                Margin = new Padding(18)
            };
            _previewPanel.Resize += (s, e) => _camera?.ResizeVideo(_previewPanel.ClientRectangle);

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

        private Button CreateOrangeButton(string text)
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
            StopCamera("Kamera wird neu geladen...");
            StartCamera();
        }

        private void StartCamera()
        {
            try
            {
                CleanupCamera();
                _selectedDevice = SelectLockedCamera();

                if (_selectedDevice == null)
                    return;

                _camera = new DirectShowCamera(_selectedDevice);
                _camera.StartPreview(_previewPanel.Handle, _previewPanel.ClientRectangle);
                SetStatus("Aktiv: " + _selectedDevice.Name);
                _startButton.Enabled = false;
                _stopButton.Enabled = true;
            }
            catch (Exception ex)
            {
                CleanupCamera();
                SetStatus("Fehler: " + ex.Message);
                MessageBox.Show(this, ex.Message, "Kamera-Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private DirectShowCamera.CameraDevice SelectLockedCamera()
        {
            var devices = DirectShowCamera.EnumerateVideoDevices();
            var targetName = (ConfigurationManager.AppSettings["TargetCameraName"] ?? string.Empty).Trim();
            var matchMode = (ConfigurationManager.AppSettings["CameraMatchMode"] ?? "Contains").Trim();

            if (devices.Count == 0)
            {
                SetStatus("Keine USB/Web-Kamera gefunden.");
                MessageBox.Show(this, "Keine USB/Web-Kamera gefunden.", "Kamera", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }

            if (!string.IsNullOrWhiteSpace(targetName))
            {
                var comparison = StringComparison.OrdinalIgnoreCase;
                var selected = matchMode.Equals("Exact", comparison)
                    ? devices.FirstOrDefault(d => string.Equals(d.Name, targetName, comparison))
                    : devices.FirstOrDefault(d => d.Name.IndexOf(targetName, comparison) >= 0);

                if (selected == null)
                {
                    var names = string.Join(Environment.NewLine, devices.Select(d => "- " + d.Name));
                    SetStatus("Zielkamera nicht gefunden: " + targetName);
                    MessageBox.Show(
                        this,
                        "Die Zielkamera wurde nicht gefunden:\n" + targetName + "\n\nGefundene Kameras:\n" + names,
                        "Zielkamera nicht gefunden",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return null;
                }

                return selected;
            }

            // Kein Auswahlfeld im UI: Wenn kein Zielname gesetzt ist, wird nur die erste Kamera verwendet.
            return devices[0];
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
            if (_camera != null)
            {
                _camera.Dispose();
                _camera = null;
            }
        }

        private void SetStatus(string text)
        {
            _statusLabel.Text = text;
        }
    }
}
