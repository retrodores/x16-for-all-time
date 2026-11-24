using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using static CommanderX16.MainForm;

namespace CommanderX16
{
    internal sealed class InstallForm : Form
    {
        private readonly SettingsStore store;
        private readonly ComboBox releases = Combo();
        private readonly TextBox parent = Entry(), folderName = Entry();
        private readonly Label destination = new Label { AutoSize = true };
        private readonly Label status = new Label { AutoSize = true, MaximumSize = new Size(600, 0) };
        private readonly Button install = Button("Install"), refresh = Button("Check for versions");
        private readonly TableLayoutPanel body = Table();
        private bool busy;
        public string EmulatorId { get; private set; }

        public InstallForm(SettingsStore store)
        {
            this.store = store;
            Text = "Install or update X16Emu";
            Font = new Font("Segoe UI", 10);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(680, 284);
            MinimumSize = new Size(620, 320);
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            body.Padding = new Padding(14);
            Controls.Add(body);
            var intro = new Label
            {
                Text = "Choose a release. Each version installs into its own folder.",
                AutoSize = true, Margin = new Padding(0, 0, 0, 10)
            };
            AddRow(body, intro);
            AddRow(body, Pair("Windows release", releases, refresh));
            AddRow(body, Pair("Install parent", parent, Button("Browse…", Browse)));
            AddRow(body, Pair("New folder name", folderName));
            AddRow(body, Pair("Destination", destination));
            AddRow(body, status);
            var close = Button("Close", Close);
            close.DialogResult = DialogResult.Cancel;
            AddRow(body, Flow(install, close));
            AcceptButton = install;
            CancelButton = close;

            parent.Text = store.Data.InstallParent ?? Path.Combine(SettingsStore.Home, "Emulators");
            refresh.Click += async (sender, args) => await LoadReleases();
            install.Click += async (sender, args) => await Install();
            releases.SelectedIndexChanged += (sender, args) =>
            {
                var selected = releases.SelectedItem as Release;
                folderName.Text = selected == null ? "" : DefaultFolder(selected);
                UpdateDestination();
            };
            parent.TextChanged += (sender, args) => UpdateDestination();
            folderName.TextChanged += (sender, args) => UpdateDestination();
            Shown += async (sender, args) => await LoadReleases();
            FormClosing += (sender, args) => { if (busy) args.Cancel = true; };
            UpdateDestination();
        }

        internal static string DefaultFolder(Release release) =>
            "x16emu-" + release.Tag + (release.Asset.IndexOf("win32", StringComparison.OrdinalIgnoreCase) >= 0 ? "-win32" : "");

        internal static string Destination(string parent, string name)
        {
            if (string.IsNullOrWhiteSpace(parent) || !Path.IsPathRooted(parent) ||
                !string.Equals(Path.GetPathRoot(parent), Path.GetPathRoot(Path.GetFullPath(parent)), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Choose an absolute install parent.");
            if (string.IsNullOrWhiteSpace(name) || name.EndsWith(".") || name.EndsWith(" ") ||
                name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                System.Text.RegularExpressions.Regex.IsMatch(name, @"^(CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³])(?:\.|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                throw new InvalidOperationException("Enter a valid new folder name.");
            return Path.GetFullPath(Path.Combine(parent, name));
        }

        private void Browse()
        {
            using (var dialog = new FolderBrowserDialog { Description = "Choose the parent for emulator versions", SelectedPath = parent.Text })
                if (dialog.ShowDialog(this) == DialogResult.OK) parent.Text = dialog.SelectedPath;
        }

        private void UpdateDestination()
        {
            install.Enabled = false;
            destination.Text = "";
            if (busy) return;
            var selected = releases.SelectedItem as Release;
            if (selected == null)
            {
                status.Text = "Check for available Windows releases.";
                return;
            }
            try
            {
                string path = Destination(parent.Text.Trim(), folderName.Text);
                destination.Text = path;
                if (Directory.Exists(path) || File.Exists(path))
                {
                    status.Text = "Destination already exists. Change the folder name or close this dialog and register the existing folder.";
                    return;
                }
                var registered = store.Data.Emulators.Where(e => Same(e.Release, selected.Tag) && Same(e.Asset, selected.Asset)).ToList();
                status.Text = registered.Any(e => File.Exists(e.Exe))
                    ? "This release is already registered. This destination will make a separate copy."
                    : "Existing builds and saved profiles stay available.";
                install.Enabled = true;
            }
            catch (Exception error) { status.Text = error.Message; }
        }

        private void SetBusy(bool value)
        {
            busy = value;
            body.Enabled = !value;
            if (!value) UpdateDestination();
        }

        private async Task LoadReleases()
        {
            string failure = null;
            var previous = releases.SelectedItem as Release;
            SetBusy(true);
            status.Text = "Checking GitHub for X16Emu releases…";
            try
            {
                var result = await PowerShell.Execute(new Request { Action = "List" });
                var available = result.Releases;
                if (available == null || available.Length == 0)
                    throw new IOException("No supported Windows release ZIPs were returned.");
                releases.Items.Clear();
                releases.Items.AddRange(available.Cast<object>().ToArray());
                int index = previous == null ? -1 : Array.FindIndex(available,
                    r => Same(r.Tag, previous.Tag) && Same(r.Asset, previous.Asset));
                if (index < 0 && Environment.Is64BitOperatingSystem)
                    index = Array.FindIndex(available, r => r.Asset.IndexOf("win64", StringComparison.OrdinalIgnoreCase) >= 0);
                if (index < 0)
                    index = Array.FindIndex(available, r => r.Asset.IndexOf("win32", StringComparison.OrdinalIgnoreCase) >= 0);
                releases.SelectedIndex = Math.Max(0, index);
            }
            catch (Exception error) { failure = error.Message; }
            finally
            {
                SetBusy(false);
                if (failure != null) status.Text = failure;
            }
        }

        private async Task Install()
        {
            string writtenExe = null;
            try
            {
                var selected = (Release)releases.SelectedItem;
                string parentPath = Path.GetFullPath(parent.Text.Trim());
                string path = Destination(parentPath, folderName.Text);
                SetBusy(true);
                status.Text = "Downloading and verifying " + selected.Tag + "…";
                var result = await PowerShell.Execute(new Request
                {
                    Action = "Install", Release = selected, Destination = path
                });
                writtenExe = result.Exe;
                if (string.IsNullOrEmpty(writtenExe) || !File.Exists(writtenExe))
                    throw new IOException("The installer did not return a usable emulator executable.");
                var emulator = new Emulator
                {
                    Name = folderName.Text, Exe = result.Exe, Release = selected.Tag, Asset = selected.Asset,
                    UpstreamOptions = true
                };
                store.Save(settings =>
                {
                    settings.InstallParent = parentPath;
                    settings.Emulators.Add(emulator);
                });
                EmulatorId = emulator.Id;
                DialogResult = DialogResult.OK;
            }
            catch (Exception error)
            {
                string message = error.Message;
                if (writtenExe != null)
                    message += Environment.NewLine + "The files were installed. Register the existing folder at " + Path.GetDirectoryName(writtenExe);
                MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { SetBusy(false); }
            if (DialogResult == DialogResult.OK) Close();
        }
    }
}
