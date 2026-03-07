using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Skjermbilde;

public class ApiClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public static async Task<ApiResult> UploadScreenshot(AppSettings settings, byte[] pngData, string filename)
    {
        if (string.IsNullOrEmpty(settings.ApiKey))
            return new ApiResult { Error = "Ingen API-nøkkel konfigurert." };

        try
        {
            using var content = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(pngData);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            content.Add(fileContent, "file", filename);

            using var req = new HttpRequestMessage(HttpMethod.Post, $"{settings.ServerUrl}/api/screenshots");
            req.Headers.Add("x-api-key", settings.ApiKey);
            req.Content = content;

            var resp = await Http.SendAsync(req);
            var json = await resp.Content.ReadAsStringAsync();
            var data = JsonSerializer.Deserialize<JsonElement>(json);

            if (!resp.IsSuccessStatusCode)
                return new ApiResult { Error = data.TryGetProperty("error", out var err) ? err.GetString() : "Opplasting feilet" };

            var id = data.GetProperty("id").GetString();
            return new ApiResult { Success = true, ScreenshotId = id };
        }
        catch (Exception ex)
        {
            return new ApiResult { Error = "Tilkoblingsfeil: " + ex.Message };
        }
    }

    public static async Task<string?> CreateShareLink(AppSettings settings, string screenshotId)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{settings.ServerUrl}/api/share/screenshot/{screenshotId}");
            req.Headers.Add("x-api-key", settings.ApiKey);
            req.Content = new StringContent("{}", Encoding.UTF8, "application/json");

            var resp = await Http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync();
            var data = JsonSerializer.Deserialize<JsonElement>(json);

            if (data.TryGetProperty("url", out var url))
                return url.GetString();
            if (data.TryGetProperty("token", out var token))
                return $"{settings.ServerUrl}/s/{token.GetString()}";
            return null;
        }
        catch { return null; }
    }

    public static async Task<MeResult?> GetMe(AppSettings settings)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{settings.ServerUrl}/api/auth/me");
            req.Headers.Add("x-api-key", settings.ApiKey);
            var resp = await Http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync();
            var data = JsonSerializer.Deserialize<JsonElement>(json);

            var result = new MeResult
            {
                Username = data.TryGetProperty("username", out var u) ? u.GetString() ?? "" : "",
                ScreenshotCount = data.TryGetProperty("screenshot_count", out var sc) ? sc.GetInt32() : 0
            };

            if (data.TryGetProperty("company", out var company) && company.ValueKind == JsonValueKind.Object)
            {
                if (company.TryGetProperty("naming_format", out var nf) && nf.ValueKind == JsonValueKind.String)
                    result.NamingFormat = nf.GetString();
            }

            return result;
        }
        catch { return null; }
    }

    public static async Task<UpdateInfo?> CheckForUpdate(AppSettings settings, string currentVersion)
    {
        try
        {
            var resp = await Http.GetAsync($"{settings.ServerUrl}/api/app/update");
            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync();
            var data = JsonSerializer.Deserialize<JsonElement>(json);

            var serverVersion = data.GetProperty("version").GetString() ?? "";
            var downloadUrl = data.GetProperty("downloadUrl").GetString() ?? "";

            if (CompareVersions(currentVersion, serverVersion) < 0)
                return new UpdateInfo { Version = serverVersion, DownloadUrl = downloadUrl };

            return null;
        }
        catch { return null; }
    }

    private static int CompareVersions(string a, string b)
    {
        var pa = a.Split('.');
        var pb = b.Split('.');
        for (int i = 0; i < 3; i++)
        {
            int va = i < pa.Length && int.TryParse(pa[i], out var x) ? x : 0;
            int vb = i < pb.Length && int.TryParse(pb[i], out var y) ? y : 0;
            if (va < vb) return -1;
            if (va > vb) return 1;
        }
        return 0;
    }
}

public class ApiResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? ScreenshotId { get; set; }
}

public class MeResult
{
    public string Username { get; set; } = "";
    public int ScreenshotCount { get; set; }
    public string? NamingFormat { get; set; }
}

public class UpdateInfo
{
    public string Version { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
}
