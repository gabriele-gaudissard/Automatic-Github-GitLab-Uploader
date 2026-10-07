using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

public sealed class GitResult
{
    public int Code;
    public string Output;
    public string Error;
}

public sealed class GitUploader
{
    static string T(string english, string italian) { return UiText.Get(english, italian); }
    readonly string git;
    readonly Action<string> log;
    public GitUploader(string gitPath, Action<string> logger) { git = gitPath; log = logger; }

    public static string FindGit()
    {
        var candidates = new List<string>();
        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            if (!String.IsNullOrWhiteSpace(dir)) candidates.Add(Path.Combine(dir.Trim('"'), "git.exe"));
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "cmd", "git.exe"));
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Git", "cmd", "git.exe"));
        foreach (string path in candidates) if (File.Exists(path)) return path;
        throw new InvalidOperationException(T("Git is not installed. Install Git for Windows from https://git-scm.com/download/win and reopen the app.", "Git non è installato. Installa Git for Windows da https://git-scm.com/download/win e riapri il programma."));
    }

    // Quote each Windows argument independently; never execute through a shell.
    public static string Quote(string value)
    {
        var result = new StringBuilder("\"");
        int slashes = 0;
        foreach (char c in value)
        {
            if (c == '\\') { slashes++; continue; }
            if (c == '"') result.Append('\\', slashes * 2 + 1);
            else result.Append('\\', slashes);
            result.Append(c);
            slashes = 0;
        }
        result.Append('\\', slashes * 2);
        return result.Append('"').ToString();
    }

    public GitResult Run(string folder, params string[] args)
    {
        var quoted = new List<string> { Quote("-c"), Quote("core.longpaths=true") };
        foreach (string arg in args) quoted.Add(Quote(arg));
        var info = new ProcessStartInfo(git, String.Join(" ", quoted.ToArray()));
        info.WorkingDirectory = folder;
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        info.RedirectStandardInput = true;
        info.StandardOutputEncoding = Encoding.UTF8;
        info.StandardErrorEncoding = Encoding.UTF8;
        info.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
        info.EnvironmentVariables["GCM_INTERACTIVE"] = "always";
        info.EnvironmentVariables["GCM_GITLAB_AUTHMODES"] = "browser";
        var output = new StringBuilder();
        var error = new StringBuilder();
        using (var process = new Process())
        {
            process.StartInfo = info;
            process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) {
                if (e.Data != null) { lock (output) output.AppendLine(e.Data); }
            };
            process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) {
                if (e.Data != null) { lock (error) error.AppendLine(e.Data); }
            };
            process.Start();
            process.StandardInput.Close();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();
            return new GitResult { Code = process.ExitCode, Output = output.ToString().Trim(), Error = error.ToString().Trim() };
        }
    }

    GitResult Check(string folder, params string[] args)
    {
        GitResult result = Run(folder, args);
        if (result.Code != 0)
            throw new InvalidOperationException((result.Error + "\n" + result.Output).Trim());
        return result;
    }

    static string Canonical(string path) { return Path.GetFullPath(path).TrimEnd('\\', '/'); }

    string Prepare(string folder, bool first)
    {
        if (!Directory.Exists(folder)) throw new InvalidOperationException(T("The selected folder does not exist.", "La cartella selezionata non esiste."));
        GitResult root = Run(folder, "rev-parse", "--show-toplevel");
        if (root.Code == 0)
        {
            if (!String.Equals(Canonical(root.Output), Canonical(folder), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(T("The selected folder is inside another repository. Select its root folder:\n", "La cartella selezionata è dentro un altro repository. Seleziona la cartella principale:\n") + root.Output);
        }
        else
        {
            if (!first) throw new InvalidOperationException(T("This folder is not an accessible Git repository. Select First upload first.\n", "Questa cartella non è un repository Git accessibile. Seleziona prima Primo caricamento.\n") + root.Error);
            if (Directory.Exists(Path.Combine(folder, ".git")) || File.Exists(Path.Combine(folder, ".git")))
                throw new InvalidOperationException(root.Error);
            log(T("Initializing the folder...", "Inizializzazione della cartella..."));
            Check(folder, "init", "-b", "main");
        }
        GitResult branch = Check(folder, "symbolic-ref", "--quiet", "--short", "HEAD");
        if (String.IsNullOrWhiteSpace(branch.Output)) throw new InvalidOperationException(T("Select a branch before uploading.", "Seleziona un branch prima di caricare."));
        foreach (string marker in new [] { "MERGE_HEAD", "CHERRY_PICK_HEAD", "REVERT_HEAD", "rebase-merge", "rebase-apply", "BISECT_LOG" })
        {
            string markerPath = Check(folder, "rev-parse", "--git-path", marker).Output;
            if (!Path.IsPathRooted(markerPath)) markerPath = Path.Combine(folder, markerPath);
            if (File.Exists(markerPath) || Directory.Exists(markerPath))
                throw new InvalidOperationException(T("A Git operation is in progress. Complete it before uploading.", "È in corso un'operazione Git. Completala prima del caricamento."));
        }
        if (Check(folder, "diff", "--name-only", "--diff-filter=U").Output.Length > 0)
            throw new InvalidOperationException(T("Resolve the existing conflicts before uploading.", "Ci sono conflitti da risolvere prima del caricamento."));
        return branch.Output;
    }

    void Commit(string folder, string message)
    {
        if (String.IsNullOrWhiteSpace(message)) throw new InvalidOperationException(T("Enter a commit message.", "Scrivi il nome del commit."));
        log(T("Preparing files (respecting .gitignore)...", "Preparazione dei file (rispettando .gitignore)..."));
        Check(folder, "add", "--all", "--", ".");
        GitResult diff = Run(folder, "diff", "--cached", "--quiet", "--exit-code");
        if (diff.Code == 1)
        {
            log(T("Creating the commit...", "Creazione del commit..."));
            GitResult commit = Check(folder, "commit", "-m", message);
            log(commit.Output);
        }
        else if (diff.Code == 0) log(T("No new changes to commit. Checking for commits to upload...", "Nessuna nuova modifica da salvare. Controllo i commit da caricare..."));
        else throw new InvalidOperationException(diff.Error);
        if (Run(folder, "rev-parse", "--verify", "HEAD").Code != 0)
            throw new InvalidOperationException(T("There are no files to upload. Check the folder and .gitignore.", "Non ci sono file da caricare. Controlla la cartella e il file .gitignore."));
    }

    void Synchronize(string folder, string remote, string target, bool branchMayBeNew)
    {
        log(T("Checking for changes in the remote repository...", "Controllo delle modifiche presenti nel repository remoto..."));
        if (branchMayBeNew)
        {
            GitResult exists = Run(folder, "ls-remote", "--exit-code", "--heads", remote, target);
            if (exists.Code == 2) { log(T("The remote branch is new: ready for its first upload.", "Il branch remoto è nuovo: pronto per il primo caricamento.")); return; }
            if (exists.Code != 0) throw new InvalidOperationException(exists.Error);
        }
        Check(folder, "fetch", "--no-tags", remote, target);
        GitResult ancestor = Run(folder, "merge-base", "--is-ancestor", "FETCH_HEAD", "HEAD");
        if (ancestor.Code == 0) { log(T("No rebase needed.", "Nessun rebase necessario.")); return; }
        if (ancestor.Code != 1) throw new InvalidOperationException(ancestor.Error);
        log(T("The remote repository has new changes: running automatic rebase...", "Il repository remoto contiene nuove modifiche: rebase automatico in corso..."));
        GitResult rebase = Run(folder, "rebase", "FETCH_HEAD");
        if (rebase.Code == 0) { log(T("Rebase completed.", "Rebase completato.")); return; }
        string conflicts = Run(folder, "diff", "--name-only", "--diff-filter=U").Output;
        bool active = false;
        foreach (string marker in new [] { "rebase-merge", "rebase-apply" })
        {
            string path = Check(folder, "rev-parse", "--git-path", marker).Output;
            if (!Path.IsPathRooted(path)) path = Path.Combine(folder, path);
            if (Directory.Exists(path)) active = true;
        }
        if (active)
        {
            GitResult abort = Run(folder, "rebase", "--abort");
            if (abort.Code != 0)
                throw new InvalidOperationException(T("Rebase failed and could not be aborted.\n", "Il rebase non è riuscito e non è stato possibile annullarlo.\n") + abort.Error + "\n" + rebase.Error);
        }
        throw new InvalidOperationException(T("Rebase did not complete", "Rebase non completato") + (active ? T(" and was aborted. Your local commit was preserved.", " e annullato. Il commit locale è stato conservato.") : ".") +
            (conflicts.Length > 0 ? T("\nConflicting files:\n", "\nFile in conflitto:\n") + conflicts + T("\nReconcile the changes in these files manually before retrying.", "\nLe modifiche a questi file vanno riconciliate manualmente prima di riprovare.") : "") +
            "\n\n" + rebase.Error + "\n" + rebase.Output);
    }

    public void First(string folder, string username, string email, string url, string message)
    {
        string branch = Prepare(folder, true);
        log(T("Setting name and email for this folder...", "Impostazione di nome ed email per questa cartella..."));
        Check(folder, "config", "--local", "user.name", username);
        Check(folder, "config", "--local", "user.email", email);
        GitResult remotes = Check(folder, "remote");
        bool originExists = Array.IndexOf(remotes.Output.Split(new [] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries), "origin") >= 0;
        if (originExists) Check(folder, "remote", "set-url", "origin", url);
        else Check(folder, "remote", "add", "origin", url);
        // A previous push URL must not silently send the project somewhere else.
        Check(folder, "config", "--local", "--replace-all", "remote.origin.pushurl", url);
        Commit(folder, message);
        log(T("Connecting to the repository. If asked to sign in, complete it in your browser...", "Connessione al repository. Se appare la richiesta di accesso, completala nel browser..."));
        Synchronize(folder, "origin", "refs/heads/" + branch, true);
        GitResult push = Check(folder, "-c", "remote.origin.mirror=false", "push", "--porcelain", "-u", "origin", branch);
        if (push.Output.Length > 0) log(push.Output);
        log(T("Upload completed. Branch: ", "Caricamento completato. Branch: ") + branch + ".");
    }

    public void Update(string folder, string message)
    {
        string branch = Prepare(folder, false);
        GitResult upstream = Run(folder, "rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{upstream}");
        if (upstream.Code != 0)
            throw new InvalidOperationException(T("This branch is not linked to a remote repository yet. Select First upload.", "Questo branch non è ancora collegato a un repository remoto. Seleziona Primo caricamento."));
        GitResult identity = Run(folder, "var", "GIT_AUTHOR_IDENT");
        if (identity.Code != 0) throw new InvalidOperationException(T("Git name or email is missing. Set them using First upload.\n", "Nome o email Git mancanti. Impostali con Primo caricamento.\n") + identity.Error);
        string remote = Check(folder, "config", "--get", "branch." + branch + ".remote").Output;
        string target = Check(folder, "config", "--get", "branch." + branch + ".merge").Output;
        if (remote == "." || !target.StartsWith("refs/heads/", StringComparison.Ordinal))
            throw new InvalidOperationException(T("The branch is not linked to a valid remote repository.", "Il branch non è collegato a un repository remoto valido."));
        Commit(folder, message);
        log(T("Uploading updates. If asked, complete your account sign-in...", "Caricamento degli aggiornamenti. Se richiesto, completa l'accesso al tuo account..."));
        Synchronize(folder, remote, target, false);
        // Explicit refspec ignores custom push.default and pushes only this branch.
        GitResult push = Check(folder, "-c", "remote." + remote + ".mirror=false", "push", "--porcelain", remote, "HEAD:" + target);
        if (push.Output.Length > 0) log(push.Output);
        log(T("Update completed. Branch: ", "Aggiornamento completato. Branch: ") + branch + ".");
    }

    public static string ValidateUrl(string value, string platform = "GitHub")
    {
        if (platform != "GitHub" && platform != "GitLab") throw new ArgumentException("Unsupported platform");
        string host = platform == "GitLab" ? "gitlab.com" : "github.com";
        string example = "https://" + host + "/username/repository";
        Uri uri;
        if (!Uri.TryCreate((value ?? "").Trim(), UriKind.Absolute, out uri) || uri.Scheme != "https" ||
            !String.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase) ||
            uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 || !uri.IsDefaultPort)
            throw new InvalidOperationException(T("Enter the HTTPS repository link for ", "Inserisci il link HTTPS del repository per ") + platform + T(". Example: ", ". Esempio: ") + example);
        string path = uri.AbsolutePath.Trim('/');
        if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) path = path.Substring(0, path.Length - 4);
        string[] segments = path.Split('/');
        bool valid = segments.Length >= 2 && (platform == "GitLab" || segments.Length == 2);
        foreach (string segment in segments)
            if (!Regex.IsMatch(segment, platform == "GitLab" ? @"^[A-Za-z0-9_][A-Za-z0-9_.-]*$" : @"^[A-Za-z0-9_.-]+$") || segment == "." || segment == "..") valid = false;
        if (platform == "GitHub" && !Regex.IsMatch(segments[0], @"^[A-Za-z0-9-]+$")) valid = false;
        if (!valid)
            throw new InvalidOperationException(T("Use the repository link, without /tree, /settings or other page paths.", "Il link deve indicare il repository, senza pagine /tree, /settings o altri percorsi."));
        return "https://" + host + "/" + path + ".git";
    }
}


