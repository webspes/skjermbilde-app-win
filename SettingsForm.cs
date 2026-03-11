using System;
using System.Drawing;
using System.Windows.Forms;

namespace Skjermbilde;

public class SettingsForm : Form
{
    private readonly AppSettings _settings;
    private readonly float _dpiScale;
    private TextBox _apiKeyBox = null!;
    private TextBox _serverUrlBox = null!;
    private TextBox _localDirBox = null!;
    private CheckBox _autoUploadCheck = null!;
    private CheckBox _startupCheck = null!;
    private CheckBox _saveLocalCheck = null!;
    private Label _statusLabel = null!;
    private Label _instanceLabel = null!;
    private Panel _mainPanel = null!;
    private bool _isSetupMode;

    public SettingsForm(AppSettings settings)
    {
        _settings = settings;
        _isSetupMode = string.IsNullOrEmpty(settings.ApiKey);

        using var g = CreateGraphics();
        _dpiScale = g.DpiX / 96f;

        InitializeComponents();
    }

    private int S(int px) => (int)(px * _dpiScale);

    private void InitializeComponents()
    {
        Text = _isSetupMode ? "Skjermbilde.no – Oppsett" : "Skjermbilde.no – Innstillinger";
        ClientSize = _isSetupMode ? new Size(S(420), S(340)) : new Size(S(480), S(620));
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(13, 13, 20);
        ForeColor = Color.FromArgb(240, 240, 255);
        Font = new Font("Segoe UI", 9.5f);
        Padding = new Padding(0);

        try
        {
            using var stream = typeof(SettingsForm).Assembly.GetManifestResourceStream("Skjermbilde.assets.icon_32.png");
            if (stream != null)
            {
                using var bmp = new Bitmap(stream);
                Icon = Icon.FromHandle(bmp.GetHicon());
            }
        }
        catch { }

        _mainPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(S(28), S(24), S(28), S(16)),
            AutoScroll = true,
            BackColor = BackColor
        };
        Controls.Add(_mainPanel);

        if (_isSetupMode)
            BuildSetupView();
        else
            BuildFullSettingsView();
    }

    private void BuildSetupView()
    {
        _mainPanel.Controls.Clear();

        var layout = MakeLayout();
        _mainPanel.Controls.Add(layout);

        int row = 0;

        var header = MakeLabel("Koble til Skjermbilde.no", 15f, FontStyle.Bold, Color.FromArgb(240, 240, 255));
        header.Margin = new Padding(0, 0, 0, S(8));
        layout.Controls.Add(header, 0, row++);

        var desc = MakeLabel("Lim inn API-nokkel fra din Skjermbilde.no-konto for a koble til.", 9.5f, FontStyle.Regular, Color.FromArgb(152, 152, 184));
        desc.Margin = new Padding(0, 0, 0, S(20));
        desc.MaximumSize = new Size(S(380), 0);
        layout.Controls.Add(desc, 0, row++);

        layout.Controls.Add(MakeFieldLabel("API-nokkel"), 0, row++);
        _apiKeyBox = MakeTextBox(_settings.ApiKey, true);
        layout.Controls.Add(_apiKeyBox, 0, row++);

        _statusLabel = new Label
        {
            AutoSize = true,
            Margin = new Padding(0, S(4), 0, S(4)),
            ForeColor = Color.FromArgb(152, 152, 184),
            Font = new Font("Segoe UI", 9f)
        };
        layout.Controls.Add(_statusLabel, 0, row++);

        _instanceLabel = new Label
        {
            AutoSize = true,
            Margin = new Padding(0, 0, 0, S(12)),
            ForeColor = Color.FromArgb(90, 90, 122),
            Font = new Font("Segoe UI", 9f),
            Visible = false
        };
        layout.Controls.Add(_instanceLabel, 0, row++);

        var connectBtn = MakeButton("Koble til", Color.FromArgb(37, 99, 235), Color.White, S(160));
        connectBtn.FlatAppearance.BorderSize = 0;
        connectBtn.Click += async (_, _) => await SetupConnect();
        layout.Controls.Add(connectBtn, 0, row++);

        // Version
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        var verLabel = MakeLabel($"Skjermbilde.no v{version?.Major}.{version?.Minor}.{version?.Build}", 8.5f, FontStyle.Regular, Color.FromArgb(90, 90, 122));
        verLabel.Margin = new Padding(0, S(20), 0, 0);
        layout.Controls.Add(verLabel, 0, row++);
    }

    private void BuildFullSettingsView()
    {
        _mainPanel.Controls.Clear();

        var layout = MakeLayout();
        _mainPanel.Controls.Add(layout);

        int row = 0;

        // Header
        var header = MakeLabel("Skjermbilde.no Innstillinger", 15f, FontStyle.Bold, Color.FromArgb(240, 240, 255));
        header.Margin = new Padding(0, 0, 0, S(16));
        layout.Controls.Add(header, 0, row++);

        // --- Server section ---
        layout.Controls.Add(MakeSectionLabel("Servertilkobling"), 0, row++);

        layout.Controls.Add(MakeFieldLabel("API-nokkel"), 0, row++);
        _apiKeyBox = MakeTextBox(_settings.ApiKey, true);
        layout.Controls.Add(_apiKeyBox, 0, row++);

        // Instance info
        _instanceLabel = new Label
        {
            AutoSize = true,
            Margin = new Padding(0, 0, 0, S(4)),
            ForeColor = Color.FromArgb(34, 211, 165),
            Font = new Font("Segoe UI", 9f),
            Text = !string.IsNullOrEmpty(_settings.InstanceUrl)
                ? $"Tilkoblet: {_settings.InstanceUrl}"
                : $"Tilkoblet: {_settings.ServerUrl}"
        };
        layout.Controls.Add(_instanceLabel, 0, row++);

        // Server URL (advanced, collapsed by default)
        var advLabel = MakeFieldLabel("Server-URL (avansert)");
        advLabel.Cursor = Cursors.Hand;
        advLabel.ForeColor = Color.FromArgb(90, 90, 122);
        layout.Controls.Add(advLabel, 0, row++);

        _serverUrlBox = MakeTextBox(_settings.ServerUrl);
        _serverUrlBox.Visible = false;
        layout.Controls.Add(_serverUrlBox, 0, row++);
        advLabel.Click += (_, _) => _serverUrlBox.Visible = !_serverUrlBox.Visible;

        // Test button + status
        var testRow = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, S(6), 0, S(12)),
            BackColor = Color.Transparent,
            WrapContents = false
        };
        var testBtn = MakeButton("Test tilkobling", Color.FromArgb(30, 30, 45), Color.FromArgb(152, 152, 184), S(140));
        testBtn.Click += async (_, _) => await TestConnection();
        testRow.Controls.Add(testBtn);

        _statusLabel = new Label
        {
            AutoSize = true,
            Margin = new Padding(S(12), S(7), 0, 0),
            ForeColor = Color.FromArgb(152, 152, 184),
            Font = new Font("Segoe UI", 9f)
        };
        testRow.Controls.Add(_statusLabel);
        layout.Controls.Add(testRow, 0, row++);

        // --- Preferences section ---
        layout.Controls.Add(MakeSectionLabel("Innstillinger"), 0, row++);

        _autoUploadCheck = MakeCheckBox("Last opp automatisk", _settings.AutoUpload);
        layout.Controls.Add(_autoUploadCheck, 0, row++);

        _startupCheck = MakeCheckBox("Start med Windows", _settings.LaunchAtStartup);
        layout.Controls.Add(_startupCheck, 0, row++);

        _saveLocalCheck = MakeCheckBox("Lagre lokalt", _settings.SaveLocal);
        layout.Controls.Add(_saveLocalCheck, 0, row++);

        var dirLabel = MakeFieldLabel("Lagringsmappe");
        dirLabel.Margin = new Padding(0, S(8), 0, S(4));
        layout.Controls.Add(dirLabel, 0, row++);
        _localDirBox = MakeTextBox(_settings.LocalDir);
        layout.Controls.Add(_localDirBox, 0, row++);

        var fmtLabel = MakeLabel($"Navneformat fra server: {_settings.NamingFormat}", 8.5f, FontStyle.Regular, Color.FromArgb(90, 90, 122));
        fmtLabel.Margin = new Padding(0, S(4), 0, S(16));
        layout.Controls.Add(fmtLabel, 0, row++);

        // --- Buttons ---
        var btnRow = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Margin = new Padding(0, S(8), 0, S(8)),
            Anchor = AnchorStyles.Right,
            BackColor = Color.Transparent,
            WrapContents = false
        };

        var saveBtn = MakeButton("Lagre innstillinger", Color.FromArgb(37, 99, 235), Color.White, S(160));
        saveBtn.FlatAppearance.BorderSize = 0;
        saveBtn.Click += (_, _) => SaveAndClose();
        btnRow.Controls.Add(saveBtn);

        var cancelBtn = MakeButton("Avbryt", Color.FromArgb(30, 30, 45), Color.FromArgb(152, 152, 184), S(100));
        cancelBtn.Margin = new Padding(0, 0, S(10), 0);
        cancelBtn.Click += (_, _) => Close();
        btnRow.Controls.Add(cancelBtn);

        layout.Controls.Add(btnRow, 0, row++);

        // Version
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        var verLabel = MakeLabel($"Skjermbilde.no v{version?.Major}.{version?.Minor}.{version?.Build}", 8.5f, FontStyle.Regular, Color.FromArgb(90, 90, 122));
        verLabel.Margin = new Padding(0, S(4), 0, 0);
        layout.Controls.Add(verLabel, 0, row++);
    }

    private async System.Threading.Tasks.Task SetupConnect()
    {
        var key = _apiKeyBox.Text.Trim();
        if (string.IsNullOrEmpty(key))
        {
            _statusLabel.Text = "Skriv inn en API-nokkel";
            _statusLabel.ForeColor = Color.FromArgb(240, 86, 86);
            return;
        }

        _statusLabel.Text = "Kobler til...";
        _statusLabel.ForeColor = Color.FromArgb(152, 152, 184);
        _instanceLabel.Visible = false;

        var tempSettings = new AppSettings { ApiKey = key };
        var me = await ApiClient.GetMe(tempSettings);

        if (me != null)
        {
            _settings.ApiKey = key;
            _settings.ServerUrl = tempSettings.ServerUrl;

            if (!string.IsNullOrEmpty(me.NamingFormat))
                _settings.NamingFormat = me.NamingFormat;
            if (!string.IsNullOrEmpty(me.InstanceUrl))
                _settings.InstanceUrl = me.InstanceUrl;

            _settings.Save();

            _statusLabel.Text = $"Tilkoblet som {me.Username} ({me.ScreenshotCount} bilder)";
            _statusLabel.ForeColor = Color.FromArgb(34, 211, 165);

            var displayUrl = !string.IsNullOrEmpty(me.InstanceUrl) ? me.InstanceUrl : _settings.ServerUrl;
            _instanceLabel.Text = displayUrl;
            _instanceLabel.ForeColor = Color.FromArgb(90, 90, 122);
            _instanceLabel.Visible = true;

            // Switch to full settings after a brief delay
            var timer = new Timer { Interval = 1500 };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                timer.Dispose();
                _isSetupMode = false;
                Text = "Skjermbilde.no – Innstillinger";
                ClientSize = new Size(S(480), S(620));
                CenterToScreen();
                BuildFullSettingsView();
            };
            timer.Start();
        }
        else
        {
            _statusLabel.Text = "Kunne ikke koble til. Sjekk API-nokkelen.";
            _statusLabel.ForeColor = Color.FromArgb(240, 86, 86);
        }
    }

    private TableLayoutPanel MakeLayout()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 1,
            Padding = new Padding(0),
            Margin = new Padding(0),
            BackColor = Color.Transparent
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return layout;
    }

    private static Label MakeLabel(string text, float size, FontStyle style, Color color)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            ForeColor = color,
            Font = new Font("Segoe UI", size, style),
            Margin = new Padding(0, 0, 0, 2)
        };
    }

    private static Label MakeSectionLabel(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            ForeColor = Color.FromArgb(200, 200, 220),
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            Margin = new Padding(0, 14, 0, 8)
        };
    }

    private static Label MakeFieldLabel(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            ForeColor = Color.FromArgb(152, 152, 184),
            Font = new Font("Segoe UI", 9f),
            Margin = new Padding(0, 2, 0, 4)
        };
    }

    private TextBox MakeTextBox(string value, bool password = false)
    {
        return new TextBox
        {
            Text = value,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Height = S(28),
            BackColor = Color.FromArgb(22, 22, 34),
            ForeColor = Color.FromArgb(240, 240, 255),
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 10f),
            UseSystemPasswordChar = password,
            Margin = new Padding(0, 0, 0, S(6))
        };
    }

    private CheckBox MakeCheckBox(string text, bool value)
    {
        return new CheckBox
        {
            Text = text,
            Checked = value,
            AutoSize = true,
            ForeColor = Color.FromArgb(240, 240, 255),
            Font = new Font("Segoe UI", 9.5f),
            Margin = new Padding(0, S(4), 0, S(4))
        };
    }

    private Button MakeButton(string text, Color bg, Color fg, int width)
    {
        var btn = new Button
        {
            Text = text,
            Size = new Size(width, S(36)),
            FlatStyle = FlatStyle.Flat,
            BackColor = bg,
            ForeColor = fg,
            Font = new Font("Segoe UI", 9.5f),
            Cursor = Cursors.Hand,
            Margin = new Padding(0)
        };
        btn.FlatAppearance.BorderColor = Color.FromArgb(50, 50, 70);
        return btn;
    }

    private async System.Threading.Tasks.Task TestConnection()
    {
        _statusLabel.Text = "Tester...";
        _statusLabel.ForeColor = Color.FromArgb(152, 152, 184);

        var tempSettings = new AppSettings
        {
            ServerUrl = _serverUrlBox?.Visible == true ? _serverUrlBox.Text.TrimEnd('/') : _settings.ServerUrl,
            ApiKey = _apiKeyBox.Text.Trim()
        };

        var me = await ApiClient.GetMe(tempSettings);
        if (me != null)
        {
            _statusLabel.Text = $"Tilkoblet som {me.Username} ({me.ScreenshotCount} bilder)";
            _statusLabel.ForeColor = Color.FromArgb(34, 211, 165);

            if (!string.IsNullOrEmpty(me.NamingFormat))
                _settings.NamingFormat = me.NamingFormat;
            if (!string.IsNullOrEmpty(me.InstanceUrl))
                _settings.InstanceUrl = me.InstanceUrl;

            var displayUrl = !string.IsNullOrEmpty(me.InstanceUrl) ? me.InstanceUrl : tempSettings.ServerUrl;
            _instanceLabel.Text = $"Tilkoblet: {displayUrl}";
        }
        else
        {
            _statusLabel.Text = "Kunne ikke koble til";
            _statusLabel.ForeColor = Color.FromArgb(240, 86, 86);
        }
    }

    private void SaveAndClose()
    {
        if (_serverUrlBox?.Visible == true)
            _settings.ServerUrl = _serverUrlBox.Text.TrimEnd('/');
        _settings.ApiKey = _apiKeyBox.Text.Trim();
        _settings.AutoUpload = _autoUploadCheck.Checked;
        _settings.LaunchAtStartup = _startupCheck.Checked;
        _settings.SaveLocal = _saveLocalCheck.Checked;
        _settings.LocalDir = _localDirBox.Text.Trim();
        _settings.Save();

        StartupManager.SetStartup(_settings.LaunchAtStartup);

        DialogResult = DialogResult.OK;
        Close();
    }
}
