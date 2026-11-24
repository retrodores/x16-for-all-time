using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace CommanderX16
{
    internal sealed partial class MainForm : Form
    {
        private static readonly Color Navy = Color.FromArgb(20, 36, 57);
        private static readonly Color Teal = Color.FromArgb(24, 113, 110);
        private readonly SettingsStore store;
        private readonly ComboBox emulators = Combo(), profiles = Combo();
        private readonly TextBox preview = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical };
        private readonly Button run = Button("Run"), copy = Button("Copy PowerShell");
        private readonly Label launchStatus = new Label { AutoSize = true, ForeColor = Navy };
        private readonly Label status = new Label { AutoSize = true, ForeColor = Navy };
        private readonly Button editBuild = Button("Edit build…"), removeBuild = Button("Remove from list…");
        private readonly Button newProfile = Button("New profile…"), editProfile = Button("Edit…"),
            duplicateProfile = Button("Duplicate…"), deleteProfile = Button("Delete…"), defaultProfile = Button("Set as default");
        private readonly Label profileDefault = new Label { AutoSize = true };
        private readonly TextBox emulatorPath = new TextBox { ReadOnly = true, Dock = DockStyle.Fill };
        private bool binding;
        private bool launching;

        public MainForm(SettingsStore store)
        {
            this.store = store;
            Text = "Commander X16 For Right Now";
            Font = new Font("Segoe UI", 10);
            AutoScaleMode = AutoScaleMode.Dpi;
            MinimumSize = new Size(700, 630);
            ClientSize = new Size(880, 770);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(234, 241, 241);

            var layout = Table();
            layout.AutoSize = false;
            layout.Padding = new Padding(20, 14, 20, 10);
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 1;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Controls.Add(layout);

            var title = new Label
            {
                Text = Text, Font = new Font("Segoe UI Semibold", 20),
                AutoSize = true, ForeColor = Navy, Margin = new Padding(0, 0, 0, 10)
            };
            AddRow(layout, title);
            var pages = new TabControl { Dock = DockStyle.Fill };
            var start = new TabPage("Start") { Padding = new Padding(12) };
            var programs = new TabPage("PRGs") { Padding = new Padding(12) };
            var emulatorPage = new TabPage("Emulators") { Padding = new Padding(12) };
            pages.TabPages.AddRange(new[] { start, programs, emulatorPage });
            AddRow(layout, pages, SizeType.Percent, 100);
            BuildProgramPages(start, programs, pages);

            var emulatorBody = Table();
            emulatorBody.AutoSize = false;
            emulatorBody.Dock = DockStyle.Fill;
            emulatorPage.Controls.Add(emulatorBody);
            var selections = Table();
            var buildActions = Flow(Button("Install or update…", ShowInstall), Button("Register existing…", () => EditEmulator(null)));
            buildActions.Dock = DockStyle.None;
            buildActions.Anchor = AnchorStyles.Right;
            buildActions.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            AddRow(selections, buildActions);
            AddRow(selections, Pair("Emulator", emulators, editBuild, removeBuild));
            AddRow(selections, Pair("Executable", emulatorPath));
            AddRow(emulatorBody, selections);

            var profileSection = Table();
            profileSection.Margin = new Padding(0, 18, 0, 6);
            AddRow(profileSection, Pair("Profiles for this build", profiles));
            AddRow(profileSection, Flow(newProfile, editProfile, duplicateProfile, deleteProfile, defaultProfile));
            AddRow(profileSection, profileDefault);
            AddRow(emulatorBody, profileSection);
            AddRow(emulatorBody, status);

            editBuild.Click += (sender, args) => EditEmulator(SelectedEmulator);
            removeBuild.Click += (sender, args) => Attempt(RemoveEmulator);
            emulators.SelectedIndexChanged += (sender, args) => { if (!binding) BindProfiles(null); };
            profiles.SelectedIndexChanged += (sender, args) => { if (!binding) UpdateProfileActions(); };
            newProfile.Click += (sender, args) => Attempt(() => SaveProfile(null, false));
            editProfile.Click += (sender, args) => Attempt(() => SaveProfile(SelectedProfile, false));
            duplicateProfile.Click += (sender, args) => Attempt(() => SaveProfile(SelectedProfile, true));
            deleteProfile.Click += (sender, args) => Attempt(RemoveProfile);
            defaultProfile.Click += (sender, args) => Attempt(SetDefaultProfile);
            FormClosing += (sender, args) => { if (launching) args.Cancel = true; };
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
            BindProfiles(profileId);
        }

        private void BindProfiles(string profileId)
        {
            binding = true;
            profiles.Items.Clear();
            var available = store.Data.Profiles.Where(profile =>
                SelectedEmulator != null && profile.EmulatorId == SelectedEmulator.Id).ToArray();
            profiles.Items.AddRange(available.Cast<object>().ToArray());
            SelectById(profiles, Array.FindIndex(available, profile => profile.Id == profileId));
            binding = false;
            UpdateProfileActions();
            RefreshPreview();
        }

        private void UpdateProfileActions()
        {
            var emulator = SelectedEmulator;
            emulatorPath.Text = emulator?.Exe ?? "";
            editBuild.Enabled = removeBuild.Enabled = newProfile.Enabled = emulator != null;
            profiles.Enabled = editProfile.Enabled = duplicateProfile.Enabled = deleteProfile.Enabled = SelectedProfile != null;
            var currentDefault = store.Data.DefaultProfile(emulator);
            defaultProfile.Text = currentDefault?.Id == SelectedProfile?.Id && currentDefault != null
                ? "Clear default" : "Set as default";
            defaultProfile.Enabled = SelectedProfile != null;
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
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
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
            if (MessageBox.Show(this, "Delete the profile " + profile.Name + "?", "Delete profile",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
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
                build.DefaultProfileId = build.DefaultProfileId == profileId ? null : profileId;
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
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    var other = store.Data.Emulators.FirstOrDefault(e => e.Id != existing?.Id && Same(e.Exe, dialog.Executable));
                    if (other != null)
                    {
                        BindEmulators(other.Id, null);
                        throw new InvalidOperationException("This executable is already registered as " + other.Name + ". Use Edit build to update it.");
                    }
                    string id = existing?.Id ?? Guid.NewGuid().ToString("N");
                    string profileId = SelectedProfile?.Id;
                    store.Save(data =>
                    {
                        var emulator = data.Emulators.FirstOrDefault(e => e.Id == id);
                        if (emulator == null)
                        {
                            emulator = new Emulator { Id = id };
                            data.Emulators.Add(emulator);
                        }
                        emulator.Name = dialog.BuildName;
                        emulator.Exe = dialog.Executable;
                        emulator.UpstreamOptions = dialog.StandardOptions;
                        emulator.Extra = dialog.Extra;
                        emulator.DefaultRom = dialog.DefaultRom;
                    });
                    BindEmulators(id, profileId);
                    RefreshPrograms();
                    status.Text = existing == null ? "Emulator registered" : "Emulator updated";
                }
            });
        }

        private void RemoveEmulator()
        {
            var emulator = SelectedEmulator;
            if (emulator == null) return;
            var affected = store.Data.Programs.Where(program => program.EmulatorId == emulator.Id).ToArray();
            string message = "Remove " + emulator.Name + " from the emulator list? Its files will stay on disk.";
            if (affected.Length != 0)
            {
                string names = string.Join(Environment.NewLine, affected.Take(10).Select(program => "• " + program.Name));
                if (affected.Length > 10)
                    names += Environment.NewLine + "…and " + (affected.Length - 10) + " more.";
                message += Environment.NewLine + Environment.NewLine + "These entries will need a new emulator:" +
                    Environment.NewLine + names;
            }
            if (store.Data.StartEmulatorId == emulator.Id)
                message += Environment.NewLine + Environment.NewLine + "Choose a new emulator in Start defaults.";
            if (MessageBox.Show(this, message, "Remove emulator", MessageBoxButtons.YesNo,
                MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            store.Save(settings => settings.RemoveEmulator(emulator));
            BindEmulators(null, null);
            RefreshPrograms();
            status.Text = "Emulator removed from list";
        }

        private void ShowInstall()
        {
            using (var dialog = new InstallForm(store))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                BindEmulators(dialog.EmulatorId, null);
                RefreshPrograms();
                status.Text = "Release installed";
            }
        }

        private void Attempt(Action action)
        {
            try { action(); }
            catch (Exception error) { ShowError(error); }
        }

        private void ShowError(Exception error) =>
            MessageBox.Show(this, error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);

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
            int row = table.RowCount++;
            table.RowStyles.Add(new RowStyle(type, height));
            table.Controls.Add(child, 0, row);
        }

        internal static TableLayoutPanel Pair(string label, Control field, params Control[] actions)
        {
            var table = Table();
            table.ColumnCount = 2 + actions.Length;
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 148));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
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
            var panel = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Dock = DockStyle.Fill, Margin = Padding.Empty };
            panel.Controls.AddRange(children);
            return panel;
        }
    }
}
