using System;
using System.Drawing;
using System.IO;
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
        RenderUI(root);
        Console.WriteLine("TUTTE LE PROVE SUPERATE");
    }
    static void RenderUI(string root)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
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
        foreach (string service in new [] { "GitHub", "GitLab" })
        foreach (bool first in new [] { true, false })
        {
            string settingsFile = Path.Combine(root, "ui-" + service + "-" + language + "-" + first + ".txt");
            using (var window = new SetupWindow(new AppSettings(settingsFile)))
            {
                Assert(window.LanguageSelector.SelectedIndex == 0, "Avvio in inglese " + language + " " + first);
                window.LanguageSelector.SelectedIndex = language == "it" ? 1 : 0;
                Assert(window.PlatformSelector.SelectedItem.ToString() == "GitHub", "GitHub predefinito nel menu piattaforma");
                window.PlatformSelector.SelectedIndex = service == "GitLab" ? 1 : 0;
                window.UploadType.SelectedIndex = first ? 0 : 1;
                window.StartPosition = FormStartPosition.Manual;
                window.Location = new Point(-32000, -32000);
                window.ShowInTaskbar = false;
                window.Show();
                Application.DoEvents();
                window.PerformLayout();
                using (var bitmap = new Bitmap(window.Width, window.Height))
                {
                    window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, window.Size));
                    bitmap.Save(Path.Combine(root, service + "-" + language + (first ? "-first.png" : "-updates.png")));
                }
                Assert(window.FirstFieldsVisible == first, "Campi corretti per la modalita " + language + " " + first);
                Assert(window.PlatformSelector.SelectedItem.ToString() == service, "Selezione della piattaforma " + service);
                Assert(window.UploadType.Items[0].ToString() == (language == "it" ? "Primo caricamento" : "First upload"), "Selettore modalita tradotto " + language);
                window.CommitMessage.Text = "Messaggio personalizzato";
                window.LanguageSelector.SelectedIndex = language == "it" ? 0 : 1;
                Assert(window.CommitMessage.Text == "Messaggio personalizzato", "Cambio lingua conserva il messaggio commit");
                Assert(window.PlatformSelector.SelectedItem.ToString() == service, "Cambio lingua conserva la piattaforma");
                window.UploadType.SelectedIndex = first ? 1 : 0;
                window.CommitMessage.Text = "Messaggio altra modalita";
                window.UploadType.SelectedIndex = first ? 0 : 1;
                Assert(window.CommitMessage.Text == "Messaggio personalizzato", "Cambio modalita conserva il messaggio commit");
                Assert(window.PlatformSelector.SelectedItem.ToString() == service, "Cambio modalita conserva la piattaforma");
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
}
