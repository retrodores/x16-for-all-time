using System;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace CommanderX16
{
    internal sealed partial class MainForm : Form
    {
        private static Color Navy => SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(20, 36, 57);
        private static readonly Color Teal = Color.FromArgb(24, 113, 110);
        private readonly SettingsStore store;
        private readonly TabControl pages = new TabControl { Dock = DockStyle.Fill };
        private readonly TabPage emulatorPage = new TabPage("Emulators") { Padding = new Padding(12) };
        private readonly Label emulatorRequired = new Label
        {
            Text = "First, install or register an emulator to unlock Start, X16 Files, and SD Card Kit.",
            AutoSize = true, ForeColor = Navy, Margin = new Padding(0, 0, 0, 12)
        };
        private readonly ComboBox emulators = Combo(), profiles = Combo();
        private readonly TextBox preview = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical };
        private readonly Button run = Button("Run"), copy = Button("Copy PowerShell");
        private readonly Label launchStatus = new Label { AutoSize = true, ForeColor = Navy };
        private readonly Label status = new Label { AutoSize = true, ForeColor = Navy };
        private readonly Button editBuild = Button("Edit emulator…"), removeBuild = Button("Remove emulator…");
        private readonly Button openBuildFolder = Button("Open folder");
        private readonly Button newProfile = Button("New profile…"), editProfile = Button("Edit profile…"),
            duplicateProfile = Button("Duplicate…"), deleteProfile = Button("Delete…"), defaultProfile = Button("Set as default");
        private readonly Label profileDefault = new Label { AutoSize = true };
        private readonly TextBox emulatorPath = new TextBox { ReadOnly = true, Dock = DockStyle.Fill };
        private bool binding;
        private bool launching;

        public MainForm(SettingsStore store)
        {
            this.store = store;
            Text = Program.Title + " " + typeof(MainForm).Assembly.GetName().Version.ToString(3);
            Font = new Font("Segoe UI", 10);
            AutoScaleMode = AutoScaleMode.Dpi;
            MinimumSize = new Size(700, 630);
            ClientSize = new Size(880, 770);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = SystemInformation.HighContrast ? SystemColors.Control : Color.FromArgb(234, 241, 241);

            var layout = Table();
            layout.AutoSize = false;
            layout.Padding = new Padding(20, 14, 20, 10);
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 1;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Controls.Add(layout);

            var title = new Label
            {
                Text = Program.Title,
                Font = new Font("Segoe UI Semibold", 20),
                AutoSize = true, ForeColor = Navy, Margin = Padding.Empty
            };
            var header = Table();
            header.ColumnCount = 2;
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.Controls.Add(title, 0, 0);
            var about = Button("&About…", () => Attempt(ShowAbout));
            about.Anchor = AnchorStyles.Right;
            header.Controls.Add(about, 1, 0);
            AddRow(layout, header);
            var start = new TabPage("Start") { Padding = new Padding(12) };
            var catalogPage = new TabPage("X16 Files") { Padding = new Padding(12) };
            var kitPage = new TabPage("SD Card Kit") { Padding = new Padding(12) };
            pages.TabPages.AddRange(new[] { start, catalogPage, kitPage, emulatorPage });
            pages.Selecting += (sender, args) => args.Cancel = args.TabPage?.Enabled == false;
            pages.DrawItem += (sender, args) =>
            {
                var page = pages.TabPages[args.Index];
                if (args.Index == pages.SelectedIndex)
                    args.Graphics.FillRectangle(SystemBrushes.Control, args.Bounds);
                else
                    args.DrawBackground();
                TextRenderer.DrawText(args.Graphics, page.Text, pages.Font, args.Bounds,
                    page.Enabled ? SystemColors.ControlText : SystemColors.GrayText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                args.DrawFocusRectangle();
            };
            AddRow(layout, pages, SizeType.Percent, 100);
            BuildStartPage(start, pages, catalogPage);
            BuildCatalogPage(catalogPage, pages, start);
            BuildKitPage(kitPage, pages, start);

            var emulatorBody = Table();
            AddScrollableContent(emulatorPage, emulatorBody);
            AddRow(emulatorBody, emulatorRequired);
            WrapText(emulatorRequired, emulatorBody);
            var selections = Table();
            var buildActions = Flow(Button("Install or update…", ShowInstall), Button("Register existing…", () => EditEmulator(null)));
            buildActions.Dock = DockStyle.None;
            buildActions.Anchor = AnchorStyles.Right;
            buildActions.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            AddRow(selections, buildActions);
            AddRow(selections, Pair("Emulator", emulators, editBuild, removeBuild));
            AddRow(selections, Pair("Executable", emulatorPath, openBuildFolder));
            AddRow(emulatorBody, selections);

            var profileSection = Table();
            profileSection.Margin = new Padding(0, 18, 0, 6);
            AddRow(profileSection, Pair("Profiles for this build", profiles));
            AddRow(profileSection, Flow(newProfile, editProfile, duplicateProfile, deleteProfile, defaultProfile));
            AddRow(profileSection, profileDefault);
            AddRow(emulatorBody, profileSection);
            AddRow(emulatorBody, status);
            WrapText(status, emulatorBody);

            editBuild.Click += (sender, args) => EditEmulator(SelectedEmulator);
            openBuildFolder.Click += (sender, args) => Attempt(OpenEmulatorFolder);
            removeBuild.Click += (sender, args) => Attempt(RemoveEmulator);
            emulators.SelectedIndexChanged += (sender, args) => { if (!binding) BindProfiles(null); };
            profiles.SelectedIndexChanged += (sender, args) => { if (!binding) UpdateProfileActions(); };
            newProfile.Click += (sender, args) => Attempt(() => SaveProfile(null, false));
            editProfile.Click += (sender, args) => Attempt(() => SaveProfile(SelectedProfile, false));
            duplicateProfile.Click += (sender, args) => Attempt(() => SaveProfile(SelectedProfile, true));
            deleteProfile.Click += (sender, args) => Attempt(RemoveProfile);
            defaultProfile.Click += (sender, args) => Attempt(SetDefaultProfile);
            FormClosing += (sender, args) => { if (launching || catalogBusy || loadingKit) args.Cancel = true; };
            Shown += (sender, args) =>
            {
                if (!store.IsFirstRun || store.Data.Emulators.Count != 0) return;
                LauncherDialogs.Message(this, "No emulator is registered. On the Emulators tab, choose Install or update " +
                    "to install X16Emu, or Register existing if you already have it.", "Set up an emulator");
                // Persist the initial settings so the first-run dialog only appears once.
                Attempt(() => store.Save(settings => { }));
            };
            BindEmulators(null, null);
        }

        private Emulator SelectedEmulator => emulators.SelectedItem as Emulator;
        private Profile SelectedProfile => profiles.SelectedItem as Profile;

        private void BindEmulators(string emulatorId, string profileId)
        {
            binding = true;
            emulators.Items.Clear();
            emulators.Items.AddRange(store.Data.Emulators.Cast<object>().ToArray());
            SelectById(emulators, store.Data.Emulators.FindIndex(e => e.Id == emulatorId));
            binding = false;
            UpdateEmulatorRequirement();
            BindProfiles(profileId);
        }

        private void UpdateEmulatorRequirement()
        {
            bool hasEmulator = store.Data.Emulators.Count != 0;
            emulatorRequired.Visible = !hasEmulator;
            if (!hasEmulator) pages.SelectedTab = emulatorPage;
            foreach (TabPage page in pages.TabPages)
                page.Enabled = hasEmulator || page == emulatorPage;
            pages.DrawMode = hasEmulator ? TabDrawMode.Normal : TabDrawMode.OwnerDrawFixed;
            pages.Invalidate();
        }

        private void BindProfiles(string profileId)
        {
            binding = true;
            profiles.Items.Clear();
            var available = store.Data.Profiles.Where(profile =>
                SelectedEmulator != null && profile.EmulatorId == SelectedEmulator.Id).ToArray();
            profiles.Items.AddRange(available.Cast<object>().ToArray());
            SelectById(profiles, Array.FindIndex(available, profile => profile.Id == (profileId ?? SelectedEmulator?.DefaultProfileId)));
            binding = false;
            UpdateProfileActions();
            RefreshPreview();
        }

        private void UpdateProfileActions()
        {
            var emulator = SelectedEmulator;
            emulatorPath.Text = emulator?.Exe ?? "";
            editBuild.Enabled = removeBuild.Enabled = openBuildFolder.Enabled = newProfile.Enabled = emulator != null;
            profiles.Enabled = editProfile.Enabled = duplicateProfile.Enabled = deleteProfile.Enabled = SelectedProfile != null;
            var currentDefault = store.Data.DefaultProfile(emulator);
            defaultProfile.Enabled = SelectedProfile != null && SelectedProfile.Id != currentDefault?.Id;
            profileDefault.Text = emulator == null ? "" : currentDefault?.Name == null
                ? "New configurations use emulator defaults."
                : "Default profile for new configurations: " + currentDefault.Name;
        }

        private void SaveProfile(Profile source, bool duplicate)
        {
            var emulator = SelectedEmulator;
            bool create = source == null || duplicate;
            string initialName = duplicate ? source.Name + " (copy)" : source?.Name ?? "";
            string id = create ? Guid.NewGuid().ToString("N") : source.Id;
            string title = (create ? "New profile – " : "Edit profile – ") + emulator.Name;

            using (var dialog = new LaunchSettingsForm(store.Data, title, emulator, source,
                profileName: initialName, profileId: id))
            {
                if (LauncherDialogs.Show(dialog, this) != DialogResult.OK) return;
                var saved = dialog.Options;
                saved.Id = id;
                saved.EmulatorId = emulator.Id;
                saved.Name = dialog.EntryName;
                store.Save(data =>
                {
                    int index = data.Profiles.FindIndex(profile => profile.Id == id);
                    if (index >= 0) data.Profiles[index] = saved;
                    else data.Profiles.Add(saved);
                });
                BindEmulators(emulator.Id, id);
                status.Text = "Profile saved";
            }
        }

        private void RemoveProfile()
        {
            var emulator = SelectedEmulator;
            var profile = SelectedProfile;
            string message = "Delete the profile " + profile.Name + "?";
            if (emulator.DefaultProfileId == profile.Id)
                message += Environment.NewLine + "A fresh default profile will be created for this emulator.";
            if (!LauncherDialogs.Confirm(this, message, "Delete profile"))
                return;
            store.Save(data =>
            {
                var build = data.FindEmulator(emulator.Id);
                if (build.DefaultProfileId == profile.Id) build.DefaultProfileId = null;
                data.Profiles.RemoveAll(item => item.Id == profile.Id);
            });
            BindEmulators(emulator.Id, null);
            status.Text = "Profile deleted";
        }

        private void SetDefaultProfile()
        {
            string emulatorId = SelectedEmulator.Id;
            string profileId = SelectedProfile.Id;
            store.Save(data =>
            {
                var build = data.FindEmulator(emulatorId);
                build.DefaultProfileId = profileId;
            });
            BindEmulators(emulatorId, profileId);
            status.Text = "Default profile updated";
        }

        private void EditEmulator(Emulator existing)
        {
            Attempt(() =>
            {
                using (var dialog = new EmulatorSettingsForm(existing))
                {
                    if (LauncherDialogs.Show(dialog, this) != DialogResult.OK) return;
                    var other = store.Data.Emulators.FirstOrDefault(e => e.Id != existing?.Id && Same(e.Exe, dialog.Executable));
                    if (other != null)
                    {
                        BindEmulators(other.Id, null);
                        throw new InvalidOperationException("This executable is already registered as " + other.Name + ". Use Edit emulator to update it.");
                    }
                    string id = existing?.Id ?? Guid.NewGuid().ToString("N");
                    string profileId = SelectedProfile?.Id;
                    string overlay = SerialNetworkOverlay.Detect(dialog.Executable);
                    store.Save(data =>
                    {
                        var emulator = data.Emulators.FirstOrDefault(e => e.Id == id);
                        if (emulator == null)
                        {
                            emulator = new Emulator { Id = id };
                            data.Emulators.Add(emulator);
                        }
                        emulator.Name = dialog.BuildName;
                        if (!Same(emulator.Exe, dialog.Executable))
                            emulator.InstallRoot = null;
                        emulator.Exe = dialog.Executable;
                        emulator.UpstreamOptions = dialog.StandardOptions;
                        emulator.SerialOverlayVersion = overlay;
                    });
                    BindEmulators(id, profileId);
                    RefreshPrograms();
                    status.Text = existing == null ? "Emulator registered" : "Emulator updated";
                }
            });
        }

        private void OpenEmulatorFolder()
        {
            var emulator = SelectedEmulator;
            if (emulator == null) return;
            var folder = EmulatorFolders.Root(emulator);
            if (!Directory.Exists(folder))
                throw new DirectoryNotFoundException("The emulator folder could not be found." + Environment.NewLine + folder);
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }

        private void RemoveEmulator()
        {
            var emulator = SelectedEmulator;
            if (emulator == null) return;
            var affected = store.Data.Programs.Where(program => program.EmulatorId == emulator.Id).ToArray();
            string message = "Choose what to do with " + emulator.Name + "." + Environment.NewLine + Environment.NewLine +
                "Remove from list leaves the folder on disk. Delete emulator files permanently deletes the folder and its contents.";
            if (affected.Length != 0)
            {
                string names = string.Join(Environment.NewLine, affected.Take(10).Select(program => "• " + program.Name));
                if (affected.Length > 10)
                    names += Environment.NewLine + "…and " + (affected.Length - 10) + " more.";
                message += Environment.NewLine + Environment.NewLine + "These entries will use the Start fallback when available:" +
                    Environment.NewLine + names;
            }
            if (store.Data.StartEmulatorId == emulator.Id)
                message += Environment.NewLine + Environment.NewLine + "Start will use the first available emulator unless you choose new Start defaults.";
            string folder = EmulatorFolders.Root(emulator);
            bool filesDeleted = false;
            using (var dialog = new Form
            {
                Text = "Remove emulator", Font = Font, AutoScaleMode = AutoScaleMode.Dpi,
                StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(650, 345),
                MinimizeBox = false, MaximizeBox = false
            })
            {
                var body = Table();
                body.Padding = new Padding(14);
                var explanation = new Label { Text = message, AutoSize = true };
                AddRow(body, explanation);
                WrapText(explanation, body);
                var path = Entry();
                path.ReadOnly = true;
                path.Text = folder;
                AddRow(body, Pair("Emulator directory", path));
                var cancel = Button("Cancel");
                cancel.DialogResult = DialogResult.Cancel;
                var remove = Button("Remove from list");
                remove.DialogResult = DialogResult.OK;
                var delete = Button("Delete emulator files…");
                delete.DialogResult = DialogResult.Yes;
                delete.Enabled = Directory.Exists(folder);
                var actions = Flow(remove, delete, cancel);
                AddScrollableContent(dialog, body, actions);
                dialog.AcceptButton = cancel;
                dialog.CancelButton = cancel;
                var choice = LauncherDialogs.Show(dialog, this);
                if (choice != DialogResult.OK && choice != DialogResult.Yes) return;
                if (choice == DialogResult.Yes)
                {
                    EmulatorFolders.CheckDelete(folder, store.Data, emulator);
                    if (!LauncherDialogs.Confirm(this,
                        "Permanently delete this emulator directory and all its contents?" + Environment.NewLine +
                        Environment.NewLine + folder, "Delete emulator files")) return;
                    string staged = folder + ".removing-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                    Directory.Move(folder, staged);
                    try { store.Save(settings => settings.RemoveEmulator(emulator)); }
                    catch
                    {
                        try { Directory.Move(staged, folder); }
                        catch (Exception error) { throw new IOException("Could not restore the emulator folder from " + staged, error); }
                        throw;
                    }
                    try
                    {
                        Directory.Delete(staged, true);
                        filesDeleted = true;
                    }
                    catch (Exception error)
                    {
                        ShowError(new IOException("Emulator removed from list. Could not finish deleting files at " + staged +
                            Environment.NewLine + error.Message, error));
                    }
                }
                else
                    store.Save(settings => settings.RemoveEmulator(emulator));
            }
            BindEmulators(null, null);
            RefreshPrograms();
            status.Text = filesDeleted ? "Emulator files deleted" : "Emulator removed from list";
        }

        private void ShowInstall()
        {
            using (var dialog = new InstallForm(store))
            {
                if (LauncherDialogs.Show(dialog, this) != DialogResult.OK) return;
                BindEmulators(dialog.EmulatorId, null);
                RefreshPrograms();
                status.Text = "Release installed";
            }
        }

        private void ShowAbout()
        {
            var assembly = typeof(MainForm).Assembly;
            using (var artwork = assembly.GetManifestResourceStream("CommanderX16.Retrodores.png"))
            using (var image = Image.FromStream(artwork))
            using (var headingFont = new Font(Font.FontFamily, 17, FontStyle.Bold))
            using (var sectionFont = new Font(Font, FontStyle.Bold))
            using (var dialog = new Form
            {
                Text = "About " + Program.Title, Font = Font,
                AutoScaleMode = AutoScaleMode.Dpi, ClientSize = new Size(560, 345),
                StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false
            })
            {
                var body = Table();
                body.Padding = new Padding(16);
                var identity = Table();
                identity.ColumnCount = 2;
                identity.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                identity.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                identity.Margin = new Padding(0, 0, 0, 12);
                identity.Controls.Add(new PictureBox
                {
                    Image = image, SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(72, 72),
                    Margin = new Padding(0, 0, 14, 0), AccessibleName = "Retrodores avatar"
                }, 0, 0);
                var name = Table();
                AddRow(name, new Label { Text = Program.Title, Font = headingFont, AutoSize = true, ForeColor = Navy });
                AddRow(name, new Label { Text = "Version " + assembly.GetName().Version.ToString(3), AutoSize = true });
                identity.Controls.Add(name, 1, 0);
                AddRow(body, identity);
                AddRow(body, new Label
                {
                    Text = "A Windows launcher for Commander X16 programs.",
                    AutoSize = true, Margin = new Padding(0, 0, 0, 12)
                });
                AddRow(body, new Label { Text = "Project and credits", Font = sectionFont, AutoSize = true });
                AddRow(body, AboutLink("Source and releases", "https://github.com/retrodores/x16-for-all-time"));
                AddRow(body, AboutLink("Report a launcher issue", IssueUrl("https://github.com/retrodores/x16-for-all-time",
                    "Launcher issue", null)));
                AddRow(body, AboutLink("Retrodores on GitHub", "https://github.com/retrodores"));
                AddRow(body, AboutLink("Commander X16 emulator", "https://github.com/X16Community/x16-emulator"));
                var close = Button("Close");
                close.DialogResult = DialogResult.OK;
                AddScrollableContent(dialog, body, Flow(close));
                dialog.AcceptButton = dialog.CancelButton = close;
                dialog.ActiveControl = close;
                LauncherDialogs.Show(dialog, this);
            }
        }

        private static string IssueUrl(string repo, string title, string programId)
        {
            var version = typeof(MainForm).Assembly.GetName().Version.ToString(3);
            var body = "F.A.T. version: " + version + Environment.NewLine +
                (programId == null ? "" : "Catalog ID: " + programId + Environment.NewLine) +
                Environment.NewLine + "Describe what happened and what you expected:";
            return repo + "/issues/new?title=" + Uri.EscapeDataString(title) + "&body=" + Uri.EscapeDataString(body);
        }

        private static void OpenReport(string repo, string title, string programId) =>
            Process.Start(new ProcessStartInfo(IssueUrl(repo, title, programId)) { UseShellExecute = true });

        private LinkLabel AboutLink(string text, string url)
        {
            var link = new LinkLabel { Text = text, AutoSize = true };
            link.LinkClicked += (sender, args) => Attempt(() => System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }));
            return link;
        }

        private void Attempt(Action action)
        {
            try { action(); }
            catch (Exception error) { ShowError(error); }
        }

        private void ShowError(Exception error) =>
            LauncherDialogs.Message(this, error.Message, Text);

        internal static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        private static void SelectById(ComboBox box, int index)
        {
            box.SelectedIndex = box.Items.Count == 0 ? -1 : Math.Max(0, index);
        }

        internal static ComboBox Combo(params string[] items)
        {
            var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, MinimumSize = new Size(135, 0), Dock = DockStyle.Fill };
            box.Items.AddRange(items);
            return box;
        }

        internal static TextBox Entry() => new TextBox { Dock = DockStyle.Fill };
        internal static Button Button(string title, Action onClick = null)
        {
            var button = new Button { Text = title, AutoSize = true, Padding = new Padding(4, 1, 4, 1) };
            if (onClick != null) button.Click += (sender, args) => onClick();
            return button;
        }

        internal static TableLayoutPanel Table() => new TableLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top,
            ColumnCount = 1, Margin = Padding.Empty
        };

        internal static void AddRow(TableLayoutPanel table, Control child, SizeType type = SizeType.AutoSize, float height = 0)
        {
            if (table.ColumnCount == 1 && table.ColumnStyles.Count == 0)
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            int row = table.RowCount++;
            table.RowStyles.Add(new RowStyle(type, height));
            table.Controls.Add(child, 0, row);
        }

        internal static void AddScrollableContent(Control host, TableLayoutPanel body, FlowLayoutPanel footer = null)
        {
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            scroll.Controls.Add(body);
            if (footer == null)
            {
                host.Controls.Add(scroll);
                return;
            }

            var layout = Table();
            layout.AutoSize = false;
            layout.Dock = DockStyle.Fill;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            host.Controls.Add(layout);
            footer.Padding = new Padding(12, 4, 12, 8);
            AddRow(layout, scroll, SizeType.Percent, 100);
            AddRow(layout, footer);
        }

        internal static void WrapText(Label label, Control container)
        {
            void UpdateWidth()
            {
                int width = Math.Max(1, container.ClientSize.Width - container.Padding.Horizontal - label.Margin.Horizontal);
                if (label.MaximumSize.Width != width) label.MaximumSize = new Size(width, 0);
            }
            container.ClientSizeChanged += (sender, args) => UpdateWidth();
            container.HandleCreated += (sender, args) => UpdateWidth();
        }

        internal static TableLayoutPanel Pair(string label, Control field, params Control[] actions)
        {
            var table = Table();
            table.ColumnCount = 2 + actions.Length;
            table.RowCount = 1;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 148));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
            // Native single-line editors have their own font-based height.
            // Stretch them across their cell without forcing the cell's height onto them.
            field.Dock = DockStyle.None;
            field.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            if (field is ComboBox || field is TextBox)
            {
                void UpdateHeight()
                {
                    int height = field is ComboBox combo ? combo.PreferredHeight : ((TextBox)field).PreferredHeight;
                    var size = new Size(field.MinimumSize.Width, height);
                    if (field.MinimumSize != size) field.MinimumSize = size;
                    var rowMinimum = new Size(0, height + field.Margin.Vertical);
                    if (table.MinimumSize != rowMinimum) table.MinimumSize = rowMinimum;
                }
                field.FontChanged += (sender, args) => UpdateHeight();
                field.HandleCreated += (sender, args) => UpdateHeight();
                field.DpiChangedAfterParent += (sender, args) => UpdateHeight();
                UpdateHeight();
            }
            table.Controls.Add(field, 1, 0);
            for (int i = 0; i < actions.Length; i++)
            {
                table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                table.Controls.Add(actions[i], i + 2, 0);
            }
            return table;
        }

        internal static FlowLayoutPanel Flow(params Control[] children)
        {
            var panel = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Fill, Margin = Padding.Empty };
            panel.Controls.AddRange(children);
            return panel;
        }
    }
}
