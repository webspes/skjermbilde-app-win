using System;
using System.IO;
using System.Text.Json;

namespace Skjermbilde;

public class AppSettings
{
    public string ServerUrl { get; set; } = "https://skjermbilde.no";
    public string ApiKey { get; set; } = "";
    public string HotkeyFullscreen { get; set; } = "PrintScreen";
    public string HotkeyArea { get; set; } = "Ctrl+Shift+A";
    public string HotkeyLast { get; set; } = "Ctrl+Shift+X";
    public bool AutoUpload { get; set; } = true;
    public bool SaveLocal { get; set; } = false;
    public string LocalDir { get; set; } = @"C:\Skjermbilder";
    public bool LaunchAtStartup { get; set; } = false;
    public string NamingFormat { get; set; } = "{year}-{number}";
    public string InstanceUrl { get; set; } = "";
    public int NextLocalNumber { get; set; } = 1;

    public string PublicBaseUrl => !string.IsNullOrEmpty(InstanceUrl) ? InstanceUrl : ServerUrl;

    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Skjermbilde");
    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOpts) ?? new AppSettings();
            }
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(this, JsonOpts);
            File.WriteAllText(SettingsPath, json);
        }
        catch { }
    }

    public string GenerateLocalFilename(string ext = ".png")
    {
        var now = DateTime.Now;
        var num = NextLocalNumber++;
        var padNum = num.ToString("D2");
        var name = NamingFormat
            .Replace("{year}", now.Year.ToString())
            .Replace("{month}", now.Month.ToString("D2"))
            .Replace("{day}", now.Day.ToString("D2"))
            .Replace("{number}", padNum);
        Save(); // persist incremented counter
        return name + ext;
    }
}
