using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Skjermbilde;

public class TrayApp : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly AppSettings _settings;
    private readonly HotkeyWindow _hotkeyWin;
    private SettingsForm? _settingsForm;
    private EditorForm? _editorForm;
    private Rectangle _lastAreaBounds;
    private bool _hasLastArea;
    private UpdateInfo? _updateAvailable;

    public TrayApp()
    {
        _settings = AppSettings.Load();

        // Load tray icon from embedded resource
        Icon trayIcon;
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Skjermbilde.assets.icon_16.png");
            if (stream != null)
            {
                using var bmp = new Bitmap(stream);
                trayIcon = Icon.FromHandle(bmp.GetHicon());
            }
            else
            {
                trayIcon = SystemIcons.Application;
            }
        }
        catch
        {
            trayIcon = SystemIcons.Application;
        }

        _trayIcon = new NotifyIcon
        {
            Icon = trayIcon,
            Text = "Skjermbilde.no",
            Visible = true
        };
        _trayIcon.Click += (_, e) =>
        {
            if (e is MouseEventArgs me && me.Button == MouseButtons.Left)
                CaptureArea();
        };

        UpdateTrayMenu();

        // Register hotkeys
        _hotkeyWin = new HotkeyWindow();
        _hotkeyWin.HotkeyPressed += OnHotkey;
        RegisterHotkeys();

        // Fetch naming format and check for updates
        _ = SyncWithServer();

        // Show settings if not configured
        if (string.IsNullOrEmpty(_settings.ApiKey))
            BeginInvoke(() => ShowSettings());

        // Auto-update timer: check every 4 hours
        var updateTimer = new System.Windows.Forms.Timer { Interval = 4 * 60 * 60 * 1000 };
        updateTimer.Tick += async (_, _) => await CheckForUpdates();
        updateTimer.Start();
    }

    private void BeginInvoke(Action action)
    {
        if (_hotkeyWin.InvokeRequired)
            _hotkeyWin.BeginInvoke(action);
        else
            action();
    }

    private void UpdateTrayMenu()
    {
        var menu = new ContextMenuStrip();

        menu.Items.Add(new ToolStripLabel("Skjermbilde.no") { ForeColor = Color.Gray, Enabled = false });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("📷  Hele skjermen", null, (_, _) => CaptureFullscreen());
        menu.Items.Add("✂️  Velg område", null, (_, _) => CaptureArea());
        menu.Items.Add("🔄  Sist valgte område", null, (_, _) => CaptureLastArea());
        menu.Items.Add(new ToolStripSeparator());

        if (!string.IsNullOrEmpty(_settings.ApiKey))
            menu.Items.Add("🌐  Åpne galleri", null, (_, _) => OpenGallery());
        else
            menu.Items.Add("⚠️  Koble til server", null, (_, _) => ShowSettings());

        menu.Items.Add("⚙️  Innstillinger", null, (_, _) => ShowSettings());

        menu.Items.Add(new ToolStripSeparator());

        if (_updateAvailable != null)
        {
            menu.Items.Add($"⬆️  Oppdater til v{_updateAvailable.Version}", null, async (_, _) => await PerformUpdate());
        }
        else
        {
            menu.Items.Add("🔍  Sjekk for oppdateringer", null, async (_, _) =>
            {
                await CheckForUpdates();
                if (_updateAvailable == null)
                    _trayIcon.ShowBalloonTip(2000, "Oppdatert", "Du har siste versjon.", ToolTipIcon.Info);
            });
        }

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Avslutt", null, (_, _) => ExitApp());

        _trayIcon.ContextMenuStrip = menu;
    }

    private void RegisterHotkeys()
    {
        _hotkeyWin.UnregisterAll();
        _hotkeyWin.RegisterHotkey(1, 0, Keys.PrintScreen);                              // Fullscreen
        _hotkeyWin.RegisterHotkey(2, NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT, Keys.A);  // Area
        _hotkeyWin.RegisterHotkey(3, NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT, Keys.X);  // Last area
    }

    private void OnHotkey(int id)
    {
        switch (id)
        {
            case 1: CaptureFullscreen(); break;
            case 2: CaptureArea(); break;
            case 3: CaptureLastArea(); break;
        }
    }

    private void CaptureFullscreen()
    {
        // Small delay to let any hotkey UI clear
        Task.Delay(150).ContinueWith(_ =>
        {
            BeginInvoke(() =>
            {
                var bmp = ScreenCapture.CaptureFullScreen();
                OpenEditor(bmp);
            });
        });
    }

    private void CaptureArea()
    {
        // Capture screen first (before overlay appears)
        var background = ScreenCapture.CaptureFullScreen();

        using var overlay = new OverlayForm(background);
        var result = overlay.ShowDialog();

        if (result == DialogResult.OK && overlay.SelectedArea.Width > 5)
        {
            var area = overlay.SelectedArea;
            _lastAreaBounds = area;
            _hasLastArea = true;
            var cropped = ScreenCapture.CropBitmap(background, area);

            switch (overlay.Action)
            {
                case OverlayAction.Edit:
                    OpenEditor(cropped);
                    break;
                case OverlayAction.Copy:
                    Clipboard.SetImage(cropped);
                    _trayIcon.ShowBalloonTip(2000, "Kopiert!", "Skjermbildet er kopiert til utklippstavlen.", ToolTipIcon.Info);
                    cropped.Dispose();
                    break;
                case OverlayAction.QuickShare:
                    _ = QuickShare(cropped);
                    break;
                case OverlayAction.Upload:
                    _ = UploadOnly(cropped);
                    break;
                case OverlayAction.Record:
                    cropped.Dispose();
                    StartRecording(area);
                    break;
            }
        }
        background.Dispose();
    }

    private void CaptureLastArea()
    {
        if (!_hasLastArea)
        {
            CaptureArea();
            return;
        }

        Task.Delay(150).ContinueWith(_ =>
        {
            BeginInvoke(() =>
            {
                var full = ScreenCapture.CaptureFullScreen();
                var cropped = ScreenCapture.CropBitmap(full, _lastAreaBounds);
                full.Dispose();
                OpenEditor(cropped);
            });
        });
    }

    private async Task QuickShare(Bitmap bmp)
    {
        var pngData = ScreenCapture.BitmapToPng(bmp);
        var filename = _settings.GenerateLocalFilename();
        bmp.Dispose();

        // Save locally if enabled
        if (_settings.SaveLocal) SaveLocalCopy(pngData, filename);

        var result = await ApiClient.UploadScreenshot(_settings, pngData, filename);
        if (result.Success && result.ScreenshotId != null)
        {
            var shareUrl = await ApiClient.CreateShareLink(_settings, result.ScreenshotId);
            if (shareUrl != null)
            {
                Clipboard.SetText(shareUrl);
                _trayIcon.ShowBalloonTip(2000, "Delt!", "Delingslenken er kopiert til utklippstavlen.", ToolTipIcon.Info);
                return;
            }
        }
        _trayIcon.ShowBalloonTip(2000, "Feil", result.Error ?? "Kunne ikke dele", ToolTipIcon.Error);
    }

    private async Task UploadOnly(Bitmap bmp)
    {
        var pngData = ScreenCapture.BitmapToPng(bmp);
        var filename = _settings.GenerateLocalFilename();
        bmp.Dispose();

        if (_settings.SaveLocal) SaveLocalCopy(pngData, filename);

        var result = await ApiClient.UploadScreenshot(_settings, pngData, filename);
        if (result.Success)
        {
            _trayIcon.ShowBalloonTip(2000, "Lastet opp!", "Skjermbildet er lastet opp til serveren.", ToolTipIcon.Info);
            OpenGallery();
        }
        else
        {
            _trayIcon.ShowBalloonTip(2000, "Feil", result.Error ?? "Kunne ikke laste opp", ToolTipIcon.Error);
        }
    }

    private void StartRecording(Rectangle area)
    {
        _trayIcon.ShowBalloonTip(3000, "Opptak", "Skjermopptak er ikke tilgjengelig enna. Kommer snart!", ToolTipIcon.Info);
    }

    private void SaveLocalCopy(byte[] pngData, string filename)
    {
        try
        {
            var now = DateTime.Now;
            var monthDir = $"{now.Year}-{now.Month:D2}";
            var dir = Path.Combine(_settings.LocalDir, monthDir);
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, filename), pngData);
        }
        catch { }
    }

    private void OpenEditor(Bitmap bmp)
    {
        if (_editorForm != null && !_editorForm.IsDisposed)
        {
            _editorForm.Focus();
            bmp.Dispose();
            return;
        }
        _editorForm = new EditorForm(bmp, _settings);
        _editorForm.FormClosed += (_, _) => _editorForm = null;
        _editorForm.Show();
        bmp.Dispose();
    }

    private void ShowSettings()
    {
        if (_settingsForm != null && !_settingsForm.IsDisposed)
        {
            _settingsForm.Focus();
            return;
        }
        _settingsForm = new SettingsForm(_settings);
        _settingsForm.FormClosed += (_, _) =>
        {
            _settingsForm = null;
            UpdateTrayMenu();
        };
        _settingsForm.Show();
    }

    private void OpenGallery()
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = _settings.ServerUrl + "/dashboard",
            UseShellExecute = true
        });
    }

    private async Task SyncWithServer()
    {
        if (string.IsNullOrEmpty(_settings.ApiKey)) return;

        // Get naming format from server
        var me = await ApiClient.GetMe(_settings);
        if (me?.NamingFormat != null)
        {
            _settings.NamingFormat = me.NamingFormat;
            _settings.Save();
        }

        // Check for updates
        await CheckForUpdates();
    }

    private async Task CheckForUpdates()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        var versionStr = $"{version?.Major}.{version?.Minor}.{version?.Build}";
        _updateAvailable = await ApiClient.CheckForUpdate(_settings, versionStr);
        if (_updateAvailable != null)
        {
            BeginInvoke(() =>
            {
                UpdateTrayMenu();
                _trayIcon.ShowBalloonTip(3000, "Oppdatering tilgjengelig",
                    $"Versjon {_updateAvailable.Version} er klar. Høyreklikk for å oppdatere.", ToolTipIcon.Info);
            });
        }
    }

    private async Task PerformUpdate()
    {
        if (_updateAvailable == null) return;
        try
        {
            using var http = new System.Net.Http.HttpClient();
            var zipPath = Path.Combine(Path.GetTempPath(), "skjermbilde-update.zip");
            var data = await http.GetByteArrayAsync(_updateAvailable.DownloadUrl);
            File.WriteAllBytes(zipPath, data);

            var appDir = Path.GetDirectoryName(Environment.ProcessPath) ?? ".";
            var extractDir = Path.Combine(Path.GetTempPath(), "skjermbilde-update");
            if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, extractDir);

            // Find content dir
            var entries = Directory.GetDirectories(extractDir);
            var sourceDir = entries.Length == 1 ? entries[0] : extractDir;

            var exeName = Path.GetFileName(Environment.ProcessPath) ?? "Skjermbilde.exe";
            var batPath = Path.Combine(Path.GetTempPath(), "skjermbilde-updater.bat");
            var bat = $"@echo off\r\ntimeout /t 3 /nobreak >nul\r\nxcopy /s /y /q \"{sourceDir}\\*\" \"{appDir}\\\"\r\nstart \"\" \"{Path.Combine(appDir, exeName)}\"\r\ndel \"{zipPath}\"\r\nrmdir /s /q \"{extractDir}\"\r\ndel \"%~f0\"\r\n";
            File.WriteAllText(batPath, bat);

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd",
                Arguments = $"/c \"{batPath}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            });

            ExitApp();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Oppdatering feilet: " + ex.Message, "Feil", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ExitApp()
    {
        _hotkeyWin.UnregisterAll();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        Application.Exit();
    }
}

// Hidden window for receiving hotkey messages
public class HotkeyWindow : Form
{
    public event Action<int>? HotkeyPressed;
    private int _nextId = 0;

    public HotkeyWindow()
    {
        ShowInTaskbar = false;
        WindowState = FormWindowState.Minimized;
        FormBorderStyle = FormBorderStyle.None;
        Opacity = 0;
        Show();
        Hide();
    }

    public void RegisterHotkey(int id, uint modifiers, Keys key)
    {
        NativeMethods.RegisterHotKey(Handle, id, modifiers, (uint)key);
        _nextId = Math.Max(_nextId, id + 1);
    }

    public void UnregisterAll()
    {
        for (int i = 0; i < _nextId; i++)
            NativeMethods.UnregisterHotKey(Handle, i);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_HOTKEY)
        {
            HotkeyPressed?.Invoke(m.WParam.ToInt32());
        }
        else if (m.Msg == NativeMethods.WM_SHOWSETTINGS)
        {
            // Second instance requested to show settings
            (Application.OpenForms.Count > 0 ? Application.OpenForms[0] : null)?.Activate();
        }
        base.WndProc(ref m);
    }
}

