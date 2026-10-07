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

public sealed class RecentFolderPicker : Form
{
    readonly TextBox path = new TextBox();
    readonly TreeView tree = new TreeView();
    readonly ToolTip tips = new ToolTip();
    public string SelectedPath { get; private set; }
    static string T(string en, string it) { return UiText.Get(en, it); }

    public RecentFolderPicker(string initialFolder, string[] recentFolders)
    {
        Text = T("Select project folder", "Seleziona cartella progetto");
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(650, 590);
        MinimumSize = new Size(550, 520);
        Font = new Font("Segoe UI", 10F);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 6 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, recentFolders.Length == 0 ? 32 : Math.Min(3, recentFolders.Length) * 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        Controls.Add(layout);
        layout.Controls.Add(new Label { Text = T("Recent uploads", "Caricamenti recenti"), Dock = DockStyle.Fill, Font = new Font("Segoe UI", 13F, FontStyle.Bold) }, 0, 0);
        var recentPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
        layout.Controls.Add(recentPanel, 0, 1);
        if (recentFolders.Length == 0)
            recentPanel.Controls.Add(new Label { Text = T("No recent uploads yet.", "Nessun caricamento recente."), Dock = DockStyle.Fill });
        for (int index = 0; index < Math.Min(3, recentFolders.Length); index++)
        {
            string recent = recentFolders[index];
            string display = Path.GetFileName(recent.TrimEnd('\\', '/'));
            if (display.Length == 0) display = recent;
            var button = new Button { Name = "RecentFolder" + index, Text = (index + 1) + ".  " + display, Tag = recent, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Margin = new Padding(0, 0, 0, 5) };
            recentPanel.RowCount = index + 1;
            recentPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            recentPanel.Controls.Add(button, 0, index);
            tips.SetToolTip(button, recent);
            button.Click += delegate { path.Text = recent; ChooseFolder(); };
        }
        layout.Controls.Add(new Label { Text = T("Or choose another folder", "Oppure scegli un'altra cartella"), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 2);
        tree.Dock = DockStyle.Fill;
        tree.HideSelection = false;
        tree.BeforeExpand += delegate(object sender, TreeViewCancelEventArgs e) { LoadChildren(e.Node); };
        tree.AfterSelect += delegate(object sender, TreeViewEventArgs e) { path.Text = (string)e.Node.Tag; };
        foreach (DriveInfo drive in DriveInfo.GetDrives()) tree.Nodes.Add(FolderNode(drive.Name));
        layout.Controls.Add(tree, 0, 3);
        path.Dock = DockStyle.Fill;
        path.Margin = new Padding(0, 8, 0, 5);
        if (Directory.Exists(initialFolder)) path.Text = Path.GetFullPath(initialFolder);
        layout.Controls.Add(path, 0, 4);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var choose = new Button { Name = "ChooseFolder", Text = T("Select folder", "Seleziona cartella"), Width = 160, Height = 34 };
        var cancel = new Button { Text = T("Cancel", "Annulla"), Width = 110, Height = 34, DialogResult = DialogResult.Cancel };
        choose.Click += delegate { ChooseFolder(); };
        actions.Controls.Add(choose);
        actions.Controls.Add(cancel);
        layout.Controls.Add(actions, 0, 5);
        AcceptButton = choose;
        CancelButton = cancel;
        SetupWindow.ApplyDarkTheme(this);
        Shown += delegate { RevealInitialFolder(); };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        SetupWindow.UseDarkTitleBar(Handle);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) tips.Dispose();
        base.Dispose(disposing);
    }
    static TreeNode FolderNode(string folder)
    {
        string name = Path.GetFileName(folder.TrimEnd('\\', '/'));
        var node = new TreeNode(name.Length == 0 ? folder : name) { Tag = folder };
        node.Nodes.Add(new TreeNode());
        return node;
    }
    static void LoadChildren(TreeNode node)
    {
        if (node.Nodes.Count != 1 || node.Nodes[0].Tag != null) return;
        node.Nodes.Clear();
        try
        {
            string[] children = Directory.GetDirectories((string)node.Tag);
            Array.Sort(children, StringComparer.CurrentCultureIgnoreCase);
            foreach (string child in children) node.Nodes.Add(FolderNode(child));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    void RevealInitialFolder()
    {
        string initial = path.Text;
        if (!Directory.Exists(initial)) return;
        string root = Path.GetPathRoot(initial);
        TreeNode node = null;
        foreach (TreeNode candidate in tree.Nodes)
            if (String.Equals((string)candidate.Tag, root, StringComparison.OrdinalIgnoreCase)) node = candidate;
        if (node == null) { node = FolderNode(root); tree.Nodes.Add(node); }
        string remainder = initial.Substring(root.Length).Trim('\\', '/');
        foreach (string part in remainder.Split(new [] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries))
        {
            LoadChildren(node);
            node.Expand();
            TreeNode next = null;
            foreach (TreeNode child in node.Nodes)
                if (String.Equals(child.Text, part, StringComparison.OrdinalIgnoreCase)) { next = child; break; }
            if (next == null) break;
            node = next;
        }
        tree.SelectedNode = node;
        node.EnsureVisible();
        path.Text = initial;
    }
    void ChooseFolder()
    {
        try
        {
            string selected = Path.GetFullPath(path.Text.Trim().Trim('"'));
            if (String.IsNullOrWhiteSpace(path.Text) || !Directory.Exists(selected)) throw new DirectoryNotFoundException();
            SelectedPath = selected;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            if (!(ex is ArgumentException) && !(ex is IOException) && !(ex is NotSupportedException) && !(ex is UnauthorizedAccessException)) throw;
            MessageBox.Show(this, T("Select an existing folder.", "Seleziona una cartella esistente."), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
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
            using (var dialog = new RecentFolderPicker(folder.Text, settings.LoadRecentFolders()))
            {
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
        UseDarkTitleBar(Handle);
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
        var tree = control as TreeView;
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
        else if (tree != null)
        {
            tree.BackColor = DarkSurface;
            tree.LineColor = DarkBorder;
            tree.BorderStyle = BorderStyle.FixedSingle;
            tree.HandleCreated += delegate { SetWindowTheme(tree.Handle, "DarkMode_Explorer", null); };
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
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new SetupWindow());
    }
}

