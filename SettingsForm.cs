using System;
using System.Drawing;
using System.Windows.Forms;

namespace Skjermbilde;

public class SettingsForm : Form
{
    private readonly AppSettings _settings;
    private TextBox _serverUrlBox = null!;
    private TextBox _apiKeyBox = null!;
    private TextBox _localDirBox = null!;
    private CheckBox _autoUploadCheck = null!;
    private CheckBox _startupCheck = null!;
    private CheckBox _saveLocalCheck = null!;
    private Label _statusLabel = null!;
    private Label _versionLabel = null!;

    public SettingsForm(AppSettings settings)
    {
        _settings = settings;
        InitializeComponents();
    }

    private void InitializeComponents()
    {
        Text = "Skjermbilde.no – Innstillinger";
        Size = new Size(500, 560);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(13, 13, 20);
        ForeColor = Color.FromArgb(240, 240, 255);
        Font = new Font("Segoe UI", 9.5f);

        try
        {
            using var stream = typeof(SettingsForm).Assembly.GetManifestResourceStream("Skjermbilde.assets.icon_32.png");
            if (stream != null) Icon = Icon.FromHandle(new Bitmap(stream).GetHicon());
        }
        catch { }

        var y = 20;

        // Header
        AddLabel("Skjermbilde.no Innstillinger", 20, y, 14f, FontStyle.Bold);
        y += 40;

        // Server section
        AddLabel("Server-URL", 20, y);
        y += 22;
        _serverUrlBox = AddTextBox(_settings.ServerUrl, 20, y);
        y += 36;

        AddLabel("API-nøkkel", 20, y);
        y += 22;
        _apiKeyBox = AddTextBox(_settings.ApiKey, 20, y, true);
        y += 32;

        var testBtn = new Button
        {
            Text = "Test tilkobling",
            Location = new Point(20, y),
            Size = new Size(130, 30),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(30, 30, 45),
            ForeColor = Color.FromArgb(152, 152, 184)
        };
        testBtn.FlatAppearance.BorderColor = Color.FromArgb(50, 50, 70);
        testBtn.Click += async (_, _) => await TestConnection();
        Controls.Add(testBtn);

        _statusLabel = new Label
        {
            Location = new Point(160, y + 5),
            Size = new Size(300, 20),
            ForeColor = Color.FromArgb(152, 152, 184)
        };
        Controls.Add(_statusLabel);
        y += 42;

        // Preferences
        AddLabel("Innstillinger", 20, y, 11f, FontStyle.Bold);
        y += 28;

        _autoUploadCheck = AddCheckBox("Last opp automatisk", _settings.AutoUpload, 20, y);
        y += 28;
        _startupCheck = AddCheckBox("Start med Windows", _settings.LaunchAtStartup, 20, y);
        y += 28;
        _saveLocalCheck = AddCheckBox("Lagre lokalt", _settings.SaveLocal, 20, y);
        y += 32;

        AddLabel("Lagringsmappe", 20, y);
        y += 22;
        _localDirBox = AddTextBox(_settings.LocalDir, 20, y);
        y += 36;

        AddLabel($"Navneformat fra server: {_settings.NamingFormat}", 20, y);
        y += 30;

        // Buttons
        var saveBtn = new Button
        {
            Text = "Lagre innstillinger",
            Location = new Point(Width - 180, y + 10),
            Size = new Size(140, 36),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(37, 99, 235),
            ForeColor = Color.White
        };
        saveBtn.FlatAppearance.BorderSize = 0;
        saveBtn.Click += (_, _) => SaveAndClose();
        Controls.Add(saveBtn);

        var cancelBtn = new Button
        {
            Text = "Avbryt",
            Location = new Point(Width - 320, y + 10),
            Size = new Size(120, 36),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(30, 30, 45),
            ForeColor = Color.FromArgb(152, 152, 184)
        };
        cancelBtn.FlatAppearance.BorderColor = Color.FromArgb(50, 50, 70);
        cancelBtn.Click += (_, _) => Close();
        Controls.Add(cancelBtn);

        // Version
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        _versionLabel = new Label
        {
            Text = $"Skjermbilde.no v{version?.Major}.{version?.Minor}.{version?.Build}",
            Location = new Point(20, y + 18),
            Size = new Size(200, 20),
            ForeColor = Color.FromArgb(90, 90, 122),
            Font = new Font("Segoe UI", 8.5f)
        };
        Controls.Add(_versionLabel);
    }

    private Label AddLabel(string text, int x, int y, float size = 9.5f, FontStyle style = FontStyle.Regular)
    {
        var lbl = new Label
        {
            Text = text,
            Location = new Point(x, y),
            AutoSize = true,
            ForeColor = Color.FromArgb(152, 152, 184),
            Font = new Font("Segoe UI", size, style)
        };
        Controls.Add(lbl);
        return lbl;
    }

    private TextBox AddTextBox(string value, int x, int y, bool password = false)
    {
        var tb = new TextBox
        {
            Text = value,
            Location = new Point(x, y),
            Size = new Size(Width - 60, 26),
            BackColor = Color.FromArgb(22, 22, 34),
            ForeColor = Color.FromArgb(240, 240, 255),
            BorderStyle = BorderStyle.FixedSingle,
            UseSystemPasswordChar = password
        };
        Controls.Add(tb);
        return tb;
    }

    private CheckBox AddCheckBox(string text, bool value, int x, int y)
    {
        var cb = new CheckBox
        {
            Text = text,
            Checked = value,
            Location = new Point(x, y),
            AutoSize = true,
            ForeColor = Color.FromArgb(240, 240, 255)
        };
        Controls.Add(cb);
        return cb;
    }

    private async System.Threading.Tasks.Task TestConnection()
    {
        _statusLabel.Text = "Tester...";
        _statusLabel.ForeColor = Color.FromArgb(152, 152, 184);

        var tempSettings = new AppSettings
        {
            ServerUrl = _serverUrlBox.Text.TrimEnd('/'),
            ApiKey = _apiKeyBox.Text.Trim()
        };

        var me = await ApiClient.GetMe(tempSettings);
        if (me != null)
        {
            _statusLabel.Text = $"✓ {me.Username} ({me.ScreenshotCount} bilder)";
            _statusLabel.ForeColor = Color.FromArgb(34, 211, 165);

            // Update naming format from server
            if (!string.IsNullOrEmpty(me.NamingFormat))
                _settings.NamingFormat = me.NamingFormat;
        }
        else
        {
            _statusLabel.Text = "✗ Kunne ikke koble til";
            _statusLabel.ForeColor = Color.FromArgb(240, 86, 86);
        }
    }

    private void SaveAndClose()
    {
        _settings.ServerUrl = _serverUrlBox.Text.TrimEnd('/');
        _settings.ApiKey = _apiKeyBox.Text.Trim();
        _settings.AutoUpload = _autoUploadCheck.Checked;
        _settings.LaunchAtStartup = _startupCheck.Checked;
        _settings.SaveLocal = _saveLocalCheck.Checked;
        _settings.LocalDir = _localDirBox.Text.Trim();
        _settings.Save();

        // Update Windows startup
        StartupManager.SetStartup(_settings.LaunchAtStartup);

        DialogResult = DialogResult.OK;
        Close();
    }
}
