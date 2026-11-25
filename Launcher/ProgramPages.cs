using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CommanderX16
{
    internal sealed partial class MainForm
    {
        private readonly TextBox search = Entry();
        private readonly ListBox startMenu = new ListBox
        {
            Dock = DockStyle.Fill, IntegralHeight = false, SelectionMode = SelectionMode.MultiExtended
        };
        private bool updatingStartSelection;
        private ProgramEntry SelectedProgram => startMenu.SelectedItems.Count == 1
            ? (ProgramEntry)startMenu.SelectedItem : null;
        private readonly Button startSettings = Button("Launch settings…");
        private readonly ProgressBar launchActivity = new ProgressBar
        {
            Size = new Size(86, 16), Margin = new Padding(4, 7, 4, 0), Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 25, Visible = false, AccessibleName = "Emulator launch activity"
        };
        private int launchAttempt;
        private readonly CheckBox saveGif = new CheckBox { Text = "Save GIF", AutoSize = true };
        private readonly TextBox gifFile = Entry();
        private string gifEntryId;
        private readonly Button browseGif = Button("Browse…");
        private readonly Button startDefaults = Button("Start defaults…");
        private readonly Label launchConfiguration = new Label { AutoSize = true };
        private readonly Button duplicate = Button("Duplicate entry…"),
            removeProgram = Button("Delete programs…");

        private void BuildStartPage(TabPage start, TabControl tabs)
        {
            var startBody = PageBody(start);
            var headline = new Label
            {
                Text = "Choose a program", Font = new Font("Segoe UI Semibold", 17),
                ForeColor = Navy, AutoSize = true
            };
            AddRow(startBody, headline);
            AddRow(startBody, Pair("Find a PRG", search, Button("Add PRG…", () =>
                EditLaunchSettings(new ProgramEntry(), "Add PRG", create: true)), startDefaults));
            startMenu.BackColor = Navy;
            startMenu.ForeColor = Color.White;
            startMenu.Font = new Font("Segoe UI", 13);
            AddRow(startBody, startMenu, SizeType.Percent, 100);
            AddRow(startBody, Flow(run, duplicate, removeProgram));
            gifFile.AccessibleName = "GIF output file";
            AddRow(startBody, Pair("", saveGif));
            browseGif.AccessibleName = "Browse for GIF output file";
            AddRow(startBody, Pair("GIF destination", gifFile, browseGif));
            AddRow(startBody, launchConfiguration);
            AddRow(startBody, preview, SizeType.Absolute, 83);
            run.BackColor = Teal;
            run.ForeColor = Color.White;
            run.FlatStyle = FlatStyle.Flat;
            AddRow(startBody, Flow(startSettings, copy, launchActivity, launchStatus));

            saveGif.CheckedChanged += (sender, args) => RefreshPreview();
            gifFile.TextChanged += (sender, args) => RefreshPreview();
            gifFile.Validated += (sender, args) => Attempt(SaveStartGifPath);
            FormClosing += (sender, args) => Attempt(SaveStartGifPath);
            browseGif.Click += (sender, args) => Attempt(PickStartGif);
            search.TextChanged += (sender, args) => RefreshPrograms();
            startMenu.SelectedIndexChanged += (sender, args) =>
            {
                if (updatingStartSelection) return;
                LoadStartGifPath();
                RefreshPreview();
            };
            BuildStartContextMenu();
            startMenu.MouseDoubleClick += async (sender, args) =>
            {
                if (args.Button != MouseButtons.Left || (ModifierKeys & (Keys.Control | Keys.Shift)) != Keys.None) return;
                int index = startMenu.IndexFromPoint(args.Location);
                if (index >= 0 && startMenu.GetSelected(index)) await RunSelected(SelectedProgram);
            };
            run.Click += async (sender, args) => await RunSelected(SelectedProgram);
            startDefaults.Click += (sender, args) => Attempt(EditStartDefaults);
            startSettings.Click += (sender, args) => Attempt(() => EditLaunchSettings(SelectedProgram));
            copy.Click += (sender, args) => Attempt(CopyPreview);
            removeProgram.Click += (sender, args) => Attempt(RemoveProgram);
            duplicate.Click += (sender, args) => Attempt(DuplicateProgram);
            tabs.SelectedIndexChanged += (sender, args) =>
                AcceptButton = tabs.SelectedTab == start ? run : null;
            AcceptButton = run;
            RefreshPrograms();
        }

        private void BuildStartContextMenu()
        {
            var contextMenu = new ContextMenuStrip();
            var actions = new[] { run, startSettings, copy, duplicate, removeProgram };
            var items = actions.Select(button =>
                new ToolStripMenuItem(button.Text, null, (sender, args) => button.PerformClick())).ToArray();
            contextMenu.Items.AddRange(items);
            contextMenu.Opening += (sender, args) =>
            {
                args.Cancel = startMenu.SelectedItem == null;
                for (int i = 0; i < actions.Length; i++)
                {
                    items[i].Text = actions[i].Text;
                    items[i].Enabled = actions[i].Enabled;
                }
            };
            startMenu.ContextMenuStrip = contextMenu;
            startMenu.MouseDown += (sender, args) =>
            {
                if (args.Button != MouseButtons.Right) return;
                int index = startMenu.IndexFromPoint(args.Location);
                if (index >= 0 && startMenu.GetSelected(index)) return;
                startMenu.ClearSelected();
                if (index >= 0) startMenu.SetSelected(index, true);
            };
            startMenu.Disposed += (sender, args) => contextMenu.Dispose();
        }

        private static TableLayoutPanel PageBody(TabPage page)
        {
            var body = Table();
            body.AutoSize = false;
            body.Dock = DockStyle.Fill;
            page.Controls.Add(body);
            return body;
        }

        private void RefreshPrograms(string select = null)
        {
            var selected = new HashSet<string>(select == null
                ? startMenu.SelectedItems.Cast<ProgramEntry>().Select(entry => entry.Id)
                : new[] { select });
            string filter = search.Text.Trim();
            var entries = store.Data.Programs.Where(p =>
                p.Name.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                p.Path.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(p => p.Path, StringComparer.OrdinalIgnoreCase).ToArray();
            updatingStartSelection = true;
            startMenu.BeginUpdate();
            try
            {
                startMenu.Items.Clear();
                startMenu.Items.AddRange(entries);
                for (int i = 0; i < entries.Length; i++)
                    if (selected.Contains(entries[i].Id)) startMenu.SetSelected(i, true);
                if (entries.Length > 0 && startMenu.SelectedItems.Count == 0)
                    startMenu.SetSelected(0, true);
            }
            finally
            {
                startMenu.EndUpdate();
                updatingStartSelection = false;
            }
            LoadStartGifPath();
            RefreshPreview();
        }

        private void RemoveProgram()
        {
            var selected = startMenu.SelectedItems.Cast<ProgramEntry>().ToArray();
            if (selected.Length == 0) return;
            var ids = new HashSet<string>(selected.Select(entry => entry.Id));
            var paths = new HashSet<string>(selected.Select(entry => Path.GetFullPath(entry.Path)),
                StringComparer.OrdinalIgnoreCase);
            if (paths.Any(path => !Launch.IsProgramFile(path)))
                throw new IOException("Only PRG and BAS program files can be deleted from Start.");
            var shared = store.Data.Programs.Where(entry => !ids.Contains(entry.Id) &&
                paths.Contains(Path.GetFullPath(entry.Path))).Select(entry => entry.Name).ToArray();
            if (shared.Length != 0)
                throw new IOException("These entries share the selected files. Select them too before deleting."
                    + Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, shared));

            var names = selected.Length == 1 ? selected[0].Name : selected.Length + " selected entries";
            var message = "Remove " + names + " from Start and permanently delete the following program files?"
                + Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, paths.OrderBy(path => path))
                + Environment.NewLine + Environment.NewLine + "Companion files and folders will stay on disk.";
            if (!LauncherDialogs.Confirm(this, message, "Delete programs")) return;

            DeleteStartPrograms(ids, paths);
        }

        private void DeleteStartPrograms(HashSet<string> ids, IEnumerable<string> paths)
        {
            var moved = new Dictionary<string, string>();
            try
            {
                foreach (var path in paths)
                {
                    var staged = Path.Combine(Path.GetDirectoryName(path), ".x16-delete-" + Guid.NewGuid().ToString("N"));
                    try
                    {
                        File.Move(path, staged);
                        moved.Add(path, staged);
                    }
                    catch (FileNotFoundException) { }
                    catch (DirectoryNotFoundException) { }
                }

                store.Save(data => data.Programs.RemoveAll(entry => ids.Contains(entry.Id)));
            }
            catch (Exception error)
            {
                var failures = new List<string>();
                foreach (var file in moved)
                {
                    try { File.Move(file.Value, file.Key); }
                    catch (Exception restoreError) { failures.Add(file.Value + " → " + file.Key + ": " + restoreError.Message); }
                }
                if (failures.Count == 0) throw;
                throw new IOException(error.Message + Environment.NewLine +
                    "These files could not be restored automatically." + Environment.NewLine + string.Join(Environment.NewLine, failures), error);
            }

            var leftovers = new List<string>();
            foreach (var staged in moved.Values)
            {
                try { File.Delete(staged); }
                catch (IOException) { leftovers.Add(staged); }
                catch (UnauthorizedAccessException) { leftovers.Add(staged); }
            }
            RefreshPrograms();
            if (leftovers.Count != 0)
                throw new IOException("The entries were removed, but these staged files could not be deleted."
                    + Environment.NewLine + string.Join(Environment.NewLine, leftovers));
            launchStatus.Text = ids.Count == 1 ? "Program removed" : ids.Count + " programs removed";
        }

        private void DuplicateProgram()
        {
            var program = SelectedProgram;
            var entry = Json.Read<ProgramEntry>(Json.Write(program));
            entry.Id = Guid.NewGuid().ToString("N");
            entry.Name = program.Name + " (copy)";
            entry.CatalogId = null;
            entry.SdKitSource = null;
            entry.StartGifPath = null;
            EditLaunchSettings(entry, "Duplicate PRG entry", create: true);
        }

        private void EditLaunchSettings(ProgramEntry program, string title = "Launch settings", bool create = false)
        {
            if (program == null) return;
            var emulator = store.Data.LaunchEmulator(program);
            var initial = store.Data.UsesStartDefaults(program) ? store.Data.StartOptions : program.Options;
            if (emulator == null && store.Data.UsesStartDefaults(program) && store.Data.Emulators.Count == 1)
                emulator = store.Data.Emulators[0];
            using (var dialog = new LaunchSettingsForm(store.Data, title, emulator, initial, program))
            {
                if (LauncherDialogs.Show(dialog, this) != DialogResult.OK) return;
                store.Save(data =>
                {
                    var entry = create ? program : data.Programs.Single(item => item.Id == program.Id);
                    entry.Name = dialog.EntryName;
                    if (!Same(entry.Path, dialog.ProgramPath))
                    {
                        entry.CatalogId = null;
                        entry.SdKitSource = null;
                        entry.SdKitRoot = null;
                    }
                    entry.Path = dialog.ProgramPath;
                    entry.EmulatorId = dialog.UseStartDefaults ? null : dialog.Emulator?.Id;
                    entry.Options = dialog.UseStartDefaults ? null : dialog.Options;
                    entry.RequiresEmulatorSelection = !dialog.UseStartDefaults && dialog.Emulator == null;
                    entry.ProfileId = null;
                    if (create) data.Programs.Add(entry);
                });
                dialog.KeepImportedProgram();
                if (create) search.Clear();
                RefreshPrograms(program.Id);
            }
        }

        private void EditStartDefaults()
        {
            var current = store.Data.FindEmulator(store.Data.StartEmulatorId);
            var selected = current ?? (store.Data.Emulators.Count == 1 ? store.Data.Emulators[0] : null);
            using (var dialog = new LaunchSettingsForm(store.Data, "Start defaults", selected,
                current == null ? null : store.Data.StartOptions,
                clearAction: current == null ? null : "Clear Start defaults"))
            {
                var result = LauncherDialogs.Show(dialog, this);
                if (result != DialogResult.OK && result != DialogResult.Abort) return;
                store.Save(settings =>
                {
                    settings.StartEmulatorId = result == DialogResult.OK ? dialog.Emulator.Id : null;
                    settings.StartOptions = result == DialogResult.OK ? dialog.Options : null;
                });
                RefreshPrograms();
            }
        }

        private void LoadStartGifPath()
        {
            var id = SelectedProgram?.Id;
            if (id == gifEntryId) return;
            Attempt(SaveStartGifPath);
            gifEntryId = id;
            gifFile.Text = store.Data.Programs.FirstOrDefault(entry => entry.Id == id)?.StartGifPath ?? "";
        }

        private void SaveStartGifPath()
        {
            if (gifEntryId == null) return;
            var entry = store.Data.Programs.FirstOrDefault(program => program.Id == gifEntryId);
            var path = string.IsNullOrWhiteSpace(gifFile.Text) ? null : gifFile.Text.Trim();
            if (entry == null || entry.StartGifPath == path) return;
            store.Save(data => data.Programs.Single(program => program.Id == gifEntryId).StartGifPath = path);
        }

        private static string DefaultStartGifName(ProgramEntry program) =>
            "capture-" + program.Id + ".gif";

        private void PickStartGif()
        {
            var program = SelectedProgram;
            if (program == null) return;

            using (var dialog = new SaveFileDialog
            {
                Title = "Save GIF recording",
                Filter = "GIF recording (*.gif;*.gi)|*.gif;*.gi",
                DefaultExt = "gif",
                FileName = DefaultStartGifName(program),
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                OverwritePrompt = false
            })
            {
                var current = gifFile.Text.Trim();
                if (current.Length != 0)
                {
                    try
                    {
                        var path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(program.Path), current));
                        var directory = Path.GetDirectoryName(path);
                        if (Directory.Exists(directory)) dialog.InitialDirectory = directory;
                        var filename = Path.GetFileName(path);
                        if (filename.Length != 0 && filename.IndexOfAny(Path.GetInvalidFileNameChars()) < 0)
                            dialog.FileName = filename;
                    }
                    catch (Exception error) when (error is ArgumentException || error is NotSupportedException ||
                        error is PathTooLongException) { }
                }
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    gifFile.Text = dialog.FileName;
                    SaveStartGifPath();
                }
            }
        }

        private Request Prepare(ProgramEntry program, Emulator emulator, bool validate)
        {
            var options = store.Data.LaunchOptions(program);
            if (saveGif.Checked)
            {
                var output = program.Id == gifEntryId ? gifFile.Text : program.StartGifPath;
                options = Launch.WithGifOverride(options, emulator,
                    string.IsNullOrWhiteSpace(output) ? DefaultStartGifName(program) : output);
            }
            return Launch.Prepare(emulator, options, program.Path, validate, program.SdKitRoot);
        }

        internal static string CommandPreview(Request request) =>
            Launch.Quote(request.Exe) + " " + request.Arguments + Environment.NewLine +
                "Working directory: " + request.WorkingDirectory;

        private void RefreshPreview()
        {
            var program = SelectedProgram;
            var emulator = program == null ? null : store.Data.LaunchEmulator(program);
            UpdateCatalogSelection();
            UpdateKitSelection();
            run.Enabled = false;
            var programType = Launch.IsBasicProgram(program?.Path) ? "BAS" : "PRG";
            run.Text = (program != null && store.Data.LaunchOptions(program).AutoRun ? "Run " : "Load ") + programType;
            saveGif.Enabled = !launching && program != null;
            gifFile.Enabled = browseGif.Enabled = saveGif.Enabled && saveGif.Checked;
            startDefaults.Enabled = !launching && store.Data.Emulators.Count != 0;
            launchConfiguration.Text = emulator == null ? "Command preview" :
                (store.Data.UsesStartDefaults(program) ? "Start defaults" : "PRG settings") + " · " + emulator.Name;
            startSettings.Enabled = duplicate.Enabled = !launching && program != null;
            removeProgram.Enabled = !launching && startMenu.SelectedItems.Count > 0;
            copy.Enabled = false;
            if (startMenu.SelectedItems.Count > 1) preview.Text = startMenu.SelectedItems.Count + " programs selected.";
            else if (program == null) preview.Text = store.Data.Programs.Count == 0 ? "Add a PRG to get started." : "No matching PRGs.";
            else if (store.Data.Emulators.Count == 0) preview.Text = "Register or install an X16Emu build on the Emulators tab.";
            else if (emulator == null) preview.Text = MissingLaunchSetup(program);
            else
            {
                try
                {
                    var command = Prepare(program, emulator, true);
                    run.Text = (Launch.HasExtraOption(command.Arguments, "-run") ? "Run " : "Load ") + programType;
                    run.Enabled = copy.Enabled = !launching;
                    preview.Text = CommandPreview(command);
                }
                catch (Exception error) { preview.Text = error.Message; }
            }
        }

        private void CopyPreview()
        {
            var program = SelectedProgram;
            Request request = Prepare(program, store.Data.LaunchEmulator(program), false);
            Clipboard.SetText("Start-Process -FilePath " + Launch.PowerShellLiteral(request.Exe) +
                " -WorkingDirectory " + Launch.PowerShellLiteral(request.WorkingDirectory) +
                " -ArgumentList " + Launch.PowerShellLiteral(request.Arguments));
            launchStatus.Text = "Copied";
        }

        private string MissingLaunchSetup(ProgramEntry program)
        {
            if (store.Data.Emulators.Count == 0)
                return "Register or install an X16Emu build on the Emulators tab.";
            return store.Data.UsesStartDefaults(program)
                ? "Set Start defaults or choose an emulator in Launch settings."
                : "Choose a replacement emulator in Launch settings.";
        }

        private async Task RunSelected(ProgramEntry program)
        {
            if (launching || program == null || !run.Enabled) return;
            Request request = null;
            try
            {
                var emulator = store.Data.LaunchEmulator(program);
                if (emulator == null) throw new InvalidOperationException(MissingLaunchSetup(program));
                request = Prepare(program, emulator, false);
                Prepare(program, emulator, true);
                launching = true;
                RefreshPreview();
                preview.Text = CommandPreview(request);
                copy.Enabled = false;
                launchStatus.Text = "Starting emulator…";
                launchActivity.Show();
                await StartPreparedProgram(request, message => launchStatus.Text = message);
            }
            catch (Exception error)
            {
                launchActivity.Hide();
                launchStatus.Text = "Launch failed";
                string details = error.Message + (request == null ? "" : Environment.NewLine + Environment.NewLine + CommandPreview(request));
                LauncherDialogs.Message(this, details, Text);
            }
            finally
            {
                launching = false;
                launchActivity.Hide();
                RefreshPreview();
            }
        }

        private async Task StartPreparedProgram(Request request, Action<string> report)
        {
            int attempt = ++launchAttempt;
            string lateError = null;
            await EmulatorProcess.Start(request, new Progress<string>(message =>
            {
                lateError = message;
                if (attempt == launchAttempt && !IsDisposed) report(message);
            }));
            if (lateError == null) report("Emulator launched");
        }
    }
}
