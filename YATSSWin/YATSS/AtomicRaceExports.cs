using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace YATSS;

// Each replacement is atomic; the receipt is published last to identify a
// complete bundle. An interrupted bundle is rebuilt at the same event paths.
internal static class AtomicRaceExports
{
    internal static void Write(IReadOnlyList<(string Path, Func<string> Render)> artifacts,
        RaceExportPlan? plan, string? receiptPath, Action<int>? published)
    {
        List<(string Path, string Sha256)> hashes = new();
        List<string> staged = new();
        try
        {
            foreach (var artifact in artifacts)
            {
                string temporary = artifact.Path + ".yatss-tmp";
                staged.Add(temporary);
                hashes.Add((artifact.Path, Stage(temporary, artifact.Render())));
            }
            if (receiptPath != null)
            {
                string receipt = JsonSerializer.Serialize(new
                {
                    schemaVersion = 1, eventId = plan!.EventId, createdAt = plan.CreatedAt,
                    files = hashes.Select(file => new { name = Path.GetFileName(file.Path), sha256 = file.Sha256 })
                });
                staged.Add(receiptPath + ".yatss-tmp");
                Stage(staged[^1], receipt);
                published?.Invoke(0);
                // A previous receipt must not identify a partially replaced bundle.
                File.Delete(receiptPath);
            }
            for (int index = 0; index < artifacts.Count; index++)
            {
                File.Move(staged[index], artifacts[index].Path, overwrite: true);
                published?.Invoke(index + 1);
            }
            if (receiptPath != null) File.Move(staged[^1], receiptPath, overwrite: true);
            published?.Invoke(artifacts.Count + 1);
        }
        finally
        {
            foreach (string path in staged)
            {
                try { File.Delete(path); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static string Stage(string path, string contents)
    {
        using FileStream stream = new(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None,
            65536, FileOptions.WriteThrough);
        using (StreamWriter writer = new(stream, new UTF8Encoding(false), 65536, leaveOpen: true))
            writer.Write(contents);
        stream.Flush(flushToDisk: true);
        stream.Position = 0;
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
