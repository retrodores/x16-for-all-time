using System;
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
        private readonly ListBox startMenu = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        private readonly Button startSettings = Button("Launch settings…");
        private readonly ProgressBar launchActivity = new ProgressBar
        {
            Size = new Size(86, 16), Margin = new Padding(4, 7, 4, 0), Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 25, Visible = false, AccessibleName = "Emulator launch activity"
        };
        private int launchAttempt;
        private readonly CheckBox saveGif = new CheckBox { Text = "Save GIF", AutoSize = true };
        private readonly TextBox gifFile = Entry();
        private readonly Button startDefaults = Button("Start defaults…");
        private readonly Label launchConfiguration = new Label { AutoSize = true };
        private readonly Button duplicate = Button("Duplicate entry…"),
            removeProgram = Button("Remove from list…");

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
            AddRow(startBody, Flow(duplicate, removeProgram));
            gifFile.AccessibleName = "GIF output file";
            AddRow(startBody, Pair("", saveGif));
            AddRow(startBody, Pair("File (blank: demo.gif)", gifFile));
            AddRow(startBody, launchConfiguration);
            AddRow(startBody, preview, SizeType.Absolute, 83);
            run.BackColor = Teal;
            run.ForeColor = Color.White;
            run.FlatStyle = FlatStyle.Flat;
            AddRow(startBody, Flow(run, startSettings, copy, launchActivity, launchStatus));

            saveGif.CheckedChanged += (sender, args) => RefreshPreview();
            gifFile.TextChanged += (sender, args) => RefreshPreview();
            search.TextChanged += (sender, args) => RefreshPrograms();
            startMenu.SelectedIndexChanged += (sender, args) => RefreshPreview();
            startMenu.MouseDoubleClick += async (sender, args) =>
            {
                if (args.Button != MouseButtons.Left) return;
                int index = startMenu.IndexFromPoint(args.Location);
                if (index >= 0) await RunSelected(startMenu.Items[index] as ProgramEntry);
            };
            run.Click += async (sender, args) => await RunSelected(startMenu.SelectedItem as ProgramEntry);
            startDefaults.Click += (sender, args) => Attempt(EditStartDefaults);
            startSettings.Click += (sender, args) => Attempt(() => EditLaunchSettings(startMenu.SelectedItem as ProgramEntry));
            copy.Click += (sender, args) => Attempt(CopyPreview);
            removeProgram.Click += (sender, args) => Attempt(RemoveProgram);
            duplicate.Click += (sender, args) => Attempt(DuplicateProgram);
            tabs.SelectedIndexChanged += (sender, args) =>
                AcceptButton = tabs.SelectedTab == start ? run : null;
            AcceptButton = run;
            RefreshPrograms();
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
            string id = select ?? (startMenu.SelectedItem as ProgramEntry)?.Id;
            string filter = search.Text.Trim();
            var entries = store.Data.Programs.Where(p =>
                p.Name.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                p.Path.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(p => p.Path, StringComparer.OrdinalIgnoreCase).ToArray();
            startMenu.BeginUpdate();
            try
            {
                startMenu.Items.Clear();
                startMenu.Items.AddRange(entries);
                if (entries.Length > 0)
                    startMenu.SelectedIndex = Math.Max(0, Array.FindIndex(entries, p => p.Id == id));
            }
            finally { startMenu.EndUpdate(); }
            RefreshPreview();
        }

        private void RemoveProgram()
        {
            var program = (ProgramEntry)startMenu.SelectedItem;
            if (!LauncherDialogs.Confirm(this, "Remove " + program.Name + " from the PRG list? The program file will stay on disk.",
                "Remove PRG"))
                return;
            store.Save(data => data.Programs.RemoveAll(entry => entry.Id == program.Id));
            RefreshPrograms();
        }

        private void DuplicateProgram()
        {
            var program = (ProgramEntry)startMenu.SelectedItem;
            var entry = Json.Read<ProgramEntry>(Json.Write(program));
            entry.Id = Guid.NewGuid().ToString("N");
            entry.Name = program.Name + " (copy)";
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
                    entry.Path = dialog.ProgramPath;
                    entry.EmulatorId = dialog.UseStartDefaults ? null : dialog.Emulator?.Id;
                    entry.Options = dialog.UseStartDefaults ? null : dialog.Options;
                    entry.RequiresEmulatorSelection = !dialog.UseStartDefaults && dialog.Emulator == null;
                    entry.ProfileId = null;
                    if (create) data.Programs.Add(entry);
                });
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

        private Request Prepare(ProgramEntry program, Emulator emulator, bool validate)
        {
            var options = store.Data.LaunchOptions(program);
            if (saveGif.Checked)
                options = Launch.WithGifOverride(options, emulator, gifFile.Text);
            return Launch.Prepare(emulator, options, program.Path, validate);
        }

        internal static string CommandPreview(Request request) =>
            Launch.Quote(request.Exe) + " " + request.Arguments + Environment.NewLine +
                "Working directory: " + request.WorkingDirectory;

        private void RefreshPreview()
        {
            var program = startMenu.SelectedItem as ProgramEntry;
            var emulator = program == null ? null : store.Data.LaunchEmulator(program);
            UpdateCatalogSelection();
            run.Enabled = false;
            run.Text = program != null && store.Data.LaunchOptions(program).AutoRun ? "Run PRG" : "Load PRG";
            saveGif.Enabled = !launching && program != null;
            gifFile.Enabled = saveGif.Enabled && saveGif.Checked;
            startDefaults.Enabled = !launching && store.Data.Emulators.Count != 0;
            launchConfiguration.Text = emulator == null ? "Command preview" :
                (store.Data.UsesStartDefaults(program) ? "Start defaults" : "PRG settings") + " · " + emulator.Name;
            startSettings.Enabled = duplicate.Enabled = removeProgram.Enabled = !launching && program != null;
            copy.Enabled = false;
            if (program == null) preview.Text = store.Data.Programs.Count == 0 ? "Add a PRG to get started." : "No matching PRGs.";
            else if (store.Data.Emulators.Count == 0) preview.Text = "Register or install an X16Emu build on the Emulators tab.";
            else if (emulator == null) preview.Text = MissingLaunchSetup(program);
            else
            {
                try
                {
                    var command = Prepare(program, emulator, true);
                    run.Text = Launch.HasExtraOption(command.Arguments, "-run") ? "Run PRG" : "Load PRG";
                    run.Enabled = copy.Enabled = !launching;
                    preview.Text = CommandPreview(command);
                }
                catch (Exception error) { preview.Text = error.Message; }
            }
        }

        private void CopyPreview()
        {
            var program = (ProgramEntry)startMenu.SelectedItem;
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
