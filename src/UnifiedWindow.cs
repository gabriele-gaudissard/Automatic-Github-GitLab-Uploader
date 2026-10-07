using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

public static class UiText
{
    public static string Code = "en";
    public static string Get(string english, string italian) { return Code == "it" ? italian : english; }
}

public sealed class AppSettings
{
    readonly string file;
    public AppSettings(string settingsFile = null)
    {
        file = settingsFile ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GithubSetup", "language.txt");
    }
    public string LoadLanguage()
    {
        try { return File.ReadAllText(file).Trim() == "it" ? "it" : "en"; }
        catch (IOException) { return "en"; }
        catch (UnauthorizedAccessException) { return "en"; }
    }
    public void SaveLanguage(string code)
    {
        if (code != "en" && code != "it") throw new ArgumentException("Unsupported language");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file)));
        string temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, code, Encoding.UTF8);
            if (File.Exists(file)) File.Replace(temporary, file, null);
            else File.Move(temporary, file);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

public sealed class DarkComboBox : ComboBox
{
    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        bool printing = message.Msg == 0x0317 || message.Msg == 0x0318;
        if (!IsHandleCreated || (message.Msg != 0x000F && !printing)) return;
        using (Graphics graphics = printing && message.WParam != IntPtr.Zero ? Graphics.FromHdc(message.WParam) : Graphics.FromHwnd(Handle))
        {
            int arrowWidth = SystemInformation.VerticalScrollBarWidth + 4;
            int centerX = Width - arrowWidth / 2;
            int centerY = Height / 2;
            using (var surface = new SolidBrush(Color.FromArgb(28, 34, 44)))
                graphics.FillRectangle(surface, Width - arrowWidth, 1, arrowWidth - 1, Height - 2);
            using (var border = new Pen(Color.FromArgb(58, 70, 89)))
                graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
            using (var arrow = new SolidBrush(Enabled ? Color.FromArgb(169, 182, 203) : Color.FromArgb(95, 105, 120)))
                graphics.FillPolygon(arrow, new [] { new Point(centerX - 4, centerY - 2), new Point(centerX + 4, centerY - 2), new Point(centerX, centerY + 2) });
        }
    }
}

public sealed class SetupWindow : Form
{
    static readonly Color DarkBackground = Color.FromArgb(18, 22, 29);
    static readonly Color DarkSurface = Color.FromArgb(28, 34, 44);
    static readonly Color DarkBorder = Color.FromArgb(58, 70, 89);
    static readonly Color LightText = Color.FromArgb(232, 237, 245);
    static readonly Color MutedText = Color.FromArgb(169, 182, 203);
    static readonly Color Accent = Color.FromArgb(47, 103, 218);
    static readonly Color SuccessText = Color.FromArgb(119, 221, 157);
    static readonly Color ErrorText = Color.FromArgb(255, 133, 143);
    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    static extern int SetWindowTheme(IntPtr window, string application, string subIdList);
    readonly AppSettings settings;
    readonly ComboBox mode = new DarkComboBox();
    readonly ComboBox language = new DarkComboBox();
    readonly ComboBox platform = new DarkComboBox();
    readonly TextBox folder = new TextBox();
    readonly TextBox email = new TextBox();
    readonly TextBox username = new TextBox();
    readonly TextBox repo = new TextBox();
    readonly TextBox message = new TextBox();
    readonly TextBox output = new TextBox();
    readonly Button browse = new Button();
    readonly Button upload = new Button();
    readonly Label heading = new Label();
    readonly Label note = new Label();
    readonly Label status = new Label();
    readonly Label footer = new Label();
    readonly Label modeLabel = new Label();
    readonly Label languageLabel = new Label();
    readonly Label[] captions = new Label[6];
    readonly TableLayoutPanel fields = new TableLayoutPanel();
    readonly TableLayoutPanel selectors = new TableLayoutPanel();
    bool busy;
    bool refreshing;
    int activeMode;
    string firstMessage;
    string updateMessage = "";
    string state = "ready";

    public ComboBox UploadType { get { return mode; } }
    public ComboBox LanguageSelector { get { return language; } }
    public ComboBox PlatformSelector { get { return platform; } }
    public TextBox CommitMessage { get { return message; } }
    public bool FirstFieldsVisible { get { return fields.RowStyles[1].Height > 0; } }
    static string T(string en, string it) { return UiText.Get(en, it); }

    public SetupWindow(AppSettings appSettings = null)
    {
        settings = appSettings ?? new AppSettings();
        UiText.Code = settings.LoadLanguage();
        firstMessage = T("Initial upload", "Primo caricamento");
        Text = "Git Repository Uploader";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(820, 720);
        MinimumSize = new Size(790, 710);
        Font = new Font("Segoe UI", 10F);
        BackColor = DarkBackground;
        ForeColor = LightText;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 8 };
        foreach (int height in new [] { 45, 52, 60 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        Controls.Add(layout);
        heading.Dock = DockStyle.Fill;
        heading.Font = new Font("Segoe UI", 19F, FontStyle.Bold);
        heading.ForeColor = LightText;
        layout.Controls.Add(heading, 0, 0);
        selectors.Dock = DockStyle.Fill;
        selectors.ColumnCount = 4;
        selectors.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        selectors.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        selectors.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 85));
        selectors.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 135));
        modeLabel.Dock = languageLabel.Dock = DockStyle.Fill;
        modeLabel.TextAlign = languageLabel.TextAlign = ContentAlignment.MiddleLeft;
        mode.Dock = language.Dock = DockStyle.Fill;
        mode.DropDownStyle = language.DropDownStyle = ComboBoxStyle.DropDownList;
        mode.Margin = new Padding(0, 10, 16, 10);
        language.Margin = new Padding(0, 10, 0, 10);
        language.Items.AddRange(new object[] { "English", "Italiano" });
        language.SelectedIndex = UiText.Code == "it" ? 1 : 0;
        selectors.Controls.Add(modeLabel, 0, 0);
        selectors.Controls.Add(mode, 1, 0);
        selectors.Controls.Add(languageLabel, 2, 0);
        selectors.Controls.Add(language, 3, 0);
        layout.Controls.Add(selectors, 0, 1);
        note.Dock = DockStyle.Fill;
        layout.Controls.Add(note, 0, 2);
        fields.Dock = DockStyle.Top;
        fields.AutoSize = true;
        fields.ColumnCount = 2;
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 165));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var folderPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0) };
        folderPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        folderPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        folder.Dock = DockStyle.Fill;
        browse.Dock = DockStyle.Fill;
        browse.Margin = new Padding(8, 0, 0, 0);
        browse.Click += delegate {
            using (var dialog = new FolderBrowserDialog { Description = T("Select the project folder", "Seleziona la cartella del progetto"), ShowNewFolderButton = false })
            {
                if (Directory.Exists(folder.Text)) dialog.SelectedPath = folder.Text;
                if (dialog.ShowDialog(this) == DialogResult.OK) folder.Text = dialog.SelectedPath;
            }
        };
        folderPanel.Controls.Add(folder, 0, 0);
        folderPanel.Controls.Add(browse, 1, 0);
        AddField(0, folderPanel);
        platform.DropDownStyle = ComboBoxStyle.DropDownList;
        platform.Items.AddRange(new object[] { "GitHub", "GitLab" });
        platform.SelectedIndex = 0;
        AddField(1, platform);
        AddField(2, email);
        AddField(3, username);
        AddField(4, repo);
        AddField(5, message);
        message.Text = firstMessage;
        layout.Controls.Add(fields, 0, 3);
        upload.BackColor = Accent;
        upload.ForeColor = Color.White;
        upload.FlatStyle = FlatStyle.Flat;
        upload.FlatAppearance.BorderSize = 0;
        upload.Dock = DockStyle.Left;
        upload.Width = 240;
        upload.Margin = new Padding(0, 12, 0, 5);
        upload.Click += StartUpload;
        layout.Controls.Add(upload, 0, 4);
        status.Dock = DockStyle.Fill;
        layout.Controls.Add(status, 0, 5);
        output.Multiline = true;
        output.ReadOnly = true;
        output.ScrollBars = ScrollBars.Both;
        output.WordWrap = false;
        output.Dock = DockStyle.Fill;
        output.BackColor = DarkSurface;
        output.Font = new Font("Consolas", 9F);
        layout.Controls.Add(output, 0, 6);
        footer.Dock = DockStyle.Fill;
        footer.TextAlign = ContentAlignment.MiddleLeft;
        footer.ForeColor = MutedText;
        layout.Controls.Add(footer, 0, 7);
        ApplyDarkTheme(this);
        upload.BackColor = Accent;
        upload.ForeColor = Color.White;
        upload.FlatAppearance.BorderSize = 0;
        upload.FlatAppearance.MouseOverBackColor = Color.FromArgb(61, 119, 238);
        upload.FlatAppearance.MouseDownBackColor = Color.FromArgb(36, 82, 183);
        note.ForeColor = footer.ForeColor = status.ForeColor = MutedText;
        RefreshText();
        platform.SelectedIndexChanged += delegate { if (!refreshing) RefreshText(); };
        mode.SelectedIndexChanged += delegate {
            if (refreshing) return;
            if (activeMode == 0) firstMessage = message.Text;
            else updateMessage = message.Text;
            activeMode = mode.SelectedIndex;
            message.Text = activeMode == 0 ? firstMessage : updateMessage;
            RefreshText();
        };
        language.SelectedIndexChanged += delegate {
            if (refreshing) return;
            string previousDefault = T("Initial upload", "Primo caricamento");
            if (activeMode == 0) firstMessage = message.Text;
            UiText.Code = language.SelectedIndex == 1 ? "it" : "en";
            if (firstMessage == previousDefault) firstMessage = T("Initial upload", "Primo caricamento");
            if (activeMode == 0) message.Text = firstMessage;
            RefreshText();
            try { settings.SaveLanguage(UiText.Code); }
            catch (Exception ex) {
                MessageBox.Show(this, T("The language changed, but the preference could not be saved.\n", "La lingua è cambiata, ma non è stato possibile salvare la preferenza.\n") + ex.Message, "Git Repository Uploader", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
        FormClosing += delegate(object sender, FormClosingEventArgs e) {
            if (busy) {
                e.Cancel = true;
                MessageBox.Show(this, T("Wait for the upload to finish. Complete or cancel any open sign-in request.", "Attendi la fine del caricamento. Completa o annulla eventuali richieste di accesso aperte."), T("Upload in progress", "Caricamento in corso"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int enabled = 1;
        if (DwmSetWindowAttribute(Handle, 20, ref enabled, sizeof(int)) != 0)
            DwmSetWindowAttribute(Handle, 19, ref enabled, sizeof(int));
    }

    static void ApplyDarkTheme(Control control)
    {
        control.ForeColor = LightText;
        var text = control as TextBox;
        var combo = control as ComboBox;
        var button = control as Button;
        if (text != null)
        {
            text.BackColor = DarkSurface;
            text.BorderStyle = BorderStyle.FixedSingle;
            text.HandleCreated += delegate { SetWindowTheme(text.Handle, "DarkMode_Explorer", null); };
        }
        else if (combo != null)
        {
            combo.BackColor = DarkSurface;
            combo.FlatStyle = FlatStyle.Flat;
            combo.DrawMode = DrawMode.OwnerDrawFixed;
            combo.DrawItem += delegate(object sender, DrawItemEventArgs e) {
                if (e.Index < 0) return;
                bool selected = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
                Color background = selected ? Accent : DarkSurface;
                using (var brush = new SolidBrush(background)) e.Graphics.FillRectangle(brush, e.Bounds);
                var bounds = new Rectangle(e.Bounds.X + 5, e.Bounds.Y, Math.Max(0, e.Bounds.Width - 10), e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, combo.Items[e.Index].ToString(), e.Font, bounds, LightText,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            };
            combo.HandleCreated += delegate { SetWindowTheme(combo.Handle, "DarkMode_Explorer", null); };
        }
        else if (button != null)
        {
            button.UseVisualStyleBackColor = false;
            button.BackColor = DarkSurface;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = DarkBorder;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(39, 48, 62);
            button.FlatAppearance.MouseDownBackColor = DarkBorder;
            button.Cursor = Cursors.Hand;
        }
        else if (control is Label) control.BackColor = Color.Transparent;
        else control.BackColor = DarkBackground;
        foreach (Control child in control.Controls) ApplyDarkTheme(child);
    }

    void AddField(int row, Control control)
    {
        fields.RowCount = row + 1;
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
        captions[row] = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        fields.Controls.Add(captions[row], 0, row);
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(0, 6, 0, 6);
        fields.Controls.Add(control, 1, row);
    }

    void RefreshText()
    {
        refreshing = true;
        bool first = activeMode == 0;
        SuspendLayout();
        heading.Text = T("Git repository uploader", "Caricamento repository Git");
        modeLabel.Text = T("Upload type", "Tipo di caricamento");
        languageLabel.Text = T("Language", "Lingua");
        mode.Items.Clear();
        mode.Items.AddRange(new object[] { T("First upload", "Primo caricamento"), T("Subsequent uploads", "Caricamenti successivi") });
        mode.SelectedIndex = activeMode;
        string service = platform.SelectedItem.ToString();
        string example = service == "GitLab" ? "https://gitlab.com/group/subgroup/project" : "https://github.com/username/repository";
        note.Text = first
            ? T("Select a folder and a repository already created on ", "Seleziona una cartella e un repository già creato su ") + service + ".\n" +
                T("Username and email are used for commits; sign-in is requested separately.\nHTTPS example: ", "Username ed email servono per i commit; l'accesso viene richiesto separatamente.\nEsempio HTTPS: ") + example
            : T("Select the linked folder and enter a commit message.\nThe upload uses the platform already configured for this folder.", "Seleziona la cartella collegata e scrivi il nome del commit.\nIl caricamento usa la piattaforma già configurata per questa cartella.");
        captions[0].Text = T("Project folder", "Cartella progetto");
        captions[1].Text = T("Platform", "Piattaforma");
        captions[2].Text = T("Account email", "Email account");
        captions[3].Text = T("Username / name", "Username / nome");
        captions[4].Text = T("Repository link", "Link repository");
        captions[5].Text = T("Commit message", "Nome commit");
        for (int row = 1; row <= 4; row++)
        {
            fields.RowStyles[row].Height = first ? 43 : 0;
            fields.GetControlFromPosition(0, row).Visible = first;
            fields.GetControlFromPosition(1, row).Visible = first;
        }
        browse.Text = T("Browse…", "Sfoglia…");
        upload.Text = first ? T("Upload to ", "Carica su ") + service : T("Upload changes", "Carica aggiornamenti");
        footer.Text = T("Automatic rebase when needed · Language preference saved", "Rebase automatico se necessario · Preferenza lingua salvata");
        RefreshStatus();
        ResumeLayout(true);
        refreshing = false;
    }

    void RefreshStatus()
    {
        status.Text = state == "busy" ? T("Upload in progress…", "Caricamento in corso…")
            : state == "done" ? T("Upload completed.", "Caricamento completato.")
            : state == "error" ? T("Upload not completed. Read the details below.", "Caricamento non completato. Leggi i dettagli qui sotto.")
            : T("Ready. All files not excluded by .gitignore will be included.", "Pronto. Verranno inclusi tutti i file non esclusi da .gitignore.");
    }

    void Log(string text)
    {
        if (InvokeRequired) { BeginInvoke(new Action<string>(Log), text); return; }
        output.AppendText(text + Environment.NewLine + Environment.NewLine);
    }

    async void StartUpload(object sender, EventArgs e)
    {
        if (busy) return;
        try
        {
            if (String.IsNullOrWhiteSpace(folder.Text)) throw new InvalidOperationException(T("Select an existing folder.", "Seleziona una cartella esistente."));
            string selected = Path.GetFullPath(folder.Text.Trim().Trim('"'));
            if (!Directory.Exists(selected)) throw new InvalidOperationException(T("Select an existing folder.", "Seleziona una cartella esistente."));
            string commit = message.Text.Trim();
            if (commit.Length == 0) throw new InvalidOperationException(T("Enter a commit message.", "Scrivi il nome del commit."));
            bool first = activeMode == 0;
            string user = username.Text.Trim();
            string mail = email.Text.Trim();
            string url = "";
            if (first)
            {
                if (user.Length == 0 || user.IndexOfAny(new [] { '\r', '\n', '\0' }) >= 0) throw new InvalidOperationException(T("Enter your username or commit author name.", "Inserisci il tuo username o il nome autore dei commit."));
                try { var address = new System.Net.Mail.MailAddress(mail); if (address.Address != mail) throw new FormatException(); }
                catch { throw new InvalidOperationException(T("Enter a valid email address.", "Inserisci un indirizzo email valido.")); }
                url = GitUploader.ValidateUrl(repo.Text, platform.SelectedItem.ToString());
            }
            string git = GitUploader.FindGit();
            busy = true;
            fields.Enabled = selectors.Enabled = upload.Enabled = false;
            output.Clear();
            status.ForeColor = MutedText;
            state = "busy";
            RefreshStatus();
            Log(T("Folder: ", "Cartella: ") + selected);
            var uploader = new GitUploader(git, Log);
            await Task.Run(delegate {
                if (first) uploader.First(selected, user, mail, url, commit);
                else uploader.Update(selected, commit);
            });
            status.ForeColor = SuccessText;
            state = "done";
            RefreshStatus();
            MessageBox.Show(this, T("Repository upload completed.", "Caricamento del repository completato."), "Git Repository Uploader", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            status.ForeColor = ErrorText;
            state = "error";
            RefreshStatus();
            Log(T("ERROR\n", "ERRORE\n") + ex.Message);
            Log(T("Any commit already created remains saved locally. You can retry the upload. Resolve any reported conflicts before retrying.", "Se il commit è già stato creato, rimane salvato nella cartella. Puoi riprovare il caricamento. Risolvi eventuali conflitti segnalati prima di riprovare."));
            MessageBox.Show(this, T("Upload not completed. See the details in the app window.", "Caricamento non completato. I dettagli sono nella finestra del programma."), "Git Repository Uploader", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { busy = false; fields.Enabled = selectors.Enabled = upload.Enabled = true; }
    }
}

static class Program
{
    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new SetupWindow());
    }
}

