using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

public sealed class UpdateManifest
{
    public string version { get; set; }
    public string sha256 { get; set; }
    public long sizeBytes { get; set; }
}

public sealed class PendingUpdate
{
    public string Directory;
    public UpdateManifest Manifest;
}

public sealed class UpdateRequest
{
    public string target { get; set; }
    public int parentId { get; set; }
    public long parentStarted { get; set; }
    public bool restart { get; set; }
    public UpdateManifest manifest { get; set; }
}

public static class AutoUpdater
{
    public const string Repository = "gabriele-gaudissard/Automatic-Github-GitLab-Uploader";
    public const string ManifestUrl = "https://raw.githubusercontent.com/" + Repository + "/main/update.json";
    public const string BinaryUrl = "https://raw.githubusercontent.com/" + Repository + "/main/Git%20Repository%20Uploader.exe";
    public const long MaximumBinarySize = 50 * 1024 * 1024;
    public static Version CurrentVersion { get { return typeof(AutoUpdater).Assembly.GetName().Version; } }
    public static string UpdateRoot { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GithubSetup", "updates"); } }

    public static UpdateManifest ParseManifest(string json)
    {
        if (json == null || json.Length > 8192) throw new InvalidDataException("Update metadata is too large.");
        var manifest = new JavaScriptSerializer().Deserialize<UpdateManifest>(json);
        Version version;
        if (manifest == null || !Version.TryParse(manifest.version, out version) || version.Build < 0 || version.Revision < 0 ||
            !Regex.IsMatch(manifest.sha256 ?? "", @"\A[0-9a-fA-F]{64}\z") || manifest.sizeBytes < 1 || manifest.sizeBytes > MaximumBinarySize)
            throw new InvalidDataException("Invalid update metadata.");
        return manifest;
    }

    public static string Hash(string path)
    {
        using (var stream = File.OpenRead(path))
        using (var hash = SHA256.Create())
            return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }

    public static void VerifyBinary(string path, UpdateManifest manifest)
    {
        ParseManifest(new JavaScriptSerializer().Serialize(manifest));
        if (new FileInfo(path).Length != manifest.sizeBytes || !String.Equals(Hash(path), manifest.sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The downloaded update failed its integrity check.");
        AssemblyName assembly = AssemblyName.GetAssemblyName(path);
        if (assembly.Name != "Git Repository Uploader" || assembly.Version != new Version(manifest.version))
            throw new InvalidDataException("The update is not the expected application version.");
    }

    static HttpClient Client(HttpMessageHandler handler, int seconds)
    {
        var client = handler == null ? new HttpClient() : new HttpClient(handler, false);
        client.Timeout = TimeSpan.FromSeconds(seconds);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("GitRepositoryUploader/" + CurrentVersion);
        client.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
        return client;
    }

    static async Task<byte[]> ReadLimited(HttpClient client, string url, long maximum, CancellationToken token)
    {
        using (HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            if (response.RequestMessage != null && response.RequestMessage.RequestUri.Scheme != "https")
                throw new InvalidDataException("Updates require HTTPS.");
            if (response.Content.Headers.ContentLength > maximum) throw new InvalidDataException("Download exceeds its size limit.");
            using (Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            using (var data = new MemoryStream())
            {
                var buffer = new byte[8192];
                int count;
                while ((count = await stream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false)) != 0)
                {
                    if (data.Length + count > maximum) throw new InvalidDataException("Download exceeds its size limit.");
                    data.Write(buffer, 0, count);
                }
                return data.ToArray();
            }
        }
    }

    // Separate short metadata and longer download deadlines also cover streamed bodies.
    // The optional handler/root let tests exercise the complete flow without the network.
    public static async Task<PendingUpdate> StageAsync(Action<string> state, CancellationToken token, HttpMessageHandler handler = null, string root = null)
    {
        UpdateManifest manifest;
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
        using (var client = Client(handler, 4))
        {
            deadline.CancelAfter(4000);
            byte[] bytes = await ReadLimited(client, ManifestUrl, 8192, deadline.Token).ConfigureAwait(false);
            manifest = ParseManifest(Encoding.UTF8.GetString(bytes));
        }
        if (new Version(manifest.version) <= CurrentVersion) return null;
        state("downloading");
        string directory = Path.Combine(root ?? UpdateRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            using (var client = Client(handler, 60))
            {
                deadline.CancelAfter(60000);
                byte[] bytes = await ReadLimited(client, BinaryUrl, manifest.sizeBytes, deadline.Token).ConfigureAwait(false);
                File.WriteAllBytes(Path.Combine(directory, "new.exe"), bytes);
            }
            VerifyBinary(Path.Combine(directory, "new.exe"), manifest);
            return new PendingUpdate { Directory = directory, Manifest = manifest };
        }
        catch
        {
            // Only files created by this attempt are removed; never touch the app folder.
            string candidate = Path.Combine(directory, "new.exe");
            try { if (File.Exists(candidate)) File.Delete(candidate); } catch (IOException) { }
            try { if (Directory.GetFileSystemEntries(directory).Length == 0) Directory.Delete(directory); } catch (IOException) { }
            throw;
        }
    }

    public static void StartApply(PendingUpdate pending, bool restart)
    {
        VerifyBinary(Path.Combine(pending.Directory, "new.exe"), pending.Manifest);
        string helper = Path.Combine(pending.Directory, "Updater.exe");
        File.Copy(Application.ExecutablePath, helper, true);
        using (Process parent = Process.GetCurrentProcess())
        {
            var request = new UpdateRequest { target = Application.ExecutablePath, parentId = parent.Id,
                parentStarted = parent.StartTime.ToUniversalTime().Ticks, restart = restart, manifest = pending.Manifest };
            string file = Path.Combine(pending.Directory, "request.json");
            File.WriteAllText(file, new JavaScriptSerializer().Serialize(request), new UTF8Encoding(false));
            Process.Start(new ProcessStartInfo(helper, "--apply-update " + GitUploader.Quote(file)) { UseShellExecute = false, CreateNoWindow = true }).Dispose();
        }
    }

    public static int Apply(string requestFile)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(requestFile));
        UpdateRequest request = null;
        bool replaced = false;
        bool validDestination = false;
        string backup = Path.Combine(directory, "previous.exe");
        try
        {
            Guid nonce;
            if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out nonce) || Path.GetFileName(requestFile) != "request.json" ||
                !String.Equals(Path.GetFullPath(Application.ExecutablePath), Path.Combine(directory, "Updater.exe"), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Invalid updater request location.");
            if (new FileInfo(requestFile).Length > 8192) throw new InvalidDataException("Invalid updater request.");
            request = new JavaScriptSerializer().Deserialize<UpdateRequest>(File.ReadAllText(requestFile));
            if (request == null || !Path.IsPathRooted(request.target) || Path.GetFileName(request.target) != "Git Repository Uploader.exe" ||
                AssemblyName.GetAssemblyName(request.target).Name != "Git Repository Uploader")
                throw new InvalidDataException("Invalid update destination.");
            validDestination = true;
            VerifyBinary(Path.Combine(directory, "new.exe"), request.manifest);
            if (new Version(request.manifest.version) <= AssemblyName.GetAssemblyName(request.target).Version)
                throw new InvalidDataException("The destination is already this version or newer.");
            if (request.parentId > 0)
            {
                Process parent = null;
                try { parent = Process.GetProcessById(request.parentId); } catch (ArgumentException) { }
                if (parent != null)
                    using (parent)
                        if (parent.StartTime.ToUniversalTime().Ticks == request.parentStarted && !parent.WaitForExit(60000))
                            throw new IOException("The application did not close in time.");
            }
            // Another open copy may still have the target executable locked.
            for (int attempt = 0; ; attempt++)
                try { File.Replace(Path.Combine(directory, "new.exe"), request.target, backup); replaced = true; break; }
                catch (IOException) { if (attempt >= 19) throw; Thread.Sleep(500); }
            if (request.restart) Process.Start(new ProcessStartInfo(request.target) { UseShellExecute = true }).Dispose();
            try { File.WriteAllText(Path.Combine(directory, "result.txt"), "updated"); } catch (IOException) { }
            return 0;
        }
        catch (Exception ex)
        {
            if (replaced)
                try { File.Replace(backup, request.target, Path.Combine(directory, "failed.exe")); } catch (Exception) { }
            try { File.WriteAllText(Path.Combine(directory, "result.txt"), "failed: " + ex.Message); } catch (Exception) { }
            if (validDestination && request.restart && File.Exists(request.target))
                try { Process.Start(new ProcessStartInfo(request.target, "--skip-update") { UseShellExecute = true }).Dispose(); } catch (Exception) { }
            return 1;
        }
    }
}
