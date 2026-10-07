using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

static class UpdateTests
{
    static void Assert(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }
    static void Reject(Action action, string description)
    {
        bool failed = false;
        try { action(); } catch (Exception) { failed = true; }
        Assert(failed, description);
    }
    sealed class FakeServer : HttpMessageHandler
    {
        public string Json;
        public byte[] Binary;
        public int Downloads;
        public bool Offline;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (Offline) throw new HttpRequestException("Offline");
            var response = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request };
            if (request.RequestUri.AbsoluteUri == AutoUpdater.ManifestUrl)
                response.Content = new StringContent(Json, Encoding.UTF8, "application/json");
            else {
                Assert(request.RequestUri.AbsoluteUri == AutoUpdater.BinaryUrl, "Download updates only from the configured repository");
                Downloads++;
                response.Content = new ByteArrayContent(Binary);
            }
            return Task.FromResult(response);
        }
    }
    static string AppSource()
    {
        string besideWork = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(typeof(UpdateTests).Assembly.Location)), "Git Repository Uploader.exe");
        if (File.Exists(besideWork)) return besideWork;
        string relative = Path.Combine(Environment.CurrentDirectory, "Git Repository Uploader.exe");
        if (File.Exists(relative)) return relative;
        return Path.Combine(Environment.CurrentDirectory, "outputs", "Git Repository Uploader", "Git Repository Uploader.exe");
    }
    static UpdateManifest Manifest(string file, string version)
    {
        return new UpdateManifest { version = version, sha256 = AutoUpdater.Hash(file), sizeBytes = new FileInfo(file).Length };
    }
    static int Helper(PendingUpdate pending, string target, bool corruptRequest)
    {
        string helper = Path.Combine(pending.Directory, "Updater.exe");
        File.Copy(AppSource(), helper, true);
        string requestFile = Path.Combine(pending.Directory, "request.json");
        var request = new UpdateRequest { target = target, parentId = 0, parentStarted = 0, restart = false, manifest = pending.Manifest };
        if (corruptRequest) request.manifest.sha256 = new string('0', 64);
        File.WriteAllText(requestFile, new JavaScriptSerializer().Serialize(request));
        using (var process = Process.Start(new ProcessStartInfo(helper, "--apply-update " + GitUploader.Quote(requestFile)) { UseShellExecute = false, CreateNoWindow = true }))
        {
            if (!process.WaitForExit(15000)) { process.Kill(); throw new Exception("Updater helper timed out."); }
            return process.ExitCode;
        }
    }
    public static void Run(string root)
    {
        string tests = Path.Combine(root, "app-updates");
        Directory.CreateDirectory(tests);
        string candidateDir = Path.Combine(tests, "candidate");
        Directory.CreateDirectory(candidateDir);
        string candidate = Path.Combine(candidateDir, "Git Repository Uploader.exe");
        string code = Path.Combine(tests, "FutureVersion.cs");
        // A real managed executable with a later version lets the tests verify
        // metadata, staging, self replacement and backup without contacting GitHub.
        Version next = new Version(AutoUpdater.CurrentVersion.Major, AutoUpdater.CurrentVersion.Minor, AutoUpdater.CurrentVersion.Build + 1, 0);
        File.WriteAllText(code, "using System.Reflection; [assembly: AssemblyVersion(\"" + next + "\")] class FutureVersion { static void Main() {} }");
        string compiler = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET", "Framework64", "v4.0.30319", "csc.exe");
        if (!File.Exists(compiler)) compiler = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET", "Framework", "v4.0.30319", "csc.exe");
        using (var process = Process.Start(new ProcessStartInfo(compiler, "/nologo /target:winexe /out:" + GitUploader.Quote(candidate) + " " + GitUploader.Quote(code)) { UseShellExecute = false, CreateNoWindow = true }))
        { process.WaitForExit(); Assert(process.ExitCode == 0, "Build a later-version update fixture"); }
        UpdateManifest manifest = Manifest(candidate, next.ToString());
        var serializer = new JavaScriptSerializer();
        string json = serializer.Serialize(manifest);
        Assert(AutoUpdater.ParseManifest(json).version == next.ToString(), "Parse valid update metadata");
        Reject(delegate { AutoUpdater.ParseManifest("{}"); }, "Reject incomplete metadata");
        Reject(delegate { AutoUpdater.ParseManifest(json.Replace(next.ToString(), "not-a-version")); }, "Reject invalid update version");
        Reject(delegate { AutoUpdater.ParseManifest(json.Replace(manifest.sha256, "bad")); }, "Reject invalid update hash");
        Reject(delegate { AutoUpdater.ParseManifest(new string('x', 8193)); }, "Reject oversized update metadata");
        var oversized = new UpdateManifest { version = next.ToString(), sha256 = manifest.sha256, sizeBytes = AutoUpdater.MaximumBinarySize + 1 };
        Reject(delegate { AutoUpdater.ParseManifest(serializer.Serialize(oversized)); }, "Reject excessive executable sizes");
        AutoUpdater.VerifyBinary(candidate, manifest);
        Assert(true, "Validate hash, length, application identity and binary version");
        var wrongVersion = Manifest(candidate, "99.0.0.0");
        Reject(delegate { AutoUpdater.VerifyBinary(candidate, wrongVersion); }, "Reject a binary with the wrong version");
        using (var same = new FakeServer { Json = serializer.Serialize(Manifest(AppSource(), AutoUpdater.CurrentVersion.ToString())), Binary = File.ReadAllBytes(candidate) })
        {
            PendingUpdate pending = AutoUpdater.StageAsync(delegate {}, CancellationToken.None, same, tests).GetAwaiter().GetResult();
            Assert(pending == null && same.Downloads == 0, "Current version does not download or restart");
        }
        PendingUpdate update;
        using (var server = new FakeServer { Json = json, Binary = File.ReadAllBytes(candidate) })
        {
            update = AutoUpdater.StageAsync(delegate {}, CancellationToken.None, server, tests).GetAwaiter().GetResult();
            Assert(server.Downloads == 1 && AutoUpdater.Hash(Path.Combine(update.Directory, "new.exe")) == manifest.sha256, "Stage a verified newer version");
        }
        using (var broken = new FakeServer { Json = json, Binary = new byte[(int)manifest.sizeBytes] })
            Reject(delegate { AutoUpdater.StageAsync(delegate {}, CancellationToken.None, broken, tests).GetAwaiter().GetResult(); }, "Reject tampered downloads before installation");
        using (var offline = new FakeServer { Offline = true })
            Reject(delegate { AutoUpdater.StageAsync(delegate {}, CancellationToken.None, offline, tests).GetAwaiter().GetResult(); }, "Network failure leaves the current application intact");
        using (var cancelled = new CancellationTokenSource())
        using (var server = new FakeServer { Json = json })
        {
            cancelled.Cancel();
            Reject(delegate { AutoUpdater.StageAsync(delegate {}, cancelled.Token, server, tests).GetAwaiter().GetResult(); }, "Closing the app cancels update checks");
        }
        string install = Path.Combine(tests, "installation");
        Directory.CreateDirectory(install);
        string target = Path.Combine(install, "Git Repository Uploader.exe");
        File.Copy(AppSource(), target);
        string oldHash = AutoUpdater.Hash(target);
        File.WriteAllText(Path.Combine(install, "Github setup.txt"), "personal file");
        Directory.CreateDirectory(Path.Combine(install, ".git"));
        File.WriteAllText(Path.Combine(install, ".git", "config"), "existing Git metadata");
        Assert(Helper(update, target, false) == 0 && AutoUpdater.Hash(target) == manifest.sha256, "Helper installs the verified newer executable");
        Assert(AutoUpdater.Hash(Path.Combine(update.Directory, "previous.exe")) == oldHash, "Keep the previous executable as a backup");
        Assert(File.ReadAllText(Path.Combine(install, "Github setup.txt")) == "personal file" && File.ReadAllText(Path.Combine(install, ".git", "config")) == "existing Git metadata", "Updating preserves personal files and Git metadata");
        PendingUpdate invalid;
        using (var server = new FakeServer { Json = json, Binary = File.ReadAllBytes(candidate) })
            invalid = AutoUpdater.StageAsync(delegate {}, CancellationToken.None, server, tests).GetAwaiter().GetResult();
        Assert(Helper(invalid, target, true) != 0 && AutoUpdater.Hash(target) == manifest.sha256, "Invalid helper payload cannot change the installed application");
        invalid.Manifest = Manifest(candidate, next.ToString());
        Assert(Helper(invalid, target, false) != 0 && AutoUpdater.Hash(target) == manifest.sha256, "Never replace an installed version with the same or an older version");
    }
}
