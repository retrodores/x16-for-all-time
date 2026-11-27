using System;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.Automation;

namespace CommanderX16
{
    internal sealed partial class MainForm
    {
        private readonly TextBox search = Entry();
        private readonly ListBox startMenu = new ListBox
        {
            Dock = DockStyle.Fill, IntegralHeight = false, SelectionMode = SelectionMode.MultiExtended,
            AccessibleName = "Start programs"
        };
        private bool updatingStartSelection;
        private Rectangle startDragArea;
        private string startDragCandidate;
        private string startDragId;
        private int lastStartDragScroll;
        private const string StartDragFormat = "X16ForAllTime.StartProgram";
        private ProgramEntry SelectedProgram => startMenu.SelectedItems.Count == 1
            ? (ProgramEntry)startMenu.SelectedItem : null;
        private readonly Button editProgram = Button("Edit program…");
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
        private readonly Button runLocal = Button("Run once…");
        private readonly Label launchConfiguration = new Label { AutoSize = true };
        private readonly Button duplicate = Button("Duplicate entry…"),
            removeProgram = Button("Delete files…");
        private readonly Button browsePrograms = Button("Browse X16 Files");
        private readonly Label selectionStatus = new Label { AutoSize = true, LiveSetting = AutomationLiveSetting.Polite };

        private void BuildStartPage(TabPage start, TabControl tabs, TabPage catalogPage)
        {
            var startBody = PageBody(start);
            var headline = new Label
            {
                Text = "Choose a program", Font = new Font("Segoe UI Semibold", 17),
                ForeColor = Navy, AutoSize = true
            };
            AddRow(startBody, headline);
            AddRow(startBody, Pair("Find a program", search));
            search.AccessibleName = "Find a Start program";
            preview.AccessibleName = "Emulator command preview";
            var toolbar = Table();
            toolbar.ColumnCount = 2;
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            toolbar.Controls.Add(Flow(Button("Add a local program…", () =>
                EditLaunchSettings(new ProgramEntry(), "Add a local program", create: true)), browsePrograms, startDefaults), 0, 0);
            toolbar.Controls.Add(Flow(runLocal), 1, 0);
            AddRow(startBody, toolbar);
            browsePrograms.Click += (sender, args) => tabs.SelectedTab = catalogPage;
            startMenu.BackColor = SystemColors.Window;
            startMenu.ForeColor = SystemColors.WindowText;
            startMenu.Font = new Font("Segoe UI", 13);
            AddRow(startBody, startMenu, SizeType.Percent, 100);
            AddRow(startBody, selectionStatus);
            AddRow(startBody, Flow(run, editProgram, duplicate, removeProgram));
            gifFile.AccessibleName = "GIF output file";
            AddRow(startBody, Pair("", saveGif));
            browseGif.AccessibleName = "Browse for GIF output file";
            AddRow(startBody, Pair("GIF destination", gifFile, browseGif));
            AddRow(startBody, launchConfiguration);
            AddRow(startBody, preview, SizeType.Absolute, 83);
            AddRow(startBody, Flow(copy, launchActivity, launchStatus));

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
            EnableStartDragging();
            startMenu.MouseDoubleClick += async (sender, args) =>
            {
                if (args.Button != MouseButtons.Left || (ModifierKeys & (Keys.Control | Keys.Shift)) != Keys.None) return;
                int index = startMenu.IndexFromPoint(args.Location);
                if (index >= 0 && startMenu.GetSelected(index)) await RunSelected(SelectedProgram);
            };
            run.Click += async (sender, args) => await RunSelected(SelectedProgram);
            runLocal.Click += async (sender, args) =>
            {
                var path = LauncherDialogs.PickProgram(this);
                if (path != null) await RunLocalProgram(path);
            };
            startDefaults.Click += (sender, args) => Attempt(EditStartDefaults);
            editProgram.Click += (sender, args) => Attempt(() => EditLaunchSettings(SelectedProgram));
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
            var actions = new[] { run, editProgram, copy, duplicate };
            var info = new ToolStripMenuItem("Program information…", null, (sender, args) => Attempt(ShowProgramInformation));
            var openPrevious = new ToolStripMenuItem("Open previous folder", null, (sender, args) =>
                Attempt(() => Process.Start(new ProcessStartInfo(Path.GetDirectoryName(SelectedProgram.PreviousInstall.Path)) { UseShellExecute = true })));
            var revert = new ToolStripMenuItem("Revert to previous install…", null, (sender, args) => Attempt(RevertInstall));
            var moveUp = new ToolStripMenuItem("Move up", null, (sender, args) => Attempt(() => MoveStartProgram(-1)));
            var moveDown = new ToolStripMenuItem("Move down", null, (sender, args) => Attempt(() => MoveStartProgram(1)));
            var moveTop = new ToolStripMenuItem("Send to top", null, (sender, args) =>
                Attempt(() => MoveStartProgramToEdge(true)));
            var moveBottom = new ToolStripMenuItem("Send to bottom", null, (sender, args) =>
                Attempt(() => MoveStartProgramToEdge(false)));
            var unlink = new ToolStripMenuItem("Remove from Start", null, (sender, args) => Attempt(RemoveStartEntries));
            var delete = new ToolStripMenuItem("Delete programs from disk…", null, (sender, args) => removeProgram.PerformClick());
            var items = actions.Select(button =>
                new ToolStripMenuItem(button == editProgram ? "Edit" : button.Text, null,
                    (sender, args) => button.PerformClick())).ToArray();
            contextMenu.Items.AddRange(items);
            contextMenu.Items.AddRange(new ToolStripItem[] { new ToolStripSeparator(), moveUp, moveDown, moveTop, moveBottom, new ToolStripSeparator(), info, openPrevious, revert, unlink, delete });
            contextMenu.Opening += (sender, args) =>
            {
                args.Cancel = startMenu.SelectedItem == null;
                moveUp.Enabled = !launching && SelectedProgram != null && startMenu.SelectedIndex > 0;
                moveDown.Enabled = !launching && SelectedProgram != null && startMenu.SelectedIndex < startMenu.Items.Count - 1;
                moveTop.Enabled = !launching && SelectedProgram != null && store.Data.Programs[0].Id != SelectedProgram.Id;
                moveBottom.Enabled = !launching && SelectedProgram != null &&
                    store.Data.Programs[store.Data.Programs.Count - 1].Id != SelectedProgram.Id;
                info.Enabled = SelectedProgram != null;
                string previousPath = SelectedProgram?.PreviousInstall?.Path;
                openPrevious.Enabled = previousPath != null && Directory.Exists(Path.GetDirectoryName(previousPath));
                revert.Enabled = !launching && File.Exists(previousPath);
                unlink.Enabled = delete.Enabled = removeProgram.Enabled;
                for (int i = 0; i < actions.Length; i++)
                {
                    if (actions[i] == run) items[i].Text = run.Text;
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

        private void EnableStartDragging()
        {
            startMenu.AllowDrop = true;
            startMenu.MouseDown += (sender, args) =>
            {
                startDragArea = Rectangle.Empty;
                if (args.Button != MouseButtons.Left || ModifierKeys != Keys.None || launching) return;
                int index = startMenu.IndexFromPoint(args.Location);
                if (index < 0 || startMenu.SelectedItems.Count > 1) return;
                startDragCandidate = ((ProgramEntry)startMenu.Items[index]).Id;
                var size = SystemInformation.DragSize;
                startDragArea = new Rectangle(args.X - size.Width / 2, args.Y - size.Height / 2, size.Width, size.Height);
            };
            startMenu.MouseUp += (sender, args) => startDragArea = Rectangle.Empty;
            startMenu.MouseMove += (sender, args) =>
            {
                if (args.Button != MouseButtons.Left || startDragArea.IsEmpty || startDragArea.Contains(args.Location) ||
                    launching) return;
                startDragArea = Rectangle.Empty;
                startDragId = startDragCandidate;
                lastStartDragScroll = Environment.TickCount;
                try { startMenu.DoDragDrop(new DataObject(StartDragFormat, startDragId), DragDropEffects.Move); }
                finally { startDragId = null; }
            };
            startMenu.DragEnter += ShowStartDropTarget;
            startMenu.DragOver += ShowStartDropTarget;
            startMenu.DragDrop += (sender, args) =>
            {
                if (!IsStartDrag(args)) return;
                var point = startMenu.PointToClient(new Point(args.X, args.Y));
                if (!startMenu.ClientRectangle.Contains(point)) return;
                int row = startMenu.IndexFromPoint(point);
                bool after = row < 0;
                if (row < 0) row = startMenu.Items.Count - 1;
                else
                {
                    var bounds = startMenu.GetItemRectangle(row);
                    after = point.Y >= bounds.Top + bounds.Height / 2;
                }
                string anchor = ((ProgramEntry)startMenu.Items[row]).Id;
                Attempt(() => InsertStartProgram(startDragId, anchor, after));
            };
        }

        private bool IsStartDrag(DragEventArgs args) =>
            !launching && startDragId != null && Equals(startDragId, args.Data?.GetData(StartDragFormat));

        private void ShowStartDropTarget(object sender, DragEventArgs args)
        {
            args.Effect = IsStartDrag(args) ? DragDropEffects.Move : DragDropEffects.None;
            if (args.Effect == DragDropEffects.None || unchecked(Environment.TickCount - lastStartDragScroll) < 115) return;
            int y = startMenu.PointToClient(new Point(args.X, args.Y)).Y;
            int first = startMenu.TopIndex;
            int rows = Math.Max(1, startMenu.ClientSize.Height / startMenu.ItemHeight);
            if (y < startMenu.ItemHeight && first > 0)
                startMenu.TopIndex = first - 1;
            else if (y >= startMenu.ClientSize.Height - startMenu.ItemHeight && first + rows < startMenu.Items.Count)
                startMenu.TopIndex = first + 1;
            lastStartDragScroll = Environment.TickCount;
        }

        private void MoveStartProgramToEdge(bool top)
        {
            var selected = SelectedProgram;
            if (selected == null) return;
            var programs = store.Data.Programs;
            InsertStartProgram(selected.Id, programs[top ? 0 : programs.Count - 1].Id, !top);
        }

        private void InsertStartProgram(string id, string anchorId, bool after)
        {
            if (id == anchorId) return;
            var programs = store.Data.Programs;
            int from = programs.FindIndex(entry => entry.Id == id);
            int anchor = programs.FindIndex(entry => entry.Id == anchorId);
            if (from < 0 || anchor < 0) return;
            int to = anchor + (after ? 1 : 0) - (from < anchor ? 1 : 0);
            if (from == to) return;
            store.Save(data =>
            {
                var entry = data.Programs[from];
                data.Programs.RemoveAt(from);
                data.Programs.Insert(to, entry);
            });
            RefreshPrograms(id);
        }

        private void MoveStartProgram(int direction)
        {
            string id = SelectedProgram?.Id;
            int neighborIndex = startMenu.SelectedIndex + direction;
            if (id == null || neighborIndex < 0 || neighborIndex >= startMenu.Items.Count) return;
            string neighborId = ((ProgramEntry)startMenu.Items[neighborIndex]).Id;
            store.Save(data =>
            {
                int from = data.Programs.FindIndex(entry => entry.Id == id);
                int to = data.Programs.FindIndex(entry => entry.Id == neighborId);
                var entryToMove = data.Programs[from];
                data.Programs[from] = data.Programs[to];
                data.Programs[to] = entryToMove;
            });
            RefreshPrograms(id);
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
                p.Path.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
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
            if (select != null && startMenu.SelectedIndex >= 0)
                ShowSelectedStartRow();
        }

        private void ShowSelectedStartRow()
        {
            int index = startMenu.SelectedIndex;
            int first = startMenu.TopIndex;
            int visibleRows = Math.Max(1, startMenu.ClientSize.Height / startMenu.ItemHeight);
            if (index < first)
                startMenu.TopIndex = index;
            else if (index - first >= visibleRows)
                startMenu.TopIndex = index - visibleRows + 1;
        }

        private void RemoveStartEntries()
        {
            var ids = new HashSet<string>(startMenu.SelectedItems.Cast<ProgramEntry>().Select(entry => entry.Id));
            store.Save(data => data.Programs.RemoveAll(entry => ids.Contains(entry.Id)));
            RefreshPrograms();
            launchStatus.Text = "Removed from Start";
        }

        private void ShowProgramInformation()
        {
            var entry = SelectedProgram;
            var catalog = catalogPrograms?.FirstOrDefault(item => entry.CatalogId != null && item.Id == entry.CatalogId);
            var source = entry.Source;
            var url = source?.Url ?? catalog?.PageUrl;
            var info = string.Join(Environment.NewLine, new[]
            {
                entry.Name, source?.Title ?? catalog?.Name, source?.Author ?? catalog?.Author?.Name,
                source?.Version ?? catalog?.Version, entry.SdKitSource, Catalog.IsWebUrl(url) ? url : null, entry.Path
            }.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct());
            if (!Catalog.IsWebUrl(url))
                LauncherDialogs.Message(this, info, "Program information");
            else if (LauncherDialogs.OfferBrowser(this, info, "Program information"))
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }

        private void RevertInstall()
        {
            var selected = SelectedProgram;
            if (!LauncherDialogs.Confirm(this, "Switch " + selected.Name + " to the previous program file?",
                "Revert to previous install")) return;
            store.Save(data =>
            {
                var entry = data.Programs.Single(item => item.Id == selected.Id);
                var previous = entry.PreviousInstall;
                if (!File.Exists(previous?.Path)) throw new IOException("The previous program file is missing.");
                entry.RememberInstallation(previous.Path);
                entry.Path = previous.Path;
                entry.Source = previous.Source;
            });
            RefreshPrograms(selected.Id);
            launchStatus.Text = "Switched to the previous installation";
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

            var files = paths.Count == 1 ? "1 program file" : paths.Count + " program files";
            var entries = selected.Length == 1 ? "1 Start entry" : selected.Length + " Start entries";
            var summary = $"Permanently delete {files} and remove {entries}? Companion files stay on disk.";
            if (!LauncherDialogs.ConfirmProgramDeletion(this, summary, paths.OrderBy(path => path).ToArray())) return;

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
            entry.Name = store.Data.UniqueProgramName(program.Name + " (copy)");
            entry.CatalogId = null;
            entry.SdKitSource = null;
            entry.StartGifPath = null;
            entry.PreviousInstall = null;
            EditLaunchSettings(entry, "Duplicate PRG entry", create: true);
        }

        private void EditLaunchSettings(ProgramEntry program, string title = "Edit program", bool create = false)
        {
            if (program == null) return;
            Profile initial = program.Options;
            var emulator = store.Data.UsesStartDefaults(program)
                ? store.Data.ResolveLaunch(null, out initial)
                : store.Data.FindEmulator(program.EmulatorId);
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
                        entry.Source = null;
                        entry.PreviousInstall = null;
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
            var selected = store.Data.ResolveLaunch(null, out _);
            var configured = store.Data.ConfiguredLaunchEmulator();
            using (var dialog = new LaunchSettingsForm(store.Data, "Start defaults", selected,
                configured == null ? null : store.Data.StartOptions,
                clearAction: store.Data.StartEmulatorId == null ? null : "Clear Start defaults"))
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

        private Request Prepare(ProgramEntry program, Emulator emulator, Profile options, bool validate)
        {
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
            Profile options = null;
            var emulator = program == null ? null : store.Data.ResolveLaunch(program, out options);
            UpdateCatalogSelection();
            UpdateKitSelection();
            run.Enabled = false;
            browsePrograms.Visible = store.Data.Programs.Count == 0;
            int count = startMenu.SelectedItems.Count;
            var selection = count == 1 ? "1 program selected" : count + " programs selected";
            selectionStatus.Visible = count > 0;
            if (selectionStatus.Text != selection)
            {
                selectionStatus.Text = selection;
                selectionStatus.AccessibilityObject.RaiseLiveRegionChanged();
            }
            var programType = Launch.IsBasicProgram(program?.Path) ? "BAS" : "PRG";
            run.Text = (options?.AutoRun == true ? "Run " : "Load ") + programType;
            saveGif.Enabled = !launching && program != null;
            gifFile.Enabled = browseGif.Enabled = saveGif.Enabled && saveGif.Checked;
            startDefaults.Enabled = runLocal.Enabled = !launching && store.Data.Emulators.Count != 0;
            launchConfiguration.Text = emulator == null ? "Command preview" :
                (program.EmulatorId == emulator.Id ? "PRG settings" : "Start defaults") + " · " + emulator.Name;
            editProgram.Enabled = duplicate.Enabled = !launching && program != null;
            removeProgram.Enabled = !launching && startMenu.SelectedItems.Count > 0;
            copy.Enabled = false;
            if (startMenu.SelectedItems.Count > 1) preview.Text = startMenu.SelectedItems.Count + " programs selected.";
            else if (program == null) preview.Text = store.Data.Programs.Count == 0 ? "Browse X16 Files or add a local program to get started." : "No matching programs.";
            else if (store.Data.Emulators.Count == 0) preview.Text = "Choose an emulator to run " + program.Name + ".";
            else if (emulator == null) preview.Text = MissingLaunchSetup();
            else
            {
                try
                {
                    var command = Prepare(program, emulator, options, true);
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
            var emulator = store.Data.ResolveLaunch(program, out var options);
            if (emulator == null) throw new InvalidOperationException(MissingLaunchSetup());
            Request request = Prepare(program, emulator, options, false);
            Clipboard.SetText("Start-Process -FilePath " + Launch.PowerShellLiteral(request.Exe) +
                " -WorkingDirectory " + Launch.PowerShellLiteral(request.WorkingDirectory) +
                " -ArgumentList " + Launch.PowerShellLiteral(request.Arguments));
            launchStatus.Text = "Copied";
        }

        private string MissingLaunchSetup()
        {
            if (store.Data.Emulators.Count == 0)
                return "Register or install an X16Emu build on the Emulators tab.";
            return "No registered emulator executable was found. Check the executable paths on the Emulators tab.";
        }

        private async Task RunSelected(ProgramEntry program)
        {
            if (launching || program == null || !run.Enabled) return;
            Request request = null;
            try
            {
                var emulator = store.Data.ResolveLaunch(program, out var options);
                if (emulator == null) throw new InvalidOperationException(MissingLaunchSetup());
                request = Prepare(program, emulator, options, false);
                Prepare(program, emulator, options, true);
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

        private async Task RunLocalProgram(string path)
        {
            if (launching) return;
            string extraction = null;
            Request request = null;
            try
            {
                var emulator = store.Data.FindEmulator(store.Data.RunOnceEmulatorId);
                var source = store.Data.RunOnceOptions;
                bool recalled = source != null && File.Exists(emulator?.Exe);
                if (!recalled) emulator = store.Data.ResolveLaunch(null, out source);
                if (emulator == null) throw new InvalidOperationException(MissingLaunchSetup());
                launching = true;
                RefreshPreview();
                launchActivity.Show();
                if (string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase))
                {
                    launchStatus.Text = "Extracting ZIP…";
                    var directory = extraction = Path.Combine(Path.GetTempPath(), "X16ForAllTime-" + Guid.NewGuid().ToString("N"));
                    string[] candidates = await Task.Run(() => Catalog.ExtractCandidates(path, directory, null, includeBasic: true));
                    int selected = candidates.Length == 1 ? 0 :
                        LauncherDialogs.SelectItem(this, "Choose the startup program", candidates);
                    if (selected < 0)
                    {
                        launchStatus.Text = "Launch canceled";
                        return;
                    }
                    path = Path.Combine(directory, candidates[selected]);
                }

                Profile runOptions;
                using (var dialog = new LaunchSettingsForm(store.Data, "Run once", emulator, source,
                    runOncePath: path, recalledRunOnce: recalled))
                {
                    if (LauncherDialogs.Show(dialog, this) != DialogResult.OK)
                    {
                        launchStatus.Text = "Launch canceled";
                        return;
                    }
                    emulator = dialog.Emulator;
                    runOptions = Profile.Snapshot(dialog.Options, emulator);
                    request = Launch.Prepare(emulator, runOptions, path, true);
                }
                launchStatus.Text = "Starting emulator…";
                string launchedDirectory = extraction;
                Action cleanup = launchedDirectory == null ? (Action)null : () => Catalog.DiscardExtraction(launchedDirectory);
                await StartPreparedProgram(request, message => launchStatus.Text = message, cleanup);
                extraction = null; // The process monitor now owns the ZIP's temporary files.
                try
                {
                    store.Save(settings =>
                    {
                        settings.RunOnceEmulatorId = emulator.Id;
                        settings.RunOnceOptions = runOptions;
                    });
                }
                catch (Exception error)
                {
                    launchStatus.Text = "Emulator launched. Could not remember the Run once settings.";
                    LauncherDialogs.Message(this, error.Message, "Run once settings");
                }
            }
            catch (Exception error)
            {
                launchActivity.Hide();
                launchStatus.Text = "Launch failed";
                string details = error.Message + (request == null ? "" : Environment.NewLine + Environment.NewLine + CommandPreview(request));
                LauncherDialogs.Message(this, details, "Run once");
            }
            finally
            {
                Catalog.DiscardExtraction(extraction);
                launching = false;
                launchActivity.Hide();
                RefreshPreview();
            }
        }

        private async Task StartPreparedProgram(Request request, Action<string> report, Action cleanup = null)
        {
            int attempt = ++launchAttempt;
            string lateError = null;
            await EmulatorProcess.Start(request, new Progress<string>(message =>
            {
                lateError = message;
                if (attempt == launchAttempt && !IsDisposed) report(message);
            }), cleanup);
            if (lateError == null) report("Emulator launched");
        }
    }
}
