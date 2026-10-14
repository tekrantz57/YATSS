using System.IO.Compression;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace YATSS;

internal sealed class UnoQUpdateClient : IDisposable
{
    internal static readonly string[] Files =
    {
        "app.yaml", "python/main.py", "sketch/sketch.ino", "sketch/sketch.yaml",
        "sketch/src/YatssController/FirmwareVersion.h", "sketch/src/YatssController/YatssController.h"
    };
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal UnoQUpdateClient(string token, HttpMessageHandler? handler = null)
    {
        if (!Regex.IsMatch(token, "\\A[0-9a-f]{64}\\z"))
            throw new InvalidOperationException("Enter the 64-character token from the UNO Q updater's token file.");
        _http = handler == null ? new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false }) : new HttpClient(handler);
        _http.BaseAddress = new Uri("http://127.0.0.1:38593/");
        _http.Timeout = TimeSpan.FromSeconds(25);
        _http.MaxResponseContentBufferSize = 256 * 1024;
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    internal static (byte[] Data, string Version) Bundle(string root)
    {
        var bodies = Files.ToDictionary(name => name, name => File.ReadAllBytes(Path.Combine(root, name)));
        if (bodies.Values.Sum(body => body.Length) > 1024 * 1024)
            throw new InvalidDataException("UNO Q source package is unexpectedly large.");
        string header = System.Text.Encoding.UTF8.GetString(bodies[Files[4]]);
        Match version = Regex.Match(header, "#define YATSSMC_FIRMWARE_VERSION \"([^\"\\r\\n]+)\"");
        if (!version.Success) throw new InvalidDataException("UNO Q firmware version is missing.");
        using MemoryStream stream = new();
        using (ZipArchive zip = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, body) in bodies)
            {
                using Stream entry = zip.CreateEntry(name).Open();
                entry.Write(body);
            }
            using Stream manifest = zip.CreateEntry("manifest.json").Open();
            JsonSerializer.Serialize(manifest, new
            {
                schema = 1, profile = ControllerFirmwareUpdatePolicy.ArduinoUnoQBoardProfile,
                version = version.Groups[1].Value,
                files = bodies.ToDictionary(pair => pair.Key, pair => Convert.ToHexStringLower(SHA256.HashData(pair.Value)))
            }, Json);
        }
        return (stream.ToArray(), version.Groups[1].Value);
    }

    private static async Task<JsonElement> Read(HttpResponseMessage response)
    {
        using (response)
        {
            JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(body.TryGetProperty("error", out var error)
                    ? error.GetString() : "UNO Q updater rejected the request.");
            return body;
        }
    }

    internal async Task PreflightAsync()
    {
        JsonElement response = await Read(await _http.GetAsync("preflight"));
        if (response.GetProperty("profile").GetString() != ControllerFirmwareUpdatePolicy.ArduinoUnoQBoardProfile ||
            response.GetProperty("port").GetInt32() != 45991)
            throw new InvalidOperationException("Helper is not configured for the YATSS UNO Q controller.");
    }

    internal async Task UpdateAsync(byte[] bundle, IProgress<string> progress)
    {
        using ByteArrayContent content = new(bundle);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        JsonElement started = await Read(await _http.PostAsync("update", content));
        string? job = started.GetProperty("job").GetString();
        DateTime deadline = DateTime.UtcNow.AddMinutes(16);
        int shown = 0;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(1000);
            JsonElement status = await Read(await _http.GetAsync("status"));
            if (!status.TryGetProperty("job", out var currentJob) || currentJob.GetString() != job)
                throw new InvalidOperationException("UNO Q helper restarted or changed update jobs; inspect its log before retrying.");
            var messages = status.GetProperty("messages").EnumerateArray().Select(value => value.GetString() ?? "").ToArray();
            foreach (string message in messages.Skip(shown)) progress.Report(message);
            shown = messages.Length;
            string? state = status.GetProperty("state").GetString();
            if (state == "complete") return;
            if (state == "failed") throw new InvalidOperationException(string.Join(Environment.NewLine, messages.TakeLast(3)));
        }
        throw new InvalidOperationException("UNO Q update has not completed. It may still be running; inspect the helper log before retrying.");
    }

    public void Dispose() => _http.Dispose();
}
