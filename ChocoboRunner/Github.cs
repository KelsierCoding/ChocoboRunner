using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using ChocoboRunner.Models.GithubModels;

namespace ChocoboRunner;

public static class Github
{
    public static async Task<string> DownloadLatestReleaseAssetAsync(
        string repo,
        Func<ReleaseModel.Asset, bool>? assetPredicate = null,
        string? personalAccessToken = null,
        string? tempFolder = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repo)) throw new ArgumentNullException(nameof(repo));
        
        tempFolder ??= Path.GetTempPath();
        
        if(!Directory.Exists(tempFolder)) Directory.CreateDirectory(tempFolder);

        try
        {
            using HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ChocoboRunner");
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
            if (!string.IsNullOrEmpty(personalAccessToken))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", personalAccessToken);
            }

            Logger.Info("Trying to downloading latest release asset");
            string releaseUrl = $"https://api.github.com/repos/{repo}/releases/latest";
            ReleaseModel? release = await client.GetFromJsonAsync<ReleaseModel>(releaseUrl, ReleaseModelContext.Default.ReleaseModel, cancellationToken: cancellationToken);
            
            if (release?.Assets == null || release.Assets.Count == 0) return string.Empty;

            assetPredicate ??= (a => a.BrowserDownloadUrl.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
            ReleaseModel.Asset? asset = release.Assets.FirstOrDefault(assetPredicate);
            
            if (asset == null || string.IsNullOrEmpty(asset.BrowserDownloadUrl)) return string.Empty;
            Logger.Info("Latest release asset found");
            return await DownloadAssetInternalAsync(client, asset.BrowserDownloadUrl, asset.Digest, tempFolder, cancellationToken);
        }
        catch(Exception ex)
        {
            Logger.Error(ex.Message);
            return string.Empty;
        }
    }

    private static async Task<string> DownloadAssetInternalAsync(HttpClient client, string assetUrl, string? expectedDigest, string tempFolder, CancellationToken cancellationToken)
    {
        string filePath = string.Empty;
        try
        {
            using HttpResponseMessage resp = await client.GetAsync(assetUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            
            if (!resp.IsSuccessStatusCode) return string.Empty;
            
            string fileName = GetFileNameFromResponse(resp) ?? Path.GetFileName(new Uri(assetUrl).LocalPath);
            
            if (string.IsNullOrEmpty(fileName)) fileName = "downloaded-asset";
            
            filePath = Path.Combine(tempFolder, fileName);

            long? contentLength = resp.Content.Headers.ContentLength;
            const int barWidth = 50;
            long totalRead = 0;
            byte[] buffer = new byte[81920];
            int read;
            DateTime lastRender = DateTime.MinValue;

            await using (Stream responseStream = await resp.Content.ReadAsStreamAsync(cancellationToken))
            await using (FileStream fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                while ((read = await responseStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    totalRead += read;

                    if (!((DateTime.UtcNow - lastRender).TotalMilliseconds >= 50)) continue;
                    RenderProgress(totalRead, contentLength, barWidth);
                    lastRender = DateTime.UtcNow;
                }
                
                await fileStream.FlushAsync(cancellationToken);
            }
            
            RenderProgress(totalRead, contentLength, barWidth);
            ClearCurrentConsoleLine();

            if (!string.IsNullOrEmpty(expectedDigest))
            {
                string expectedHex = expectedDigest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                    ? expectedDigest["sha256:".Length..]
                    : expectedDigest;
                
                expectedHex = expectedHex.Trim().ToLowerInvariant();
                
                try
                {
                    await using FileStream fs = File.OpenRead(filePath);
                    using SHA256 sha256 = SHA256.Create();
                    byte[] hash = await sha256.ComputeHashAsync(fs, cancellationToken);
                    string actualHex = Convert.ToHexStringLower(hash);

                    if (!string.Equals(actualHex, expectedHex, StringComparison.OrdinalIgnoreCase))
                    {
                        TryDeleteFile(filePath);
                        ClearCurrentConsoleLine();
                        return string.Empty;
                    }
                }
                catch
                {
                    TryDeleteFile(filePath);
                    ClearCurrentConsoleLine();
                    return string.Empty;
                }
            }

            Console.WriteLine("Download successful");
            return filePath;
        }
        catch
        {
            if (!string.IsNullOrEmpty(filePath)) TryDeleteFile(filePath);
            try { ClearCurrentConsoleLine(); } catch { /* ignore */ }
            return string.Empty;
        }
    }

    private static void RenderProgress(long totalRead, long? contentLength, int barWidth)
    {
        if (contentLength is > 0)
        {
            double progress = Math.Min(1.0, (double)totalRead / contentLength.Value);
            int filled = (int)Math.Round(progress * barWidth);
            string bar = new string('█', filled) + new string('-', Math.Max(0, barWidth - filled));
            int percent = (int)Math.Round(progress * 100);
            Console.Write($"\r[{bar}] {percent,3}% ({FormatBytes(totalRead)}/{FormatBytes(contentLength.Value)})");
        }
        else
        {
            Console.Write($"\rDownloaded {FormatBytes(totalRead)}");
        }
    }

    private static string FormatBytes(long bytes)
    {
        return bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
            < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
            _ => $"{bytes / (1024.0 * 1024 * 1024):F1} GB"
        };
    }

    private static void ClearCurrentConsoleLine()
    {
        try
        {
            int currentLineCursor = Console.CursorTop;
            Console.SetCursorPosition(0, currentLineCursor);
            int width = Console.WindowWidth;
            if (width <= 0) width = 80;
            Console.Write(new string(' ', width - 1));
            Console.SetCursorPosition(0, currentLineCursor);
        }
        catch
        {
            Console.WriteLine();
        }
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
    }

    private static string? GetFileNameFromResponse(HttpResponseMessage resp)
    {
        if (resp.Content?.Headers?.ContentDisposition != null && !string.IsNullOrEmpty(resp.Content.Headers.ContentDisposition.FileNameStar))
            return TrimQuotes(resp.Content.Headers.ContentDisposition.FileNameStar);

        if (resp.Content?.Headers?.ContentDisposition != null && !string.IsNullOrEmpty(resp.Content.Headers.ContentDisposition.FileName))
            return TrimQuotes(resp.Content.Headers.ContentDisposition.FileName);

        if (resp.Content?.Headers?.TryGetValues("Content-Disposition", out IEnumerable<string>? values) == true)
        {
            string? cd = values.FirstOrDefault();
            if (string.IsNullOrEmpty(cd)) return null;

            Match m = new Regex("filename\\*?=(?:UTF-8'')?\"?(?<name>[^\"]+)\"?", RegexOptions.IgnoreCase)
                .Match(cd);

            if (m.Success) return TrimQuotes(m.Groups["name"].Value);
        }

        return null;
    }

    private static string? TrimQuotes(string s) => s?.Trim().Trim('"') ?? s;
}
