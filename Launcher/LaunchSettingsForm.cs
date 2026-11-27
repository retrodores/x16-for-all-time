using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using static CommanderX16.MainForm;

namespace CommanderX16
{
    internal sealed class LaunchSettingsForm : Form
    {
        private readonly Settings settings;
        private readonly ProgramEntry program;
        private readonly string profileId;
        private readonly Emulator initialEmulator;
        private readonly TextBox name = Entry(), prgPath = Entry();
        private readonly CheckBox startDefaults = new CheckBox { Text = "Use Favorites defaults", AutoSize = true };
        private readonly TableLayoutPanel launchFields = Table();
        private readonly Profile savedOptions;
        private readonly bool editingStartDefaults;
        private readonly string runOncePath;
        private readonly string gifDirectory;
        private readonly string runOnceKitRoot;
        private readonly bool recalledRunOnce;
        private readonly ComboBox builds = Combo(), profiles = ProfileCombo();
        private readonly ComboBox cpu = OptionsFor(HardwareOptions.Cpu, HardwareOptions.CpuName);
        private readonly ComboBox mhz = OptionsFor(HardwareOptions.Clock,
            value => value + " MHz" + (value == "8" ? " (standard)" : ""));
        private readonly ComboBox ram = OptionsFor(HardwareOptions.Ram, value =>
            value == "512" ? "512 KiB (standard)" : value == "1024" ? "1 MiB" : value == "1536" ? "1.5 MiB" : "2 MiB");
        private readonly ComboBox scale = OptionsFor(HardwareOptions.Scale, value => value + "×");
        private readonly TextBox gif = Entry(), extra = Entry(), rom = Entry(), sd = Entry(), fs = Entry();
        private readonly TextBox advancedClock = Entry(), advancedRam = Entry();
        private readonly CheckBox advanced = new CheckBox { Text = "Advanced hardware", AutoSize = true };
        private readonly CheckBox fullscreen = new CheckBox { Text = "Fullscreen", AutoSize = true };
        private readonly CheckBox serialCard = new CheckBox
        {
            Text = "Enable serial card (-serialcard)", AutoSize = true
        };
        private readonly CheckBox autorun = new CheckBox { Text = "Run after loading", AutoSize = true };
        private readonly TableLayoutPanel hardware = Table(), hardwareOutput = Table(), advancedFields = Table();
        private readonly TextBox preview = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, WordWrap = false, ScrollBars = ScrollBars.Horizontal };
        private readonly Button confirm;
        private Profile options;
        private bool loading;
        private bool preserveUnassignedOptions;
        private bool importingProgram;
        private string importedDirectory;

        public Emulator Emulator => builds.SelectedItem as Emulator;
        public Profile Options { get; private set; }
        public bool UseStartDefaults => program != null && startDefaults.Checked;
        public string EntryName => name.Text.Trim();
        public string ProgramPath => Path.GetFullPath(prgPath.Text.Trim());

        public LaunchSettingsForm(Settings settings, string title, Emulator selected, Profile savedOptions,
            ProgramEntry program = null, string profileName = null, string profileId = null, string clearAction = null,
            string runOncePath = null, bool recalledRunOnce = false, string gifDirectory = null, string runOnceKitRoot = null)
        {
            this.settings = settings;
            this.program = program;
            this.profileId = profileId;
            initialEmulator = selected;
            bool fixedBuild = profileName != null;
            this.runOncePath = runOncePath;
            this.gifDirectory = gifDirectory;
            this.runOnceKitRoot = runOnceKitRoot;
            this.recalledRunOnce = recalledRunOnce;
            editingStartDefaults = program == null && !fixedBuild && runOncePath == null;
            this.savedOptions = savedOptions;
            preserveUnassignedOptions = program != null && selected == null && savedOptions != null;
            Text = title;
            Font = new Font("Segoe UI", 10);
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(980, 570);
            MaximizeBox = false;
            MinimizeBox = false;

            var body = Table();
            var columns = Table();
            var tables = new[] { body, launchFields, columns, hardware, hardwareOutput, advancedFields };
            foreach (var table in tables) table.SuspendLayout();
            body.Padding = new Padding(12);
            if (program != null || fixedBuild)
            {
                name.Text = program?.Name ?? profileName;
                AddRow(body, Pair(program == null ? "Profile name" : "PRG name", name));
            }
            if (program != null)
            {
                prgPath.Text = program.Path ?? "";
                startDefaults.Checked = settings.UsesStartDefaults(program);
                AddRow(body, Pair("Program file", prgPath, Button("Browse…", PickProgram)));
                Disposed += (sender, args) => Catalog.DiscardExtraction(importedDirectory);
                AddRow(body, Pair("Configuration", startDefaults));
            }
            else if (runOncePath != null)
                AddRow(body, Pair("Program file", new TextBox { Text = runOncePath, ReadOnly = true }));
            AddRow(launchFields, Pair("Emulator", builds));
            if (!fixedBuild)
                AddRow(launchFields, Pair(editingStartDefaults ? "Load settings" : "Prefill from profile", profiles));
            AddRow(hardware, Pair("CPU", cpu));
            AddRow(hardware, Pair("Clock", mhz));
            AddRow(hardware, Pair("Banked RAM", ram));
            AddRow(hardware, advanced);
            AddRow(advancedFields, Pair("Clock override (MHz)", advancedClock));
            AddRow(advancedFields, Pair("RAM override (KiB)", advancedRam));
            AddRow(hardware, advancedFields);
            AddRow(hardwareOutput, Pair("Display scale", scale));
            AddRow(hardwareOutput, Pair("Display", fullscreen));
            AddRow(hardwareOutput, Pair("GIF output", gif, Button("Browse…", PickGif)));
            AddRow(hardwareOutput, Pair("SD card image", sd));
            AddRow(hardwareOutput, Pair("Host filesystem root", fs));
            columns.ColumnCount = 2;
            columns.RowCount = 1;
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
            hardwareOutput.Margin = new Padding(12, 0, 0, 0);
            columns.Controls.Add(hardware, 0, 0);
            columns.Controls.Add(hardwareOutput, 1, 0);
            AddRow(launchFields, columns);
            AddRow(launchFields, Pair("ROM", rom, Button("Browse…", PickRom)));
            AddRow(launchFields, runOncePath == null ? (Control)Flow(autorun, serialCard) : serialCard);
            AddRow(launchFields, Pair("Extra arguments", extra));
            AddRow(body, launchFields);
            AddRow(body, new Label { Text = program == null && runOncePath == null ? "Arguments preview" : "Command preview", AutoSize = true });
            AddRow(body, preview, SizeType.Absolute, 54);

            var cancel = Button("Cancel");
            cancel.DialogResult = DialogResult.Cancel;
            confirm = Button(runOncePath == null ? "Save" : "Run", Confirm);
            var actions = clearAction == null ? Flow(confirm, cancel)
                : Flow(confirm, Button(clearAction, () => DialogResult = DialogResult.Abort), cancel);
            AddConfigurationContent(this, body, actions);
            AcceptButton = confirm;
            CancelButton = cancel;

            name.TextChanged += (sender, args) => UpdatePreview();
            prgPath.TextChanged += (sender, args) => UpdatePreview();
            startDefaults.CheckedChanged += (sender, args) =>
            {
                launchFields.Enabled = !UseStartDefaults;
                UpdatePreview();
            };
            launchFields.Enabled = !UseStartDefaults;

            var available = settings.Emulators.Where(emulator => runOncePath == null || File.Exists(emulator.Exe)).ToArray();
            builds.Items.AddRange(available);
            builds.Enabled = !fixedBuild;
            int index = Array.FindIndex(available, emulator => emulator.Id == selected?.Id);
            if (index >= 0) builds.SelectedIndex = index;
            builds.SelectedIndexChanged += (sender, args) => LoadBuild();
            profiles.SelectedIndexChanged += (sender, args) =>
            {
                if (loading || profiles.SelectedIndex < 0) return;
                LoadOptions(Profile.Snapshot(((ProfileChoice)profiles.SelectedItem).Source, Emulator));
            };
            foreach (var box in new[] { cpu, scale, ram, mhz })
                box.SelectedIndexChanged += (sender, args) => UpdatePreview();
            foreach (var box in new[] { gif, extra, rom, sd, fs, advancedClock, advancedRam })
                box.TextChanged += (sender, args) => UpdatePreview();
            foreach (var box in new[] { fullscreen, autorun, serialCard })
                box.CheckedChanged += (sender, args) => UpdatePreview();
            advanced.CheckedChanged += (sender, args) =>
            {
                advancedFields.Visible = advanced.Checked;
                UpdatePreview();
            };
            if (savedOptions != null || fixedBuild)
            {
                RefreshProfiles();
                LoadOptions(Profile.Snapshot(savedOptions, selected));
            }
            else
                LoadBuild();
            foreach (var table in tables.Reverse()) table.ResumeLayout(false);
        }

        private void LoadBuild()
        {
            RefreshProfiles(preferDefault: !((editingStartDefaults || runOncePath != null) && HasSavedSettings));
            if (preserveUnassignedOptions && Emulator != null)
            {
                preserveUnassignedOptions = false;
                LoadOptions(Profile.Snapshot(options, Emulator));
                return;
            }
            LoadOptions(Profile.Snapshot(((ProfileChoice)profiles.SelectedItem).Source, Emulator));
        }

        private bool HasSavedSettings => savedOptions != null && Emulator?.Id == initialEmulator?.Id;
        private string SavedSettingsLabel => runOncePath != null ? (recalledRunOnce ? "Last Run once settings" : "Favorites launch settings") :
            editingStartDefaults ? "Saved Favorites settings" : "Saved settings";
        private string BuiltInSettingsLabel => editingStartDefaults ? "Built-in emulator defaults" : "Emulator defaults";

        private void RefreshProfiles(bool preferDefault = false)
        {
            var emulator = Emulator;
            var currentDefault = settings.DefaultProfile(emulator);
            var choices = new List<ProfileChoice>();
            if (HasSavedSettings) choices.Add(new ProfileChoice(SavedSettingsLabel, savedOptions));
            var defaultChoice = currentDefault == null
                ? new ProfileChoice(BuiltInSettingsLabel)
                : new ProfileChoice(editingStartDefaults ? currentDefault.Name + " (emulator preset)" : currentDefault.Name,
                    currentDefault);
            choices.Add(defaultChoice);
            if (currentDefault != null) choices.Add(new ProfileChoice(BuiltInSettingsLabel));
            if (emulator != null)
            {
                choices.AddRange(settings.Profiles.Where(profile => profile.EmulatorId == emulator.Id && profile != currentDefault)
                    .Select(profile => new ProfileChoice(profile.Name, profile)));
                if (!editingStartDefaults)
                    choices.AddRange(settings.Profiles.Where(profile => profile.EmulatorId == null && profile.DetachedFrom != null)
                        .Select(profile => new ProfileChoice(profile.ToString(), profile)));
            }

            // Compare editable launch values so duplicate history entries don't crowd the picker.
            var groups = choices.GroupBy(choice =>
            {
                var snapshot = Profile.Snapshot(choice.Source, emulator);
                var serial = snapshot.EnableSerialCard;
                var address = snapshot.SerialCardAddress;
                string arguments = snapshot.Extra.Trim();
                if (emulator?.SupportsSerialCard == true)
                {
                    serial = serial != false;
                    arguments = Launch.RemoveSerialCardOption(arguments, out var suppliedAddress);
                    address = suppliedAddress ?? address;
                }
                return (snapshot.Cpu, snapshot.Mhz, snapshot.Ram, snapshot.Scale, runOncePath != null || snapshot.AutoRun, snapshot.Fullscreen,
                    snapshot.Gif?.Trim() ?? "", snapshot.Rom.Trim(), snapshot.SdCard?.Trim() ?? "",
                    snapshot.FsRoot?.Trim() ?? "", arguments, serial, address);
            })
                .OrderByDescending(group => group.Contains(defaultChoice))
                .ThenBy(group => group.Key.Cpu == "-c02" ? 0 : group.Key.Cpu == "-c816" ? 1 : 2)
                .ThenByDescending(group => int.TryParse(group.Key.Mhz, out int mhz) ? mhz : 0)
                .ThenByDescending(group => int.TryParse(group.Key.Ram, out int ram) ? ram : 0)
                .ToArray();

            loading = true;
            profiles.Items.Clear();
            var labels = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
            foreach (var group in groups)
            {
                var choice = group.Contains(defaultChoice) ? defaultChoice : group.First();
                string label = choice.Label;
                for (int number = 2; !labels.Add(label); number++)
                    label = $"{choice.Label} ({number})";
                profiles.Items.Add(new ProfileChoice(label, choice.Source));
            }
            var initialChoice = preferDefault ? defaultChoice : choices[0];
            profiles.SelectedIndex = Array.FindIndex(groups, group => group.Contains(initialChoice));
            profiles.Enabled = emulator != null;
            hardware.Enabled = hardwareOutput.Enabled = emulator?.UpstreamOptions ?? (program != null);
            loading = false;
        }

        private sealed class ProfileChoice
        {
            public readonly string Label;
            public readonly Profile Source;

            public ProfileChoice(string label, Profile source = null)
            {
                Label = label;
                Source = source;
            }

            public override string ToString() => Label;
        }

        private sealed class Choice
        {
            public string Value;
            public string Label;
            public override string ToString() => Label;
        }

        private static ComboBox OptionsFor(string[] values, Func<string, string> label)
        {
            var box = Combo("Emulator default");
            box.Items.AddRange(values.Select(value => new Choice { Value = value, Label = label(value) }).ToArray());
            return box;
        }

        private static string Value(ComboBox box) => (box.SelectedItem as Choice)?.Value;
        private static void SelectValue(ComboBox box, string value)
        {
            var index = Array.FindIndex(box.Items.Cast<object>().ToArray(), item => item is Choice choice && choice.Value == value);
            box.SelectedIndex = Math.Max(0, index);
        }

        private void LoadOptions(Profile source)
        {
            options = source;
            loading = true;
            SelectValue(cpu, source.Cpu);
            SelectValue(mhz, source.Mhz);
            SelectValue(ram, source.Ram);
            SelectValue(scale, source.Scale);
            advancedClock.Text = HardwareOptions.Clock.Contains(source.Mhz) ? "" : source.Mhz ?? "";
            advancedRam.Text = HardwareOptions.Ram.Contains(source.Ram) ? "" : source.Ram ?? "";
            advanced.Checked = HardwareOptions.NeedsAdvanced(source);
            advancedFields.Visible = advanced.Checked;
            autorun.Checked = source.AutoRun;
            fullscreen.Checked = source.Fullscreen;
            gif.Text = source.Gif ?? "";
            serialCard.Enabled = Emulator?.SupportsSerialCard == true;
            serialCard.Checked = serialCard.Enabled && source.EnableSerialCard != false;
            extra.Text = source.Extra ?? "";
            if (serialCard.Enabled)
            {
                extra.Text = Launch.RemoveSerialCardOption(source.Extra, out var address);
                options.SerialCardAddress = address ?? source.SerialCardAddress;
            }
            rom.Text = source.Rom ?? "";
            sd.Text = source.SdCard ?? "";
            fs.Text = source.FsRoot ?? "";
            loading = false;
            UpdatePreview();
        }

        private async void PickProgram()
        {
            if (importingProgram) return;
            string path = LauncherDialogs.PickProgram(this);
            if (path != null) await SelectProgramFile(path);
        }

        private async Task SelectProgramFile(string path)
        {
            string extracted = null;
            try
            {
                if (string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase))
                {
                    importingProgram = true;
                    confirm.Enabled = false;
                    preview.Text = "Extracting ZIP…";
                    extracted = Path.Combine(SettingsStore.Home, "LocalPrograms", Guid.NewGuid().ToString("N"));
                    string[] candidates = await Task.Run(() => Catalog.ExtractCandidates(path, extracted, null, includeBasic: true));
                    if (IsDisposed) return;
                    int selected = candidates.Length == 1 ? 0 :
                        LauncherDialogs.SelectItem(this, "Choose the startup program", candidates);
                    if (selected < 0) return;
                    path = Path.Combine(extracted, candidates[selected]);
                }

                if (!IsImportedProgram(path))
                {
                    Catalog.DiscardExtraction(importedDirectory);
                    importedDirectory = extracted;
                    extracted = null;
                }
                prgPath.Text = path;
                if (EntryName.Length == 0) name.Text = Path.GetFileNameWithoutExtension(path);
            }
            catch (Exception error)
            {
                if (!IsDisposed) LauncherDialogs.Message(this, error.Message, "Add program");
            }
            finally
            {
                Catalog.DiscardExtraction(extracted);
                importingProgram = false;
                if (!IsDisposed) UpdatePreview();
            }
        }

        internal void KeepImportedProgram()
        {
            if (IsImportedProgram(ProgramPath)) importedDirectory = null;
        }

        private bool IsImportedProgram(string path) =>
            importedDirectory != null &&
            path.StartsWith(importedDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

        private void PickRom()
        {
            using (var dialog = new OpenFileDialog { Filter = "ROM images (*.bin;*.rom)|*.bin;*.rom|All files (*.*)|*.*" })
                if (dialog.ShowDialog(this) == DialogResult.OK) rom.Text = dialog.FileName;
        }

        private void PickGif()
        {
            using (var dialog = new SaveFileDialog
            {
                Filter = "GIF recording (*.gif)|*.gif", DefaultExt = "gif",
                InitialDirectory = gifDirectory ?? Path.GetDirectoryName(runOncePath ?? program?.Path) ?? "",
                OverwritePrompt = false
            })
                if (dialog.ShowDialog(this) == DialogResult.OK) gif.Text = dialog.FileName;
        }

        private void ReadOptions()
        {
            options.Cpu = Value(cpu);
            options.Mhz = advanced.Checked && advancedClock.Text.Trim().Length != 0
                ? advancedClock.Text.Trim() : Value(mhz);
            options.Ram = advanced.Checked && advancedRam.Text.Trim().Length != 0
                ? advancedRam.Text.Trim() : Value(ram);
            options.Scale = Value(scale);
            options.AutoRun = runOncePath != null || autorun.Checked;
            options.Fullscreen = fullscreen.Checked;
            options.Gif = gif.Text.Trim();
            options.Extra = extra.Text.Trim();
            if (serialCard.Enabled)
            {
                options.EnableSerialCard = serialCard.Checked;
                options.Extra = Launch.RemoveSerialCardOption(options.Extra, out var address);
                options.SerialCardAddress = address ?? options.SerialCardAddress;
            }
            options.Rom = rom.Text.Trim();
            options.SdCard = sd.Text.Trim();
            options.FsRoot = fs.Text.Trim();
        }

        private void UpdatePreview()
        {
            if (loading || importingProgram) return;
            confirm.Enabled = false;
            try
            {
                ReadOptions();
                if (runOncePath != null)
                    preview.Text = CommandPreview(Launch.Prepare(Emulator, options, runOncePath, false, runOnceKitRoot, gifDirectory));
                else if (program != null)
                {
                    if (string.IsNullOrWhiteSpace(prgPath.Text) ||
                        !Launch.IsProgramFile(ProgramPath))
                        throw new InvalidOperationException("Choose a .prg or .bas file.");
                    if (EntryName.Length == 0) throw new InvalidOperationException("Enter a PRG name.");
                    var launchOptions = options;
                    var emulator = UseStartDefaults ? settings.ResolveLaunch(null, out launchOptions) : Emulator;
                    preview.Text = emulator == null
                        ? (UseStartDefaults ? "Set an emulator in Favorites defaults before launching." :
                            "Settings can be saved. Choose an emulator before launching.")
                        : CommandPreview(Launch.Prepare(emulator, launchOptions, ProgramPath, false,
                            Same(program.Path, ProgramPath) ? program.SdKitRoot : null));
                }
                else
                {
                    string args = Launch.OptionArguments(Emulator, options);
                    string autoRun = options.AutoRun ? "-run" : "";
                    string display = (autoRun + " " + args).Trim();
                    preview.Text = display.Length == 0 ? "Emulator defaults" : display;
                }
                confirm.Enabled = true;
            }
            catch (Exception error) { preview.Text = error.Message; }
        }

        private void Confirm()
        {
            UpdatePreview();
            if (!confirm.Enabled) return;
            if (program != null && settings.HasProgramName(EntryName, program.Id))
            {
                LauncherDialogs.Message(this, "This name is already in Favorites. Add a version or another distinguishing detail.", Text);
                name.Focus();
                name.SelectAll();
                return;
            }
            if (program == null && !builds.Enabled && (EntryName.Length == 0 || settings.Profiles.Any(profile =>
                profile.Id != profileId && profile.EmulatorId == Emulator.Id && Same(profile.Name, EntryName))))
            {
                LauncherDialogs.Message(this, EntryName.Length == 0 ? "Enter a profile name." :
                    "That profile name is already in use for this emulator.", Text);
                name.Focus();
                return;
            }
            Options = options;
            DialogResult = DialogResult.OK;
        }
    }
}
