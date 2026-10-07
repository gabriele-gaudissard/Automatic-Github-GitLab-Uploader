using System;
using System.Collections.Generic;
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
    readonly string recentFile;
    public AppSettings(string settingsFile = null)
    {
        file = settingsFile ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GithubSetup", "language.txt");
        recentFile = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(file)), "recent-folders.txt");
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
        SaveText(file, code);
    }
    public string[] LoadRecentFolders()
    {
        var folders = new List<string>();
        try
        {
            foreach (string line in File.ReadAllLines(recentFile, Encoding.UTF8))
            {
                try
                {
                    if (String.IsNullOrWhiteSpace(line) || !Path.IsPathRooted(line)) continue;
                    string path = Path.GetFullPath(line);
                    if (Directory.Exists(path) && !folders.Exists(delegate(string existing) { return SameFolder(existing, path); })) folders.Add(path);
                    if (folders.Count == 3) break;
                }
                catch (ArgumentException) { }
                catch (NotSupportedException) { }
                catch (IOException) { }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return folders.ToArray();
    }
    public void RememberUploadedFolder(string folder)
    {
        string path = Path.GetFullPath(folder);
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException(path);
        var recent = new List<string> { path };
        foreach (string existing in LoadRecentFolders())
        {
            if (!SameFolder(existing, path)) recent.Add(existing);
            if (recent.Count == 3) break;
        }
        SaveText(recentFile, String.Join(Environment.NewLine, recent.ToArray()));
    }
    static bool SameFolder(string first, string second)
    {
        return String.Equals(first.TrimEnd('\\', '/'), second.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
    }
    static void SaveText(string path, string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, contents, Encoding.UTF8);
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

public sealed class FolderBrowseMenu : ContextMenuStrip
{
    public FolderBrowseMenu(string[] recentFolders, Action openFolder, Action<string> selectRecent)
    {
        Font = new Font("Segoe UI", 10F);
        ForeColor = Color.FromArgb(232, 237, 245);
        BackColor = Color.FromArgb(28, 34, 44);
        Renderer = new ToolStripProfessionalRenderer(new DarkMenuColors());
        ShowImageMargin = false;
        var open = new ToolStripMenuItem(UiText.Get("Open folder…", "Apri cartella…")) { Name = "OpenFolder" };
        open.Click += delegate { openFolder(); };
        Items.Add(open);
        var recents = new ToolStripMenuItem(UiText.Get("Recents", "Recenti")) { Name = "RecentFolders" };
        Items.Add(recents);
        var dropdown = (ToolStripDropDownMenu)recents.DropDown;
        dropdown.Renderer = Renderer;
        dropdown.ShowImageMargin = false;
        dropdown.Font = Font;
        dropdown.ForeColor = ForeColor;
        dropdown.BackColor = BackColor;
        if (recentFolders.Length == 0)
            recents.DropDownItems.Add(new ToolStripMenuItem(UiText.Get("No recent uploads yet", "Nessun caricamento recente")) { Enabled = false });
        for (int index = 0; index < Math.Min(3, recentFolders.Length); index++)
        {
            string recent = recentFolders[index];
            string name = Path.GetFileName(recent.TrimEnd('\\', '/'));
            if (name.Length == 0) name = recent;
            var item = new ToolStripMenuItem((index + 1) + ".  " + name.Replace("&", "&&")) { Name = "RecentFolder" + index, ToolTipText = recent, ForeColor = ForeColor };
            item.Click += delegate { selectRecent(recent); };
            recents.DropDownItems.Add(item);
        }
        foreach (ToolStripItem item in Items) item.ForeColor = ForeColor;
    }
}

public sealed class DarkMenuColors : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground { get { return Color.FromArgb(28, 34, 44); } }
    public override Color ImageMarginGradientBegin { get { return ToolStripDropDownBackground; } }
    public override Color ImageMarginGradientMiddle { get { return ToolStripDropDownBackground; } }
    public override Color ImageMarginGradientEnd { get { return ToolStripDropDownBackground; } }
    public override Color MenuBorder { get { return Color.FromArgb(58, 70, 89); } }
    public override Color MenuItemBorder { get { return Color.FromArgb(47, 103, 218); } }
    public override Color MenuItemSelected { get { return MenuItemBorder; } }
    public override Color MenuItemSelectedGradientBegin { get { return MenuItemBorder; } }
    public override Color MenuItemSelectedGradientEnd { get { return MenuItemBorder; } }
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
    readonly ComboBox branchChoice = new DarkComboBox();
    readonly TextBox giteaPort = new TextBox { MaxLength = 5 };
    readonly Label branchHint = new Label();
    readonly Label portHint = new Label();
    readonly TextBox folder = new TextBox();
    readonly TextBox email = new TextBox();
    readonly TextBox username = new TextBox();
    readonly TextBox repo = new TextBox();
    readonly TextBox message = new TextBox();
    readonly TextBox output = new TextBox();
    readonly Button browse = new Button();
    FolderBrowseMenu folderMenu;
    readonly Button upload = new Button();
    readonly Label heading = new Label();
    readonly Label note = new Label();
    readonly Label status = new Label();
    readonly Label footer = new Label();
    readonly Label modeLabel = new Label();
    readonly Label languageLabel = new Label();
    readonly Label[] captions = new Label[8];
    readonly TableLayoutPanel fields = new TableLayoutPanel();
    readonly TableLayoutPanel selectors = new TableLayoutPanel();
    bool busy;
    bool refreshing;
    int activeMode;
    string firstBranchChoice = "main";
    string updateBranchChoice = "";
    string[] branchSuggestions = new [] { "main" };
    bool updateIsGitea;
    string firstMessage;
    string updateMessage = "";
    string state = "ready";
    readonly System.Threading.CancellationTokenSource updateCancellation = new System.Threading.CancellationTokenSource();
    PendingUpdate pendingUpdate;
    bool updaterStarted;
    string updateState = "idle";

    public ComboBox UploadType { get { return mode; } }
    public ComboBox LanguageSelector { get { return language; } }
    public ComboBox PlatformSelector { get { return platform; } }
    public TextBox CommitMessage { get { return message; } }
    public ComboBox BranchSelector { get { return branchChoice; } }
    public TextBox GiteaPort { get { return giteaPort; } }
    public TextBox ProjectFolder { get { return folder; } }
    public bool PortFieldVisible { get { return fields.RowStyles[5].Height > 0; } }
    public bool FirstFieldsVisible { get { return fields.RowStyles[1].Height > 0; } }
    static string T(string en, string it) { return UiText.Get(en, it); }

    public SetupWindow(AppSettings appSettings = null, bool automaticUpdates = false)
    {
        settings = appSettings ?? new AppSettings();
        UiText.Code = settings.LoadLanguage();
        firstMessage = T("Initial upload", "Primo caricamento");
        Text = "Git Repository Uploader";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(820, 806);
        MinimumSize = new Size(790, 796);
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
            if (folderMenu != null) folderMenu.Dispose();
            folderMenu = new FolderBrowseMenu(settings.LoadRecentFolders(), OpenFolder, delegate(string recent) {
                if (Directory.Exists(recent)) folder.Text = recent;
                else MessageBox.Show(this, T("This folder no longer exists. Choose Open folder to select another one.", "Questa cartella non esiste più. Scegli Apri cartella per selezionarne un'altra."), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            });
            folderMenu.Show(browse, new Point(0, browse.Height));
        };
        folderPanel.Controls.Add(folder, 0, 0);
        folderPanel.Controls.Add(browse, 1, 0);
        AddField(0, folderPanel);
        platform.DropDownStyle = ComboBoxStyle.DropDownList;
        platform.Items.AddRange(new object[] { "GitHub", "GitLab", "Gitea" });
        platform.SelectedIndex = 0;
        AddField(1, platform);
        AddField(2, email);
        AddField(3, username);
        AddField(4, repo);
        var portPanel = new TableLayoutPanel { ColumnCount = 2, Margin = new Padding(0) };
        portPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        portPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        giteaPort.Dock = portHint.Dock = DockStyle.Fill;
        portHint.TextAlign = ContentAlignment.MiddleLeft;
        portHint.Margin = new Padding(10, 0, 0, 0);
        portPanel.Controls.Add(giteaPort, 0, 0);
        portPanel.Controls.Add(portHint, 1, 0);
        AddField(5, portPanel);
        var branchPanel = new TableLayoutPanel { ColumnCount = 2, Margin = new Padding(0) };
        branchPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        branchPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        branchChoice.DropDownStyle = ComboBoxStyle.DropDown;
        branchChoice.Dock = branchHint.Dock = DockStyle.Fill;
        branchChoice.Margin = new Padding(0, 0, 12, 0);
        branchHint.TextAlign = ContentAlignment.MiddleLeft;
        branchHint.Font = new Font("Segoe UI", 9F);
        branchPanel.Controls.Add(branchChoice, 0, 0);
        branchPanel.Controls.Add(branchHint, 1, 0);
        AddField(6, branchPanel);
        AddField(7, message);
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
        branchChoice.TextChanged += delegate {
            if (refreshing) return;
            string value = branchChoice.Text == T("Default branch", "Branch principale") ? "\0default" : branchChoice.Text;
            if (activeMode == 0) firstBranchChoice = value;
            else updateBranchChoice = value;
        };
        branchChoice.DropDown += delegate { RefreshBranchSuggestions(); };
        branchChoice.MouseClick += delegate(object sender, MouseEventArgs e) {
            if (!busy && e.X < branchChoice.Width - SystemInformation.VerticalScrollBarWidth && !branchChoice.DroppedDown)
                branchChoice.DroppedDown = true;
        };
        folder.Leave += delegate { RefreshBranchSuggestions(); };
        folder.TextChanged += delegate { if (!busy && !refreshing) RefreshBranchSuggestions(); };
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
            else {
                updateCancellation.Cancel();
                if (pendingUpdate != null && !updaterStarted) {
                    try { AutoUpdater.StartApply(pendingUpdate, false); updaterStarted = true; }
                    catch (Exception ex) { Log(T("The app update could not be installed: ", "Non è stato possibile installare l'aggiornamento: ") + ex.Message); }
                }
            }
        };
        if (automaticUpdates) Shown += async delegate { await CheckForAppUpdate(); };
    }

    bool HasWorkInForm()
    {
        return folder.TextLength > 0 || email.TextLength > 0 || username.TextLength > 0 || repo.TextLength > 0 ||
            giteaPort.TextLength > 0 || firstBranchChoice != "main" || updateBranchChoice.Length > 0 ||
            firstMessage != T("Initial upload", "Primo caricamento") || updateMessage.Length > 0 ||
            message.Text != (activeMode == 0 ? T("Initial upload", "Primo caricamento") : "");
    }

    async Task CheckForAppUpdate()
    {
        updateState = "checking";
        RefreshFooter();
        try {
            PendingUpdate update = await AutoUpdater.StageAsync(delegate(string state) {
                if (!IsDisposed && IsHandleCreated) BeginInvoke(new Action(delegate { updateState = state; RefreshFooter(); }));
            }, updateCancellation.Token);
            if (IsDisposed || updateCancellation.IsCancellationRequested) return;
            pendingUpdate = update;
            if (update == null) { updateState = "current"; RefreshFooter(); return; }
            if (busy || HasWorkInForm()) {
                updateState = "ready";
                RefreshFooter();
                return;
            }
            updateState = "installing";
            RefreshFooter();
            AutoUpdater.StartApply(update, true);
            updaterStarted = true;
            Close();
        }
        catch (Exception) {
            if (!IsDisposed && !updateCancellation.IsCancellationRequested) {
                pendingUpdate = null;
                updateState = "unavailable";
                RefreshFooter();
            }
        }
    }

    void RefreshFooter()
    {
        string message = updateState == "checking" ? T("Checking for updates…", "Controllo aggiornamenti…")
            : updateState == "downloading" ? T("Downloading the app update…", "Download aggiornamento del programma…")
            : updateState == "ready" ? T("App update ready: it will install when you close this window.", "Aggiornamento pronto: verrà installato alla chiusura della finestra.")
            : updateState == "installing" ? T("Updating and restarting the app…", "Aggiornamento e riavvio del programma…")
            : updateState == "unavailable" ? T("Update check unavailable; you can keep using the app.", "Controllo aggiornamenti non disponibile; puoi usare il programma.")
            : T("Automatic rebase when needed · Language preference saved", "Rebase automatico se necessario · Preferenza lingua salvata");
        footer.Text = "v" + AutoUpdater.CurrentVersion.ToString(3) + " · " + message;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UseDarkTitleBar(Handle);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && folderMenu != null) folderMenu.Dispose();
        base.Dispose(disposing);
    }

    void OpenFolder()
    {
        try
        {
            string selected = WindowsFolderPicker.Show(this, folder.Text);
            if (selected != null) folder.Text = selected;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, T("The folder selection window could not be opened.\n", "Non è stato possibile aprire la finestra di selezione cartella.\n") + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    public static void UseDarkTitleBar(IntPtr handle)
    {
        int enabled = 1;
        if (DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int)) != 0)
            DwmSetWindowAttribute(handle, 19, ref enabled, sizeof(int));
    }

    public static void ApplyDarkTheme(Control control)
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

    void RefreshBranchSuggestions()
    {
        try {
            var git = new GitUploader(GitUploader.FindGit(), delegate(string text) {});
            branchSuggestions = git.SuggestedBranches(folder.Text.Trim().Trim('"'));
            string url = git.ConnectedUrl(folder.Text.Trim().Trim('"'));
            updateIsGitea = false;
            if (url != null) {
                try { GitUploader.ValidateUrl(url, "Gitea"); updateIsGitea = true; }
                catch (InvalidOperationException) { }
            }
        }
        catch { branchSuggestions = new [] { "main" }; updateIsGitea = false; }
        bool previous = refreshing;
        refreshing = true;
        string typedBranch = branchChoice.Text;
        branchChoice.Items.Clear();
        branchChoice.Items.Add(T("Default branch", "Branch principale"));
        foreach (string name in branchSuggestions) branchChoice.Items.Add(name);
        branchChoice.Text = typedBranch;
        RefreshPortVisibility();
        refreshing = previous;
    }

    void RefreshPortVisibility()
    {
        bool showPort = activeMode == 0 ? platform.SelectedItem.ToString() == "Gitea" : updateIsGitea;
        fields.RowStyles[5].Height = showPort ? 43 : 0;
        fields.GetControlFromPosition(0, 5).Visible = showPort;
        fields.GetControlFromPosition(1, 5).Visible = showPort;
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
        string example = service == "Gitea" ? "https://gitea.example.com/username/repository" : service == "GitLab" ? "https://gitlab.com/group/subgroup/project" : "https://github.com/username/repository";
        string signIn = service == "Gitea"
            ? T("Name/email identify commits; sign in with username and an access token when asked.\nHTTPS example: ", "Nome/email identificano i commit; quando richiesto, accedi con username e token.\nEsempio HTTPS: ")
            : T("Username and email are used for commits; sign-in is requested separately.\nHTTPS example: ", "Username ed email servono per i commit; l'accesso viene richiesto separatamente.\nEsempio HTTPS: ");
        note.Text = first
            ? T("Select a folder and a repository already created on ", "Seleziona una cartella e un repository già creato su ") + service + ".\n" +
                signIn + example
            : T("Select the linked folder and enter a commit message.\nThe upload uses the platform already configured for this folder.", "Seleziona la cartella collegata e scrivi il nome del commit.\nIl caricamento usa la piattaforma già configurata per questa cartella.");
        captions[0].Text = T("Project folder", "Cartella progetto");
        captions[1].Text = T("Platform", "Piattaforma");
        captions[2].Text = T("Account email", "Email account");
        captions[3].Text = T("Username / name", "Username / nome");
        captions[4].Text = T("Repository link", "Link repository");
        captions[5].Text = T("Gitea HTTPS port", "Porta HTTPS Gitea");
        captions[6].Text = T("Branch", "Branch");
        captions[7].Text = T("Commit message", "Nome commit");
        branchHint.Text = first ? T("Type a name or choose a suggestion.", "Scrivi un nome o scegli un suggerimento.") : T("Empty: current branch. Type or choose.", "Vuoto: branch attuale. Scrivi o scegli.");
        portHint.Text = T("Optional: empty uses the link's port (HTTPS default: 443).", "Facoltativa: vuota usa la porta del link (HTTPS: 443).");
        branchChoice.Items.Clear();
        branchChoice.Items.Add(T("Default branch", "Branch principale"));
        foreach (string name in branchSuggestions) branchChoice.Items.Add(name);
        string branchText = first ? firstBranchChoice : updateBranchChoice;
        branchChoice.Text = branchText == "\0default" ? T("Default branch", "Branch principale") : branchText;
        for (int row = 1; row <= 4; row++)
        {
            fields.RowStyles[row].Height = first ? 43 : 0;
            fields.GetControlFromPosition(0, row).Visible = first;
            fields.GetControlFromPosition(1, row).Visible = first;
        }
        RefreshPortVisibility();
        browse.Text = T("Browse…", "Sfoglia…");
        upload.Text = first ? T("Upload to ", "Carica su ") + service : T("Upload changes", "Carica aggiornamenti");
        RefreshFooter();
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
            bool defaultBranch = branchChoice.SelectedIndex == 0;
            string selectedBranch = defaultBranch || String.IsNullOrWhiteSpace(branchChoice.Text) ? null : branchChoice.Text.Trim();
            string user = username.Text.Trim();
            string mail = email.Text.Trim();
            string url = "";
            if (first)
            {
                if (user.Length == 0 || user.IndexOfAny(new [] { '\r', '\n', '\0' }) >= 0) throw new InvalidOperationException(T("Enter your username or commit author name.", "Inserisci il tuo username o il nome autore dei commit."));
                try { var address = new System.Net.Mail.MailAddress(mail); if (address.Address != mail) throw new FormatException(); }
                catch { throw new InvalidOperationException(T("Enter a valid email address.", "Inserisci un indirizzo email valido.")); }
                url = GitUploader.ValidateUrl(repo.Text, platform.SelectedItem.ToString(), platform.SelectedItem.ToString() == "Gitea" ? giteaPort.Text : null);
            }
            string git = GitUploader.FindGit();
            string updatePort = !first && updateIsGitea ? giteaPort.Text : null;
            busy = true;
            fields.Enabled = selectors.Enabled = upload.Enabled = false;
            output.Clear();
            status.ForeColor = MutedText;
            state = "busy";
            RefreshStatus();
            Log(T("Folder: ", "Cartella: ") + selected);
            var uploader = new GitUploader(git, Log);
            await Task.Run(delegate {
                if (first) uploader.First(selected, user, mail, url, commit, selectedBranch, defaultBranch);
                else uploader.Update(selected, commit, selectedBranch, defaultBranch, updatePort);
            });
            try { settings.RememberUploadedFolder(selected); }
            catch (Exception ex) {
                Log(T("Upload succeeded, but recent folders could not be saved: ", "Caricamento riuscito, ma non è stato possibile salvare le cartelle recenti: ") + ex.Message);
            }
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
    static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--apply-update") return AutoUpdater.Apply(args[1]);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new SetupWindow(null, !(args.Length == 1 && args[0] == "--skip-update")));
        return 0;
    }
}


