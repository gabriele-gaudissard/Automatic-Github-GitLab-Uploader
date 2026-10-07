using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

static class SmokeTests
{
    static void Assert(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }
    static void Reject(Action action, string description)
    {
        bool rejected = false;
        try { action(); } catch (InvalidOperationException) { rejected = true; }
        Assert(rejected, description);
    }
    static string Must(GitUploader git, string folder, params string[] args)
    {
        GitResult result = git.Run(folder, args);
        if (result.Code != 0) throw new Exception(result.Error + result.Output);
        return result.Output;
    }
    [STAThread]
    static void Main(string[] args)
    {
        string root = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(root);
        if (args.Length > 1 && args[1] == "render") { RenderUI(root); return; }
        string project = Path.Combine(root, "Cartella di prova è");
        string remote = Path.Combine(root, "repository remoto.git");
        Directory.CreateDirectory(project);
        Directory.CreateDirectory(remote);
        var git = new GitUploader(GitUploader.FindGit(), delegate(string s) { });
        Must(git, remote, "init", "--bare");
        File.WriteAllText(Path.Combine(project, ".gitignore"), "escluso.txt\n");
        File.WriteAllText(Path.Combine(project, "escluso.txt"), "non pubblicare");
        File.WriteAllText(Path.Combine(project, "prova.txt"), "prima versione");
        string commit = "Primo caricamento: \"è pronto\" & $(prova) C:\\cartella\\";
        git.First(project, "utente-test", "test@example.com", remote, commit);
        Assert(Must(git, remote, "log", "main", "-1", "--format=%s") == commit, "Primo caricamento, spazi, Unicode e virgolette");
        Assert(!Must(git, remote, "ls-tree", "-r", "--name-only", "main").Contains("escluso.txt"), "Rispetto di .gitignore");
        Assert(Must(git, project, "rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{upstream}") == "origin/main", "Collegamento per aggiornamenti successivi");
        File.WriteAllText(Path.Combine(project, "prova.txt"), "seconda versione");
        File.WriteAllText(Path.Combine(project, "nuovo.txt"), "nuovo file");
        git.Update(project, "Aggiornamento V2");
        Assert(Must(git, remote, "show", "main:prova.txt") == "seconda versione", "Aggiornamento del contenuto");
        File.Delete(Path.Combine(project, "nuovo.txt"));
        git.Update(project, "Rimozione file");
        Assert(!Must(git, remote, "ls-tree", "-r", "--name-only", "main").Contains("nuovo.txt"), "Caricamento delle eliminazioni");
        string head = Must(git, project, "rev-parse", "HEAD");
        git.Update(project, "Nessuna modifica");
        Assert(Must(git, project, "rev-parse", "HEAD") == head, "Nessun commit vuoto");
        string other = Path.Combine(root, "altro computer");
        Must(git, root, "clone", "--branch", "main", remote, other);
        Must(git, other, "config", "user.name", "altro-test");
        Must(git, other, "config", "user.email", "altro@example.com");
        File.WriteAllText(Path.Combine(other, "remoto.txt"), "modifica da altro computer");
        git.Update(other, "Modifica remota");
        File.WriteAllText(Path.Combine(project, "locale.txt"), "modifica locale");
        git.Update(project, "Modifica locale con rebase");
        Assert(Must(git, remote, "show", "main:locale.txt") == "modifica locale" &&
            Must(git, remote, "show", "main:remoto.txt") == "modifica da altro computer", "Rebase automatico con cronologie divergenti");
        Must(git, other, "pull", "--rebase");
        File.WriteAllText(Path.Combine(other, "prova.txt"), "versione remota in conflitto");
        git.Update(other, "Cambio remoto in conflitto");
        File.WriteAllText(Path.Combine(project, "prova.txt"), "versione locale in conflitto");
        Reject(delegate { git.Update(project, "Cambio locale in conflitto"); }, "Conflitto rilevato e caricamento interrotto");
        Assert(File.ReadAllText(Path.Combine(project, "prova.txt")) == "versione locale in conflitto" &&
            Must(git, project, "log", "-1", "--format=%s") == "Cambio locale in conflitto", "Commit locale conservato dopo il conflitto");
        Assert(!Directory.Exists(Path.Combine(project, ".git", "rebase-merge")) &&
            Must(git, project, "status", "--porcelain").Length == 0, "Rebase annullato senza lasciare conflitti aperti");
        Assert(Must(git, remote, "show", "main:prova.txt") == "versione remota in conflitto", "Contenuto remoto conservato dopo il conflitto");
        string firstExisting = Path.Combine(root, "primo su remoto esistente");
        Directory.CreateDirectory(firstExisting);
        File.WriteAllText(Path.Combine(firstExisting, "primo-nuovo.txt"), "primo caricamento");
        git.First(firstExisting, "utente-test", "test@example.com", remote, "Primo su remoto esistente");
        Assert(Must(git, remote, "show", "main:primo-nuovo.txt") == "primo caricamento" &&
            Must(git, remote, "show", "main:remoto.txt") == "modifica da altro computer", "Rebase al primo caricamento con repository già popolato");
        string child = Path.Combine(project, "sottocartella");
        Directory.CreateDirectory(child);
        Reject(delegate { git.Update(child, "errore"); }, "Rifiuto della sottocartella di un repository");
        string plain = Path.Combine(root, "cartella senza git");
        Directory.CreateDirectory(plain);
        Reject(delegate { git.Update(plain, "errore"); }, "Indicazione di primo caricamento necessario");
        string isolated = Path.Combine(root, "repository senza upstream");
        Directory.CreateDirectory(isolated);
        Must(git, isolated, "init", "-b", "main");
        File.WriteAllText(Path.Combine(isolated, "prova.txt"), "prova");
        Reject(delegate { git.Update(isolated, "errore"); }, "Branch senza collegamento remoto");
        Assert(Must(git, isolated, "diff", "--cached", "--name-only").Length == 0, "Errore di configurazione prima di preparare i file");
        Assert(GitUploader.ValidateUrl("https://github.com/utente/progetto") == "https://github.com/utente/progetto.git", "Link GitHub normalizzato");
        Assert(GitUploader.ValidateUrl("https://github.com/utente/.github") == "https://github.com/utente/.github.git", "Repository GitHub con punto iniziale");
        Assert(GitUploader.ValidateUrl("https://gitlab.com/utente/progetto", "GitLab") == "https://gitlab.com/utente/progetto.git", "Link GitLab normalizzato");
        Assert(GitUploader.ValidateUrl("https://gitlab.com/gruppo/sottogruppo/progetto.git", "GitLab") == "https://gitlab.com/gruppo/sottogruppo/progetto.git", "Gruppi e sottogruppi GitLab");
        Assert(GitUploader.ValidateUrl("https://gitlab.com/gruppo/progetto/", "GitLab") == "https://gitlab.com/gruppo/progetto.git", "Slash finale del link GitLab");
        Reject(delegate { GitUploader.ValidateUrl("https://gitlab.com/gruppo/progetto/-/tree/main", "GitLab"); }, "Rifiuto dei link a pagine interne GitLab");
        Reject(delegate { GitUploader.ValidateUrl("https://gitlab.com/gruppo", "GitLab"); }, "Rifiuto dei link al solo gruppo GitLab");
        Reject(delegate { GitUploader.ValidateUrl("https://github.com/utente/progetto", "GitLab"); }, "Piattaforma selezionata coerente con il link");
        Reject(delegate { GitUploader.ValidateUrl("https://utente:segreto@gitlab.com/gruppo/progetto", "GitLab"); }, "Nessuna credenziale nel link GitLab");
        Reject(delegate { GitUploader.ValidateUrl("https://gitlab.com/gruppo/progetto?token=segreto", "GitLab"); }, "Nessun token nei parametri del link GitLab");
        Reject(delegate { GitUploader.ValidateUrl("https://github.com/utente/progetto/tree/main"); }, "Rifiuto dei link a pagine interne");
        Reject(delegate { GitUploader.ValidateUrl("https://utente:password@github.com/utente/progetto"); }, "Rifiuto di credenziali nel link");
        Reject(delegate { GitUploader.ValidateUrl("https://example.com/utente/progetto"); }, "Controllo del dominio GitHub");
        Assert(GitUploader.ValidateUrl("https://gitea.com/utente/progetto", "Gitea") == "https://gitea.com/utente/progetto.git", "Link Gitea normalizzato");
        Assert(GitUploader.ValidateUrl("https://git.example.com:3443/team/progetto.git/", "Gitea") == "https://git.example.com:3443/team/progetto.git", "Dominio e porta HTTPS personalizzati Gitea");
        Assert(GitUploader.ValidateUrl("https://git.example.com/gitea/team/progetto.git", "Gitea") == "https://git.example.com/gitea/team/progetto.git", "Gitea ospitato con prefisso nel percorso");
        Assert(GitUploader.ValidateUrl("https://192.168.1.25/team/progetto", "Gitea") == "https://192.168.1.25/team/progetto.git", "Gitea su indirizzo di rete locale");
        Reject(delegate { GitUploader.ValidateUrl("https://git.example.com/utente", "Gitea"); }, "Rifiuto del solo profilo Gitea");
        Reject(delegate { GitUploader.ValidateUrl("http://git.example.com/utente/progetto", "Gitea"); }, "Gitea richiede HTTPS");
        Reject(delegate { GitUploader.ValidateUrl("https://utente:segreto@git.example.com/utente/progetto", "Gitea"); }, "Nessuna credenziale nel link Gitea");
        Reject(delegate { GitUploader.ValidateUrl("https://git.example.com/utente/progetto?token=segreto", "Gitea"); }, "Nessun token nei parametri Gitea");
        Reject(delegate { GitUploader.ValidateUrl("https://git.example.com/utente/progetto#readme", "Gitea"); }, "Rifiuto di frammenti nel link Gitea");
        Reject(delegate { GitUploader.ValidateUrl("https://git.example.com/utente/progetto/src/branch/main", "Gitea"); }, "Rifiuto delle pagine interne Gitea");
        Reject(delegate { GitUploader.ValidateUrl("https://git.example.com/utente/progetto/settings", "Gitea"); }, "Rifiuto della pagina impostazioni Gitea");
        Reject(delegate { GitUploader.ValidateUrl("https://github.com/utente/progetto", "Gitea"); }, "Link GitHub richiede la piattaforma GitHub");
        Reject(delegate { GitUploader.ValidateUrl("https://gitlab.com/utente/progetto", "Gitea"); }, "Link GitLab richiede la piattaforma GitLab");
        Assert(GitUploader.ValidateUrl("https://git.example.com/team/repo", "Gitea", "31000") == "https://git.example.com:31000/team/repo.git", "Campo porta 31000 aggiunto al link Gitea");
        Assert(GitUploader.ValidateUrl("https://git.example.com:3443/gitea/team/repo.git", "Gitea", "4443") == "https://git.example.com:4443/gitea/team/repo.git", "Campo porta sostituisce la porta del link senza perdere il percorso");
        Assert(GitUploader.ValidateUrl("https://git.example.com:3443/team/repo", "Gitea", " ") == "https://git.example.com:3443/team/repo.git", "Porta vuota conserva la porta del link");
        Assert(GitUploader.ValidateUrl("https://git.example.com:3443/team/repo", "Gitea", "443") == "https://git.example.com/team/repo.git", "Porta HTTPS predefinita");
        foreach (string port in new [] { "0", "65536", "-1", "abc", "1.5", "999999999999" })
            Reject(delegate { GitUploader.ValidateUrl("https://git.example.com/team/repo", "Gitea", port); }, "Rifiuto porta non valida: " + port);
        Assert(GitUploader.ValidateUrl("https://git.example.com/team/repo", "Gitea", "65535").Contains(":65535/"), "Porta massima valida");
        TestBranchUploads(root);
        UpdateTests.Run(root);
        RenderUI(root);
        Console.WriteLine("TUTTE LE PROVE SUPERATE");
    }
    static void TestBranchUploads(string root)
    {
        var git = new GitUploader(GitUploader.FindGit(), delegate(string text) {});
        string remote = Path.Combine(root, "branch-remote.git");
        string project = Path.Combine(root, "branch-project");
        Directory.CreateDirectory(remote);
        Directory.CreateDirectory(project);
        Must(git, remote, "init", "--bare");
        File.WriteAllText(Path.Combine(project, "main.txt"), "main baseline");
        git.First(project, "test", "test@example.com", remote, "Main upload", "main");
        string mainHead = Must(git, remote, "rev-parse", "main");
        File.WriteAllText(Path.Combine(project, "main.txt"), "trunk version");
        git.Update(project, "Create release", "release");
        Assert(Must(git, remote, "show", "release:main.txt") == "trunk version" && Must(git, remote, "rev-parse", "main") == mainHead, "Trunk1 creato senza modificare main");
        Assert(Must(git, project, "rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{upstream}") == "origin/release", "Nuovo trunk collegato per aggiornamenti successivi");
        string other = Path.Combine(root, "branch-other");
        Must(git, root, "clone", "--branch", "release", remote, other);
        Must(git, other, "config", "user.name", "test-other");
        Must(git, other, "config", "user.email", "other@example.com");
        File.WriteAllText(Path.Combine(other, "remote-only.txt"), "remote addition");
        git.Update(other, "Advance remote release");
        Must(git, project, "switch", "main");
        File.WriteAllText(Path.Combine(project, "local-only.txt"), "local addition");
        git.Update(project, "Use existing release", "release");
        Assert(Must(git, remote, "show", "release:remote-only.txt") == "remote addition" && Must(git, remote, "show", "release:local-only.txt") == "local addition", "Trunk1 esistente riutilizzato e aggiornato con rebase");
        string releaseHead = Must(git, remote, "rev-parse", "release");
        File.WriteAllText(Path.Combine(project, "feature-login.txt"), "second trunk");
        git.Update(project, "Create feature/login", "feature/login");
        Assert(Must(git, remote, "show", "feature/login:feature-login.txt") == "second trunk" && Must(git, remote, "rev-parse", "release") == releaseHead, "Branch con slash creato senza modificare release");
        string firstExisting = Path.Combine(root, "first-existing-trunk");
        Directory.CreateDirectory(firstExisting);
        File.WriteAllText(Path.Combine(firstExisting, "first.txt"), "new folder");
        git.First(firstExisting, "test", "test@example.com", remote, "First on existing trunk", "release");
        Assert(Must(git, firstExisting, "symbolic-ref", "--short", "HEAD") == "release" && Must(git, remote, "show", "release:first.txt") == "new folder" && Must(git, remote, "show", "release:remote-only.txt") == "remote addition", "Primo caricamento su trunk remoto gia esistente");
        File.WriteAllText(Path.Combine(other, "remote-bugfix.txt"), "remote third trunk");
        git.Update(other, "Create remote bugfix", "bugfix");
        File.WriteAllText(Path.Combine(project, "local-bugfix.txt"), "local third trunk");
        git.Update(project, "Reuse remote-only bugfix", "bugfix");
        Assert(Must(git, remote, "show", "bugfix:remote-bugfix.txt") == "remote third trunk" && Must(git, remote, "show", "bugfix:local-bugfix.txt") == "local third trunk", "Trunk esistente solo sul remoto riutilizzato");
        File.WriteAllText(Path.Combine(project, "main.txt"), "pending changes must survive");
        Reject(delegate { git.Update(project, "Blocked switch", "main"); }, "Cambio branch bloccato se perderebbe modifiche locali");
        Assert(File.ReadAllText(Path.Combine(project, "main.txt")) == "pending changes must survive" && Must(git, project, "symbolic-ref", "--short", "HEAD") == "bugfix" && Must(git, remote, "rev-parse", "main") == mainHead, "Cambio bloccato conserva file, branch e main remoto");
        Must(git, project, "config", "--replace-all", "remote.origin.pushurl", Path.Combine(root, "missing-push.git"));
        Reject(delegate { git.Update(project, "Retryable new branch", "retry-upload"); }, "Errore push di un nuovo trunk rilevato");
        Must(git, project, "config", "--replace-all", "remote.origin.pushurl", remote);
        git.Update(project, "Retry current branch");
        Assert(Must(git, remote, "show", "retry-upload:main.txt") == "pending changes must survive", "Retry sul branch attuale dopo push fallito di un trunk nuovo");
        git.Update(project, "Return to main", "main");
        Assert(Must(git, project, "symbolic-ref", "--short", "HEAD") == "main" && Must(git, remote, "rev-parse", "main") == mainHead, "Main esistente selezionato senza commit vuoto");
        string fresh = Path.Combine(root, "first-new-trunk");
        Directory.CreateDirectory(fresh);
        File.WriteAllText(Path.Combine(fresh, "fresh.txt"), "fresh trunk");
        git.First(fresh, "test", "test@example.com", remote, "First new trunk", "prototype");
        Assert(Must(git, fresh, "symbolic-ref", "--short", "HEAD") == "prototype" && Must(git, remote, "show", "prototype:fresh.txt") == "fresh trunk", "Primo caricamento crea il trunk richiesto");
        string invalid = Path.Combine(root, "invalid-branch-folder");
        Directory.CreateDirectory(invalid);
        foreach (string name in new [] { "bad name", "../bad", "-bad", "HEAD", "bad.lock", "bad\nname", "topic@{1}" })
            Reject(delegate { git.First(invalid, "test", "test@example.com", remote, "Invalid", name); }, "Rifiuto branch non valido prima di modificare la cartella: " + name);
        Assert(!Directory.Exists(Path.Combine(invalid, ".git")), "Branch invalido senza inizializzare Git");
        Must(git, remote, "symbolic-ref", "HEAD", "refs/heads/release");
        Must(git, other, "remote", "set-head", "origin", "release");
        string[] suggestions = git.SuggestedBranches(other);
        Assert(suggestions[0] == "release" && Array.IndexOf(suggestions, "main") >= 0 && Array.IndexOf(suggestions, "bugfix") >= 0, "Suggerimenti includono principale, main e branch esistenti");
        string automatic = Path.Combine(root, "automatic-default-branch");
        Directory.CreateDirectory(automatic);
        File.WriteAllText(Path.Combine(automatic, "automatic.txt"), "default branch upload");
        git.First(automatic, "test", "test@example.com", remote, "Default branch upload", null, true);
        Assert(Must(git, automatic, "symbolic-ref", "--short", "HEAD") == "release" && Must(git, remote, "show", "release:automatic.txt") == "default branch upload", "Branch principale rilevato dal server con nome diverso da main");
        git.Update(project, "Use default branch", null, true);
        Assert(Must(git, project, "symbolic-ref", "--short", "HEAD") == "release", "Branch principale selezionabile negli aggiornamenti");
        string emptyRemote = Path.Combine(root, "empty-default-remote.git");
        string emptyProject = Path.Combine(root, "empty-default-project");
        Directory.CreateDirectory(emptyRemote);
        Directory.CreateDirectory(emptyProject);
        Must(git, emptyRemote, "init", "--bare");
        File.WriteAllText(Path.Combine(emptyProject, "start.txt"), "start");
        git.First(emptyProject, "test", "test@example.com", emptyRemote, "Default on empty", null, true);
        Assert(Must(git, emptyRemote, "show", "main:start.txt") == "start", "Branch principale di un repository vuoto usa main");
        string portProject = Path.Combine(root, "gitea-port-project");
        Directory.CreateDirectory(portProject);
        Must(git, portProject, "init", "-b", "main");
        Must(git, portProject, "remote", "add", "origin", "https://git.example.com/gitea/team/repo.git");
        git.ConfigureGiteaPort(portProject, "origin", "31000");
        Assert(git.ConnectedUrl(portProject) == "https://git.example.com:31000/gitea/team/repo.git" && Must(git, portProject, "remote", "get-url", "--push", "origin") == git.ConnectedUrl(portProject), "Porta Gitea salvata per fetch e push negli aggiornamenti");
        Reject(delegate { git.ConfigureGiteaPort(portProject, "origin", "0"); }, "Porta invalida rifiutata senza cambiare il remote");
        Assert(git.ConnectedUrl(portProject).Contains(":31000/"), "Remote conservato dopo porta invalida");
        Must(git, portProject, "remote", "set-url", "origin", "https://github.com/team/repo.git");
        Reject(delegate { git.ConfigureGiteaPort(portProject, "origin", "31000"); }, "Campo porta Gitea non cambia GitHub");
        Assert(git.ConnectedUrl(portProject) == "https://github.com/team/repo.git", "Remote GitHub conservato");
    }
    static void RenderUI(string root)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        TestRecentFolders(root);
        string persistenceFile = Path.Combine(root, "preferences", "language.txt");
        var preferences = new AppSettings(persistenceFile);
        Assert(preferences.LoadLanguage() == "en", "Inglese predefinito senza preferenze salvate");
        preferences.SaveLanguage("it");
        Assert(new AppSettings(persistenceFile).LoadLanguage() == "it", "Preferenza italiana caricata da una nuova istanza");
        preferences.SaveLanguage("en");
        Assert(new AppSettings(persistenceFile).LoadLanguage() == "en", "Aggiornamento della preferenza salvata");
        File.WriteAllText(persistenceFile, "invalid");
        Assert(preferences.LoadLanguage() == "en", "Preferenza non valida: ritorno all'inglese");
        foreach (string language in new [] { "en", "it" })
        foreach (string service in new [] { "GitHub", "GitLab", "Gitea" })
        foreach (bool first in new [] { true, false })
        {
            string settingsFile = Path.Combine(root, "ui-" + service + "-" + language + "-" + first + ".txt");
            using (var window = new SetupWindow(new AppSettings(settingsFile)))
            {
                Assert(window.LanguageSelector.SelectedIndex == 0, "Avvio in inglese " + language + " " + first);
                window.LanguageSelector.SelectedIndex = language == "it" ? 1 : 0;
                Assert(window.PlatformSelector.SelectedItem.ToString() == "GitHub", "GitHub predefinito nel menu piattaforma");
                window.PlatformSelector.SelectedItem = service;
                window.UploadType.SelectedIndex = first ? 0 : 1;
                window.StartPosition = FormStartPosition.Manual;
                window.Location = new Point(-32000, -32000);
                window.ShowInTaskbar = false;
                window.Show();
                Application.DoEvents();
                window.PerformLayout();
                Assert(window.BranchSelector.Text == (first ? "main" : ""), "Main predefinito al primo upload, campo vuoto per branch attuale negli aggiornamenti");
                Assert(window.PortFieldVisible == (first && service == "Gitea"), "Campo porta visibile solo al primo upload Gitea");
                Assert(window.BranchSelector.DropDownStyle == ComboBoxStyle.DropDown && window.BranchSelector.Items.Contains("main"), "Casella branch modificabile con suggerimenti");
                window.BranchSelector.Text = "feature/login";
                window.GiteaPort.Text = "31000";
                using (var bitmap = new Bitmap(window.Width, window.Height))
                {
                    window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, window.Size));
                    bitmap.Save(Path.Combine(root, service + "-" + language + (first ? "-first.png" : "-updates.png")));
                }
                string uiProject = Path.Combine(root, "ui-project-" + service + "-" + language + "-" + first);
                Directory.CreateDirectory(uiProject);
                var uiGit = new GitUploader(GitUploader.FindGit(), delegate(string text) {});
                Must(uiGit, uiProject, "init", "-b", "main");
                Must(uiGit, uiProject, "remote", "add", "origin", service == "Gitea" ? "https://git.example.com/team/repo.git" : service == "GitLab" ? "https://gitlab.com/team/repo.git" : "https://github.com/team/repo.git");
                window.ProjectFolder.Text = uiProject;
                Assert(window.PortFieldVisible == (service == "Gitea"), "Porta Gitea disponibile al primo e ai successivi caricamenti dopo selezione cartella");
                Assert(window.FirstFieldsVisible == first, "Campi corretti per la modalita " + language + " " + first);
                Assert(window.PlatformSelector.SelectedItem.ToString() == service, "Selezione della piattaforma " + service);
                Assert(window.UploadType.Items[0].ToString() == (language == "it" ? "Primo caricamento" : "First upload"), "Selettore modalita tradotto " + language);
                window.CommitMessage.Text = "Messaggio personalizzato";
                window.LanguageSelector.SelectedIndex = language == "it" ? 0 : 1;
                Assert(window.BranchSelector.Text == "feature/login" && window.GiteaPort.Text == "31000", "Cambio lingua conserva nome branch libero e porta");
                Assert(window.CommitMessage.Text == "Messaggio personalizzato", "Cambio lingua conserva il messaggio commit");
                Assert(window.PlatformSelector.SelectedItem.ToString() == service, "Cambio lingua conserva la piattaforma");
                window.UploadType.SelectedIndex = first ? 1 : 0;
                window.CommitMessage.Text = "Messaggio altra modalita";
                window.UploadType.SelectedIndex = first ? 0 : 1;
                Assert(window.BranchSelector.Text == "feature/login", "Cambio modalita conserva il nome branch libero della modalita");
                Assert(window.CommitMessage.Text == "Messaggio personalizzato", "Cambio modalita conserva il messaggio commit");
                Assert(window.PlatformSelector.SelectedItem.ToString() == service, "Cambio modalita conserva la piattaforma");
                window.LanguageSelector.SelectedIndex = 1;
                window.BranchSelector.SelectedIndex = 0;
                window.LanguageSelector.SelectedIndex = 0;
                Assert(window.BranchSelector.Text == "Default branch", "Suggerimento principale tradotto e conservato");
                window.LanguageSelector.SelectedIndex = 1;
            }
            using (var reopened = new SetupWindow(new AppSettings(settingsFile)))
            {
                Assert(reopened.LanguageSelector.SelectedIndex == 1 && reopened.UploadType.Items[0].ToString() == "Primo caricamento", "Lingua mantenuta dopo la chiusura e riapertura");
            }
        }
        UiText.Code = "en";
        try { GitUploader.ValidateUrl("invalid"); }
        catch (InvalidOperationException ex) { Assert(ex.Message.StartsWith("Enter"), "Errori del caricamento in inglese"); }
        UiText.Code = "it";
        try { GitUploader.ValidateUrl("invalid"); }
        catch (InvalidOperationException ex) { Assert(ex.Message.StartsWith("Inserisci"), "Errori del caricamento in italiano"); }
        UiText.Code = "en";
    }
    static void TestRecentFolders(string root)
    {
        var settings = new AppSettings(Path.Combine(root, "history", "language.txt"));
        Assert(settings.LoadRecentFolders().Length == 0, "Cronologia inizialmente vuota");
        var projects = new string[4];
        string[] names = { "First Project", "Website", "Tools", "Example Project" };
        for (int index = 0; index < projects.Length; index++)
        {
            projects[index] = Path.Combine(root, "demo-projects", names[index]);
            Directory.CreateDirectory(projects[index]);
            settings.RememberUploadedFolder(projects[index]);
        }
        string[] recent = new AppSettings(Path.Combine(root, "history", "language.txt")).LoadRecentFolders();
        Assert(recent.Length == 3 && recent[0] == projects[3] && recent[1] == projects[2] && recent[2] == projects[1], "Ultimi tre caricamenti persistenti, dal piu recente");
        settings.RememberUploadedFolder(projects[1]);
        recent = settings.LoadRecentFolders();
        Assert(recent.Length == 3 && recent[0] == projects[1] && recent[1] == projects[3] && recent[2] == projects[2], "Cartella gia usata spostata in cima senza duplicati");
        settings.RememberUploadedFolder(projects[1].ToUpperInvariant() + Path.DirectorySeparatorChar);
        Assert(settings.LoadRecentFolders().Length == 3, "Nessun duplicato con maiuscole e slash finale");
        RejectMissingRecentFolder(settings, Path.Combine(root, "does-not-exist"));
        File.AppendAllText(Path.Combine(root, "history", "recent-folders.txt"), Environment.NewLine + "relative-path" + Environment.NewLine + "invalid\0path");
        Assert(settings.LoadRecentFolders().Length == 3, "Cronologia corrotta gestita senza errore");
        UiText.Code = "en";
        bool opened = false;
        string selected = null;
        using (var menu = new FolderBrowseMenu(settings.LoadRecentFolders(), delegate { opened = true; }, delegate(string folder) { selected = folder; }))
        {
            menu.CreateControl();
            menu.Size = menu.GetPreferredSize(Size.Empty);
            menu.PerformLayout();
            var recentDropdown = ((ToolStripMenuItem)menu.Items[1]).DropDown;
            recentDropdown.CreateControl();
            recentDropdown.Size = recentDropdown.GetPreferredSize(Size.Empty);
            recentDropdown.PerformLayout();
            menu.Items[1].Select();
            using (var image = new Bitmap(menu.Width + recentDropdown.Width, Math.Max(menu.Height, recentDropdown.Height)))
            {
                using (var graphics = Graphics.FromImage(image)) graphics.Clear(Color.FromArgb(18, 22, 29));
                menu.DrawToBitmap(image, new Rectangle(Point.Empty, menu.Size));
                recentDropdown.DrawToBitmap(image, new Rectangle(new Point(menu.Width, 0), recentDropdown.Size));
                image.Save(Path.Combine(root, "recent-folders.png"));
            }
            menu.Items[0].PerformClick();
            Assert(opened && selected == null, "Apri cartella avvia il selettore senza cambiare la cartella");
            var recents = (ToolStripMenuItem)menu.Items[1];
            Assert(recents.DropDownItems.Count == 3, "Menu Recenti con tre cartelle");
            var first = recents.DropDownItems[0];
            first.PerformClick();
            Assert(String.Equals(selected.TrimEnd('\\'), projects[1], StringComparison.OrdinalIgnoreCase), "Selezione recente con un clic");
        }
        using (var emptyMenu = new FolderBrowseMenu(new string[0], delegate {}, delegate(string folder) {}))
        {
            var emptyRecents = (ToolStripMenuItem)emptyMenu.Items[1];
            Assert(emptyRecents.DropDownItems.Count == 1 && !emptyRecents.DropDownItems[0].Enabled, "Menu recenti vuoto con messaggio informativo");
        }
        UiText.Code = "it";
        using (var italian = new FolderBrowseMenu(new string[0], delegate {}, delegate(string folder) {}))
            Assert(italian.Items[0].Text == "Apri cartella…" && italian.Items[1].Text == "Recenti", "Menu Sfoglia tradotto in italiano");
        UiText.Code = "en";
        var native = WindowsFolderPicker.CreateDialog(projects[3]);
        try
        {
            uint options;
            native.GetOptions(out options);
            Assert((options & 0x20) != 0 && (options & 0x40) != 0, "Selettore Explorer nativo configurato per cartelle");
            WindowsFolderPicker.IShellItem initial;
            native.GetFolder(out initial);
            try { Assert(String.Equals(WindowsFolderPicker.GetPath(initial), projects[3], StringComparison.OrdinalIgnoreCase), "Cartella iniziale del selettore Windows mantenuta"); }
            finally { Marshal.ReleaseComObject(initial); }
        }
        finally { Marshal.ReleaseComObject(native); }
        // Simulate a stale history entry without deleting any project directory.
        File.WriteAllText(Path.Combine(root, "history", "recent-folders.txt"), Path.Combine(root, "missing-project") + Environment.NewLine + projects[3]);
        recent = settings.LoadRecentFolders();
        Assert(recent.Length == 1 && recent[0] == projects[3], "Cartelle non piu esistenti escluse dai recenti");
    }
    static void RejectMissingRecentFolder(AppSettings settings, string path)
    {
        bool failed = false;
        try { settings.RememberUploadedFolder(path); } catch (DirectoryNotFoundException) { failed = true; }
        Assert(failed && settings.LoadRecentFolders().Length == 3, "Percorso inesistente non altera la cronologia");
    }
}
