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
        private readonly TextBox destination = new TextBox { ReadOnly = true, Dock = DockStyle.Fill };
        private readonly CheckBox serialOverlay = new CheckBox
        {
            Text = "Install Serial/Network overlay (R49 Win64)", AutoSize = true, Enabled = false
        };
        private bool changingRelease;
        private readonly CheckBox overwrite = new CheckBox { Text = "Overwrite existing installation", AutoSize = true };
        private readonly ProgressBar activity = new ProgressBar
        {
            Dock = DockStyle.Top, Height = 16, Style = ProgressBarStyle.Marquee, Visible = false,
            MarqueeAnimationSpeed = 25, AccessibleName = "Emulator installer activity"
        };
        private readonly Label status = new Label { AutoSize = true };
        private readonly Button install = Button("Install"), refresh = Button("Check for versions");
        private readonly TableLayoutPanel body = Table();
        private bool busy;
        private string overwritePath;
        public string EmulatorId { get; private set; }

        public InstallForm(SettingsStore store)
        {
            this.store = store;
            Text = "Install or update X16Emu";
            Font = new Font("Segoe UI", 10);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(730, 450);
            MinimumSize = new Size(645, 385);
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            body.Padding = new Padding(14);
            var intro = new Label
            {
                Text = "Choose a release. Each version installs into its own folder.",
                AutoSize = true, Margin = new Padding(0, 0, 0, 10)
            };
            AddRow(body, intro);
            WrapText(intro, body);
            AddRow(body, Pair("Windows release", releases, refresh));
            AddRow(body, serialOverlay);
            var browse = Button("Browse…", Browse);
            AddRow(body, Pair("Install parent", parent, browse));
            parent.EnabledChanged += (sender, args) => browse.Enabled = parent.Enabled;
            AddRow(body, Pair("New folder name", folderName));
            AddRow(body, Pair("Destination", destination));
            AddRow(body, overwrite);
            AddRow(body, status);
            WrapText(status, body);
            AddRow(body, activity);
            var close = Button("Close", Close);
            refresh.EnabledChanged += (sender, args) => close.Enabled = refresh.Enabled;
            close.DialogResult = DialogResult.Cancel;
            AddScrollableContent(this, body, Flow(install, close));
            AcceptButton = install;
            CancelButton = close;

            parent.Text = store.Data.InstallParent ?? Path.Combine(SettingsStore.Home, "Emulators");
            refresh.Click += async (sender, args) => await LoadReleases();
            install.Click += async (sender, args) => await Install();
            releases.SelectedIndexChanged += (sender, args) =>
            {
                changingRelease = true;
                overwrite.Checked = false;
                serialOverlay.Checked = false;
                var selected = releases.SelectedItem as Release;
                folderName.Text = selected == null ? "" : DefaultFolder(selected);
                changingRelease = false;
                UpdateDestination();
            };
            serialOverlay.CheckedChanged += (sender, args) =>
            {
                if (changingRelease) return;
                var selected = (Release)releases.SelectedItem;
                string stock = DefaultFolder(selected);
                if (Same(folderName.Text, stock) || Same(folderName.Text, stock + "-serial-network"))
                    folderName.Text = stock + (serialOverlay.Checked ? "-serial-network" : "");
                UpdateDestination();
            };
            parent.TextChanged += (sender, args) => UpdateDestination();
            folderName.TextChanged += (sender, args) => UpdateDestination();
            overwrite.CheckedChanged += (sender, args) => UpdateDestination();
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
            install.Enabled = overwrite.Enabled = false;
            destination.Text = "";
            var selected = releases.SelectedItem as Release;
            serialOverlay.Enabled = !busy && SerialNetworkOverlay.Supports(selected);
            if (busy) return;
            if (selected == null)
            {
                status.Text = "Check for available Windows releases.";
                return;
            }
            try
            {
                string path = Destination(parent.Text.Trim(), folderName.Text);
                destination.Text = path;
                if (!Same(path, overwritePath))
                {
                    overwritePath = path;
                    overwrite.Checked = false;
                }
                if (File.Exists(path))
                {
                    status.Text = "A file already occupies the destination.";
                    return;
                }
                if (Directory.Exists(path))
                {
                    EmulatorFolders.CheckDirectory(path, store.Data, RegisteredAt(path));
                    overwrite.Enabled = true;
                    status.Text = overwrite.Checked
                        ? "The previous installation will be permanently deleted after the replacement is registered."
                        : "Destination already exists. Choose another folder or check Overwrite.";
                    install.Enabled = overwrite.Checked;
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
            activity.Visible = value;
            releases.Enabled = parent.Enabled = folderName.Enabled = refresh.Enabled = !value;
            install.Enabled = overwrite.Enabled = false;
            serialOverlay.Enabled = !value && SerialNetworkOverlay.Supports(releases.SelectedItem as Release);
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
            catch (Exception error) { failure = "Release check failed. " + error.Message; }
            finally
            {
                SetBusy(false);
                if (failure != null) status.Text = failure;
            }
        }

        private Emulator RegisteredAt(string path) => store.Data.Emulators.FirstOrDefault(build =>
            Same(path, EmulatorFolders.Root(build)) ||
            (build.InstallRoot == null && build.Asset != null && EmulatorFolders.Contains(path, build.Exe)));

        private async Task Install()
        {
            string writtenExe = null;
            string backup = null;
            string target = null;
            bool recorded = false;
            try
            {
                var selected = (Release)releases.SelectedItem;
                string parentPath = Path.GetFullPath(parent.Text.Trim());
                string path = Destination(parentPath, folderName.Text);
                target = path;
                var existing = Directory.Exists(path) ? RegisteredAt(path) : null;
                bool replace = overwrite.Checked && Directory.Exists(path);
                if (replace)
                {
                    EmulatorFolders.CheckDelete(path, store.Data, existing);
                    if (!LauncherDialogs.Confirm(this, "Replace this installation? The old folder and its contents will be permanently deleted after the replacement is registered." +
                        Environment.NewLine + Environment.NewLine + path, Text)) return;
                }
                bool withOverlay = serialOverlay.Checked && SerialNetworkOverlay.Supports(selected);
                SetBusy(true);
                status.Text = "Preparing " + selected.Tag + "…";
                var result = await PowerShell.Execute(new Request
                {
                    Action = "Install", Release = selected, Destination = path, Overwrite = replace, SerialOverlay = withOverlay
                }, new Progress<string>(step => { if (busy) status.Text = step; }));
                backup = result.Backup;
                writtenExe = result.Exe;
                if (string.IsNullOrEmpty(writtenExe) || !File.Exists(writtenExe))
                    throw new IOException("The installer did not return a usable emulator executable.");
                string id = existing?.Id ?? Guid.NewGuid().ToString("N");
                store.Save(settings =>
                {
                    settings.InstallParent = parentPath;
                    var emulator = settings.FindEmulator(id);
                    if (emulator == null)
                    {
                        emulator = new Emulator { Id = id, Name = Path.GetFileName(path), UpstreamOptions = true };
                        settings.Emulators.Add(emulator);
                    }
                    emulator.Exe = writtenExe;
                    emulator.Release = selected.Tag;
                    emulator.Asset = selected.Asset;
                    emulator.InstallRoot = path;
                    emulator.SerialOverlayVersion = withOverlay ? SerialNetworkOverlay.Version : null;
                    settings.EnsureHardwareProfiles(emulator);
                });
                recorded = true;
                EmulatorId = id;
                if (backup != null)
                {
                    status.Text = "Removing the previous installation…";
                    try
                    {
                        await Task.Run(() =>
                        {
                            EmulatorFolders.CheckDelete(backup, store.Data, null);
                            Directory.Delete(backup, recursive: true);
                        });
                    }
                    catch (Exception error)
                    {
                        LauncherDialogs.Message(this, "The replacement is installed, but the previous folder could not be fully deleted." +
                            Environment.NewLine + backup + Environment.NewLine + error.Message, Text);
                    }
                }
                DialogResult = DialogResult.OK;
            }
            catch (Exception error)
            {
                string message = error.Message;
                if (backup != null && !recorded)
                {
                    string replacement = target + ".replacement-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                    try
                    {
                        Directory.Move(target, replacement);
                        Directory.Move(backup, target);
                        message += Environment.NewLine + "The previous installation was restored. Replacement files: " + replacement;
                    }
                    catch (Exception restoreError)
                    {
                        message += Environment.NewLine + "Could not restore the previous installation from " +
                            backup + ". " + restoreError.Message;
                    }
                }
                else if (writtenExe != null && !recorded)
                    message += Environment.NewLine + "The files were installed. Register the existing folder at " + Path.GetDirectoryName(writtenExe);
                LauncherDialogs.Message(this, message, Text);
            }
            finally { SetBusy(false); }
            if (DialogResult == DialogResult.OK) Close();
        }
    }
}
