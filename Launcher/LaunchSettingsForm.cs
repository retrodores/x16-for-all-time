using System;
using System.Drawing;
using System.IO;
using System.Linq;
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
        private readonly CheckBox startDefaults = new CheckBox { Text = "Use Start defaults", AutoSize = true };
        private readonly TableLayoutPanel launchFields = Table();
        private readonly Profile savedOptions;
        private readonly ComboBox builds = Combo(), profiles = Combo();
        private readonly ComboBox cpu = OptionsFor(HardwareOptions.Cpu, value => "65C02");
        private readonly ComboBox mhz = OptionsFor(HardwareOptions.Clock,
            value => value + " MHz" + (value == "8" ? " (standard)" : ""));
        private readonly ComboBox ram = OptionsFor(HardwareOptions.Ram, value =>
            value == "512" ? "512 KiB (standard)" : value == "1024" ? "1 MiB" : value == "1536" ? "1.5 MiB" : "2 MiB");
        private readonly ComboBox scale = OptionsFor(HardwareOptions.Scale, value => value + "×");
        private readonly TextBox gif = Entry(), extra = Entry(), rom = Entry(), sd = Entry(), fs = Entry();
        private readonly TextBox advancedClock = Entry(), advancedRam = Entry();
        private readonly CheckBox advanced = new CheckBox { Text = "Advanced hardware", AutoSize = true };
        private readonly CheckBox cpu816 = new CheckBox { Text = "65C816 emulation", AutoSize = true };
        private readonly CheckBox fullscreen = new CheckBox { Text = "Fullscreen", AutoSize = true };
        private readonly CheckBox autorun = new CheckBox { Text = "Run after loading", AutoSize = true };
        private readonly TableLayoutPanel hardware = Table(), advancedFields = Table();
        private readonly TextBox preview = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
        private readonly Button confirm;
        private Profile options;
        private bool loading;

        public Emulator Emulator => builds.SelectedItem as Emulator;
        public Profile Options { get; private set; }
        public bool UseStartDefaults => program != null && startDefaults.Checked;
        public string EntryName => name.Text.Trim();
        public string ProgramPath => Path.GetFullPath(prgPath.Text.Trim());

        public LaunchSettingsForm(Settings settings, string title, Emulator selected, Profile savedOptions,
            ProgramEntry program = null, string profileName = null, string profileId = null, string clearAction = null)
        {
            this.settings = settings;
            this.program = program;
            this.profileId = profileId;
            initialEmulator = selected;
            bool fixedBuild = profileName != null;
            this.savedOptions = savedOptions;
            Text = title;
            Font = new Font("Segoe UI", 10);
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(720, 650);
            MinimumSize = new Size(630, 550);
            MaximizeBox = false;
            MinimizeBox = false;

            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            var body = Table();
            body.Padding = new Padding(12);
            scroll.Controls.Add(body);
            Controls.Add(scroll);
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
                AddRow(body, Pair("Configuration", startDefaults));
            }
            AddRow(launchFields, Pair("Emulator", builds));
            if (!fixedBuild)
                AddRow(launchFields, Pair("Prefill from profile", profiles));
            AddRow(hardware, Pair("CPU", cpu));
            AddRow(hardware, Pair("Clock", mhz));
            AddRow(hardware, Pair("Banked RAM", ram));
            AddRow(hardware, advanced);
            AddRow(advancedFields, Pair("CPU override", cpu816));
            AddRow(advancedFields, Pair("Clock override (MHz)", advancedClock));
            AddRow(advancedFields, Pair("RAM override (KiB)", advancedRam));
            AddRow(hardware, advancedFields);
            AddRow(hardware, Pair("Display scale", scale));
            AddRow(hardware, Pair("Display", fullscreen));
            AddRow(hardware, Pair("GIF output", gif, Button("Browse…", PickGif)));
            AddRow(hardware, Pair("SD card image", sd));
            AddRow(hardware, Pair("Host filesystem root", fs));
            AddRow(launchFields, hardware);
            AddRow(launchFields, Pair("ROM", rom, Button("Browse…", PickRom)));
            AddRow(launchFields, Pair("Startup", autorun));
            AddRow(launchFields, Pair("Extra arguments", extra));
            AddRow(body, launchFields);
            AddRow(body, new Label { Text = program == null ? "Arguments preview" : "Command preview", AutoSize = true });
            AddRow(body, preview, SizeType.Absolute, 84);

            var cancel = Button("Cancel");
            cancel.DialogResult = DialogResult.Cancel;
            confirm = Button("Save", Confirm);
            var actions = clearAction == null ? Flow(confirm, cancel)
                : Flow(confirm, Button(clearAction, () => DialogResult = DialogResult.Abort), cancel);
            actions.Dock = DockStyle.Bottom;
            actions.Padding = new Padding(12, 0, 12, 8);
            Controls.Add(actions);
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

            builds.Items.AddRange(settings.Emulators.Cast<object>().ToArray());
            builds.Enabled = !fixedBuild;
            int index = settings.Emulators.FindIndex(emulator => emulator.Id == selected?.Id);
            if (index >= 0) builds.SelectedIndex = index;
            builds.SelectedIndexChanged += (sender, args) => LoadBuild();
            profiles.SelectedIndexChanged += (sender, args) =>
            {
                if (loading || profiles.SelectedIndex < 0) return;
                var source = Equals(profiles.SelectedItem, "Saved settings")
                    ? savedOptions : profiles.SelectedItem as Profile;
                LoadOptions(Profile.Snapshot(source, Emulator));
            };
            foreach (var box in new[] { cpu, scale, ram, mhz })
                box.SelectedIndexChanged += (sender, args) => UpdatePreview();
            foreach (var box in new[] { gif, extra, rom, sd, fs, advancedClock, advancedRam })
                box.TextChanged += (sender, args) => UpdatePreview();
            foreach (var box in new[] { fullscreen, autorun, cpu816 })
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
        }

        private void LoadBuild()
        {
            RefreshProfiles();
            var profile = settings.DefaultProfile(Emulator);
            loading = true;
            if (profile != null)
                profiles.SelectedItem = profile;
            else if (Emulator != null)
                profiles.SelectedItem = "Emulator defaults";
            loading = false;
            LoadOptions(Profile.Snapshot(profile, Emulator));
        }

        private void RefreshProfiles()
        {
            loading = true;
            profiles.Items.Clear();
            if (savedOptions != null && Emulator?.Id == initialEmulator?.Id)
                profiles.Items.Add("Saved settings");
            profiles.Items.Add("Emulator defaults");
            profiles.Items.AddRange(settings.Profiles.Where(profile =>
                Emulator != null && (profile.EmulatorId == Emulator.Id || profile.DetachedFrom != null))
                .Cast<object>().ToArray());
            profiles.SelectedIndex = 0;
            profiles.Enabled = Emulator != null;
            hardware.Enabled = Emulator?.UpstreamOptions == true;
            loading = false;
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
            cpu816.Checked = source.Cpu == "-c816";
            advancedClock.Text = HardwareOptions.Clock.Contains(source.Mhz) ? "" : source.Mhz ?? "";
            advancedRam.Text = HardwareOptions.Ram.Contains(source.Ram) ? "" : source.Ram ?? "";
            advanced.Checked = HardwareOptions.NeedsAdvanced(source);
            advancedFields.Visible = advanced.Checked;
            autorun.Checked = source.AutoRun;
            fullscreen.Checked = source.Fullscreen;
            gif.Text = source.Gif ?? "";
            extra.Text = source.Extra ?? "";
            rom.Text = source.Rom ?? "";
            sd.Text = source.SdCard ?? "";
            fs.Text = source.FsRoot ?? "";
            loading = false;
            UpdatePreview();
        }

        private void PickProgram()
        {
            using (var dialog = new OpenFileDialog { Filter = "Commander X16 program (*.prg)|*.prg" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                prgPath.Text = dialog.FileName;
                if (EntryName.Length == 0) name.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
            }
        }

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
                InitialDirectory = program?.Path == null ? "" : Path.GetDirectoryName(program.Path)
            })
                if (dialog.ShowDialog(this) == DialogResult.OK) gif.Text = dialog.FileName;
        }

        private void ReadOptions()
        {
            options.Cpu = advanced.Checked && cpu816.Checked ? "-c816" : Value(cpu);
            options.Mhz = advanced.Checked && advancedClock.Text.Trim().Length != 0
                ? advancedClock.Text.Trim() : Value(mhz);
            options.Ram = advanced.Checked && advancedRam.Text.Trim().Length != 0
                ? advancedRam.Text.Trim() : Value(ram);
            options.Scale = Value(scale);
            options.AutoRun = autorun.Checked;
            options.Fullscreen = fullscreen.Checked;
            options.Gif = gif.Text.Trim();
            options.Extra = extra.Text.Trim();
            options.Rom = rom.Text.Trim();
            options.SdCard = sd.Text.Trim();
            options.FsRoot = fs.Text.Trim();
        }

        private void UpdatePreview()
        {
            if (loading) return;
            confirm.Enabled = false;
            try
            {
                ReadOptions();
                if (program != null)
                {
                    if (string.IsNullOrWhiteSpace(prgPath.Text) ||
                        !string.Equals(Path.GetExtension(ProgramPath), ".prg", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Choose a .prg file.");
                    if (EntryName.Length == 0) throw new InvalidOperationException("Enter a PRG name.");
                    var emulator = UseStartDefaults ? settings.FindEmulator(settings.StartEmulatorId) : Emulator;
                    var launchOptions = UseStartDefaults ? settings.StartOptions ?? new Profile() : options;
                    preview.Text = UseStartDefaults && emulator == null
                        ? "Set an emulator in Start defaults before launching."
                        : CommandPreview(Launch.Prepare(emulator, launchOptions, ProgramPath, false));
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
            if (program == null && !builds.Enabled && (EntryName.Length == 0 || settings.Profiles.Any(profile =>
                profile.Id != profileId && profile.EmulatorId == Emulator.Id && Same(profile.Name, EntryName))))
            {
                MessageBox.Show(this, EntryName.Length == 0 ? "Enter a profile name." :
                    "That profile name is already in use for this emulator.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                name.Focus();
                return;
            }
            Options = options;
            DialogResult = DialogResult.OK;
        }
    }
}
