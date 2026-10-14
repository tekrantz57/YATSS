using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using YATSS;

internal static class UnoQUpdateTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    internal static void Run() => RunAsync().GetAwaiter().GetResult();

    private static async Task RunAsync()
    {
        string source = Path.Combine(AppContext.BaseDirectory, "Linux", "UNOQ");
        var bundle = UnoQUpdateClient.Bundle(source);
        using (ZipArchive zip = new(new MemoryStream(bundle.Data)))
        {
            Check(zip.Entries.Count == 7, "UNO Q package must contain only six source files and its manifest");
            Check(zip.Entries.All(entry => !entry.FullName.Contains('\\')), "Package paths must be portable");
            using var manifest = JsonDocument.Parse(zip.GetEntry("manifest.json")!.Open());
            Check(manifest.RootElement.GetProperty("version").GetString() == bundle.Version, "Manifest version must match header");
            foreach (string name in UnoQUpdateClient.Files)
            {
                using Stream entry = zip.GetEntry(name)!.Open();
                string hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(entry));
                Check(manifest.RootElement.GetProperty("files").GetProperty(name).GetString() == hash, "Source hash mismatch");
            }
        }
        try { using var invalid = new UnoQUpdateClient("wrong"); throw new Exception("Invalid token accepted"); }
        catch (InvalidOperationException) { }

        using var handler = new HelperHandler();
        using var client = new UnoQUpdateClient(new string('a', 64), handler);
        await client.PreflightAsync();
        await client.UpdateAsync(bundle.Data, new ImmediateProgress());
        Check(handler.Uploaded, "UNO Q bundle should be uploaded after preflight");
        handler.State = "failed";
        try { await client.UpdateAsync(bundle.Data, new ImmediateProgress()); throw new Exception("Failed update accepted"); }
        catch (InvalidOperationException ex) { Check(ex.Message.Contains("Verified"), "Job failure must retain helper details"); }
        handler.State = "idle";
        try { await client.UpdateAsync(bundle.Data, new ImmediateProgress()); throw new Exception("Lost update job accepted"); }
        catch (InvalidOperationException ex) { Check(ex.Message.Contains("restarted"), "Lost job must not imply success"); }
        handler.Fail = true;
        try { await client.PreflightAsync(); throw new Exception("Helper failure accepted"); }
        catch (InvalidOperationException ex) { Check(ex.Message == "fixture failure", "Helper error should reach user"); }
        Console.WriteLine("UNO Q update client tests passed.");
    }

    private sealed class ImmediateProgress : IProgress<string>
    {
        public void Report(string value) { }
    }

    private sealed class HelperHandler : HttpMessageHandler
    {
        internal bool Fail, Uploaded;
        internal string State = "complete";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Check(request.RequestUri!.Host == "127.0.0.1", "Updater must stay on loopback");
            Check(request.Headers.Authorization?.Parameter == new string('a', 64), "Updater must authenticate every request");
            string result;
            if (Fail) result = "{\"error\":\"fixture failure\"}";
            else if (request.Method == HttpMethod.Post)
            {
                Check((await request.Content!.ReadAsByteArrayAsync(cancellationToken)).Length > 0, "Uploaded package missing");
                Uploaded = true; result = "{\"job\":\"test\",\"state\":\"running\"}";
            }
            else if (request.RequestUri.AbsolutePath == "/preflight")
                result = "{\"profile\":\"ARDUINO_UNO_Q_STM32U585\",\"port\":45991}";
            else result = State == "idle" ? "{\"state\":\"idle\",\"messages\":[]}" :
                "{\"job\":\"test\",\"state\":\"" + State + "\",\"messages\":[\"Verified\"]}";
            return new HttpResponseMessage(Fail ? HttpStatusCode.BadRequest : HttpStatusCode.OK)
            { Content = new StringContent(result, Encoding.UTF8, "application/json") };
        }
    }
}
