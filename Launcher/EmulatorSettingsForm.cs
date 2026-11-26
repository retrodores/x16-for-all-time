using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using static CommanderX16.MainForm;

namespace CommanderX16
{
    internal sealed class EmulatorSettingsForm : Form
    {
        private readonly TextBox name = Entry(), path = Entry();
        private readonly CheckBox standard = new CheckBox { Text = "Supports standard X16Emu options", AutoSize = true };

        public string BuildName => name.Text.Trim();
        public string Executable { get; private set; }
        public bool StandardOptions => standard.Checked;

        public EmulatorSettingsForm(Emulator emulator)
        {
            Text = emulator == null ? "Register existing emulator" : "Edit emulator";
            Font = new Font("Segoe UI", 10);
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(735, 205);
            MinimumSize = new Size(650, 240);
            MaximizeBox = false;
            MinimizeBox = false;
            var body = Table();
            body.Padding = new Padding(12);
            name.Text = emulator?.Name ?? "";
            path.Text = emulator?.Exe ?? "";
            standard.Checked = emulator?.UpstreamOptions ?? true;
            AddRow(body, Pair("Build name", name));
            AddRow(body, Pair("Executable", path, Button("Folder…", ChooseFolder), Button("Browse EXE…", () => ChooseExe(null))));
            AddRow(body, Pair("Options", standard));
            var confirm = Button(emulator == null ? "Register" : "Save", Confirm);
            var cancel = Button("Cancel");
            cancel.DialogResult = DialogResult.Cancel;
            AddScrollableContent(this, body, Flow(confirm, cancel));
            AcceptButton = confirm;
            CancelButton = cancel;
        }

        private void ChooseFolder()
        {
            using (var dialog = new FolderBrowserDialog { Description = "Select an unpacked emulator folder", ShowNewFolderButton = false })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    string executable = Path.Combine(dialog.SelectedPath, "x16emu.exe");
                    if (File.Exists(executable))
                    {
                        SetPath(executable);
                        return;
                    }
                    var nested = Directory.EnumerateDirectories(dialog.SelectedPath)
                        .Select(folder => Path.Combine(folder, "x16emu.exe")).Where(File.Exists).Take(2).ToArray();
                    if (nested.Length == 1) SetPath(nested[0]);
                    else ChooseExe(dialog.SelectedPath);
                }
                catch (IOException error) { ShowError(error.Message); }
                catch (UnauthorizedAccessException error) { ShowError(error.Message); }
            }
        }

        private void ChooseExe(string folder)
        {
            using (var dialog = new OpenFileDialog { Filter = "Windows executable (*.exe)|*.exe", InitialDirectory = folder ?? "" })
                if (dialog.ShowDialog(this) == DialogResult.OK) SetPath(dialog.FileName);
        }

        private void SetPath(string executable)
        {
            path.Text = executable;
            if (BuildName.Length == 0)
                name.Text = Path.GetFileName(Path.GetDirectoryName(executable));
        }

        private void Confirm()
        {
            try
            {
                string input = path.Text.Trim();
                if (input.Length == 0 || !Path.IsPathRooted(input) ||
                    !string.Equals(Path.GetExtension(input), ".exe", StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Choose a full path to an emulator executable.");
                string fullPath = Path.GetFullPath(input);
                if (!File.Exists(fullPath))
                    throw new IOException("The emulator executable could not be found." + Environment.NewLine + fullPath);
                SetPath(fullPath);
                if (BuildName.Length == 0) name.Text = Path.GetFileNameWithoutExtension(fullPath);
                Executable = fullPath;
                DialogResult = DialogResult.OK;
            }
            catch (Exception error) { ShowError(error.Message); }
        }

        private void ShowError(string message) =>
            LauncherDialogs.Message(this, message, Text);
    }
}
