using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CommanderX16
{
    internal sealed partial class MainForm
    {
        private readonly TextBox catalogSearch = Entry();
        private readonly CheckBox downloadableOnly = new CheckBox { Text = "Downloadable only", AutoSize = true };
        private readonly ListBox catalogList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        private readonly TextBox catalogDetails = new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            AccessibleName = "Selected program details"
        };
        private readonly Button refreshCatalog = Button("Refresh");
        private readonly Button openProject = Button("Open project page");
        private readonly Button installProgram = Button("Install to Start");
        private readonly Button downloadProgram = Button("Download and launch");
        private readonly Label catalogStatus = new Label
        {
            Dock = DockStyle.Fill, AutoEllipsis = true, Height = 24, ForeColor = Navy
        };
        private readonly ProgressBar catalogActivity = new ProgressBar
        {
            Size = new Size(86, 16), Margin = new Padding(4, 7, 4, 0), Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 25, Visible = false, AccessibleName = "Catalog activity"
        };
        private CatalogProgram[] catalogPrograms;
        private bool catalogBusy;

        private void BuildCatalogPage(TabPage page, TabControl tabs, TabPage start)
        {
            var body = PageBody(page);
            catalogSearch.AccessibleName = "Find a catalog program";
            catalogList.AccessibleName = "X16 Files programs";
            AddRow(body, Pair("Find a program", catalogSearch, refreshCatalog));
            AddRow(body, downloadableOnly);
            AddRow(body, catalogList, SizeType.Percent, 100);
            AddRow(body, catalogDetails, SizeType.Absolute, 112);
            AddRow(body, Flow(openProject, installProgram, downloadProgram, catalogActivity));
            AddRow(body, catalogStatus);
            UpdateCatalogSelection();

            tabs.SelectedIndexChanged += async (sender, args) =>
            {
                if (tabs.SelectedTab == page && catalogPrograms == null) await LoadCatalog();
            };
            refreshCatalog.Click += async (sender, args) => await LoadCatalog();
            catalogSearch.TextChanged += (sender, args) => ShowCatalogPrograms();
            downloadableOnly.CheckedChanged += (sender, args) => ShowCatalogPrograms();
            catalogList.SelectedIndexChanged += (sender, args) => UpdateCatalogSelection();
            openProject.Click += (sender, args) => Attempt(() =>
            {
                var program = (CatalogProgram)catalogList.SelectedItem;
                Process.Start(new ProcessStartInfo(program.PageUrl) { UseShellExecute = true });
            });
            installProgram.Click += async (sender, args) =>
            {
                var id = await InstallCatalogProgram();
                if (id == null) return;
                search.Clear();
                RefreshPrograms(id);
                tabs.SelectedTab = start;
                launchStatus.Text = "Added from X16 Files";
            };
            downloadProgram.Click += async (sender, args) => await LaunchCatalogProgram();
        }

        private async Task LoadCatalog()
        {
            if (catalogBusy) return;
            SetCatalogBusy(true, "Loading X16 Files catalog…");
            try
            {
                catalogPrograms = await Catalog.Fetch();
                ShowCatalogPrograms();
                catalogStatus.Text = catalogPrograms.Length + " programs in X16 Files";
            }
            catch (Exception error)
            {
                catalogStatus.Text = "X16 Files catalog unavailable. Choose Refresh to try again.";
                if (catalogPrograms == null)
                    catalogDetails.Text = error.Message;
            }
            finally { SetCatalogBusy(false); }
        }

        private void ShowCatalogPrograms()
        {
            if (catalogPrograms == null) return;
            var id = (catalogList.SelectedItem as CatalogProgram)?.Id;
            var filter = catalogSearch.Text.Trim();
            var entries = catalogPrograms
                .Where(program => !downloadableOnly.Checked || program.Downloads.Length > 0)
                .Where(program => new[] { program.ToString(), program.Category, program.Author?.Name }
                    .Any(value => value?.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0))
                .OrderBy(program => program.ToString(), StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(program => program.Id, StringComparer.Ordinal).ToArray();
            catalogList.BeginUpdate();
            try
            {
                catalogList.Items.Clear();
                catalogList.Items.AddRange(entries);
                if (entries.Length > 0)
                    catalogList.SelectedIndex = Math.Max(0, Array.FindIndex(entries, program => program.Id == id));
            }
            finally { catalogList.EndUpdate(); }
            UpdateCatalogSelection();
        }

        private void UpdateCatalogSelection()
        {
            var program = catalogList.SelectedItem as CatalogProgram;
            openProject.Enabled = !catalogBusy && Catalog.IsWebUrl(program?.PageUrl);
            downloadProgram.Enabled = !catalogBusy && !launching && (program?.Downloads.Length ?? 0) > 0;
            installProgram.Enabled = downloadProgram.Enabled;
            installProgram.Visible = downloadProgram.Visible = (program?.Downloads.Length ?? 0) > 0;
            installProgram.Text = program != null && store.Data.Programs.Any(entry => entry.CatalogId == program.Id)
                ? "Reinstall to Start" : "Install to Start";
            if (program == null)
            {
                if (catalogPrograms != null) catalogDetails.Text = "No matching programs.";
                return;
            }
            var metadata = string.Join(" · ", new[] { program.Category, program.Author?.Name, program.Version }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            catalogDetails.Text = string.Join(Environment.NewLine, new[] { program.ToString(), metadata, program.Description,
                program.Downloads.Length > 0 ? null : "No ZIP or PRG download available." }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        private void SetCatalogBusy(bool busy, string message = null)
        {
            catalogBusy = busy;
            catalogActivity.Visible = busy;
            refreshCatalog.Enabled = catalogSearch.Enabled = downloadableOnly.Enabled = catalogList.Enabled = !busy;
            if (message != null) catalogStatus.Text = message;
            UpdateCatalogSelection();
        }

        private CatalogPackage SelectCatalogDownload(CatalogProgram program)
        {
            var downloads = program.Downloads;
            if (downloads.Length == 1) return downloads[0];
            var selected = SelectCatalogItem("Choose a download", downloads.Select(item => item.Url).ToArray());
            return selected < 0 ? null : downloads[selected];
        }

        private int SelectCatalogItem(string title, string[] items)
        {
            using (var dialog = new Form
            {
                Text = title, Font = Font, ClientSize = new Size(640, 300), AutoScaleMode = AutoScaleMode.Dpi,
                StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false,
                MinimizeBox = false
            })
            {
                var body = Table();
                body.Dock = DockStyle.Fill;
                body.AutoSize = false;
                body.Padding = new Padding(12);
                dialog.Controls.Add(body);
                var list = new ListBox
                {
                    Dock = DockStyle.Fill, HorizontalScrollbar = true, IntegralHeight = false,
                    AccessibleName = title
                };
                list.Items.AddRange(items);
                list.SelectedIndex = 0;
                var select = Button("Select");
                select.DialogResult = DialogResult.OK;
                var cancel = Button("Cancel");
                cancel.DialogResult = DialogResult.Cancel;
                dialog.AcceptButton = select;
                dialog.CancelButton = cancel;
                list.DoubleClick += (sender, args) =>
                {
                    if (list.IndexFromPoint(list.PointToClient(Cursor.Position)) >= 0)
                        dialog.DialogResult = DialogResult.OK;
                };
                AddRow(body, list, SizeType.Percent, 100);
                AddRow(body, Flow(select, cancel));
                dialog.ActiveControl = list;
                return LauncherDialogs.Show(dialog, this) == DialogResult.OK ? list.SelectedIndex : -1;
            }
        }

        private string SelectCatalogPrg(string[] paths)
        {
            var selected = SelectCatalogItem("Choose the startup PRG", paths);
            return selected < 0 ? null : paths[selected];
        }

        private async Task<string> InstallCatalogProgram()
        {
            var program = catalogList.SelectedItem as CatalogProgram;
            if (catalogBusy || launching || (program?.Downloads.Length ?? 0) == 0) return null;
            var package = SelectCatalogDownload(program);
            if (package == null) return null;
            SetCatalogBusy(true, "Preparing installation…");
            try
            {
                var path = await Catalog.Download(program, package,
                    new Progress<string>(message => catalogStatus.Text = message), SelectCatalogPrg);
                if (path == null)
                {
                    catalogStatus.Text = "Download canceled";
                    return null;
                }
                string id = null;
                store.Save(data =>
                {
                    var entry = data.Programs.FirstOrDefault(item => item.CatalogId == program.Id);
                    if (entry == null)
                    {
                        entry = new ProgramEntry { CatalogId = program.Id, Name = program.ToString() };
                        // Use the same default emulator fallback as a catalog launch.
                        if (data.FindEmulator(data.StartEmulatorId) == null && data.Emulators.Count == 1)
                        {
                            var emulator = data.Emulators[0];
                            entry.EmulatorId = emulator.Id;
                            entry.Options = Profile.Snapshot(data.DefaultProfile(emulator), emulator);
                        }
                        data.Programs.Add(entry);
                    }
                    entry.Path = path;
                    id = entry.Id;
                });
                catalogStatus.Text = "Installed " + program + " and added it to Start.";
                return id;
            }
            catch (DownloadForbiddenException error)
            {
                ShowForbiddenDownload(error);
                return null;
            }
            catch (Exception error)
            {
                catalogStatus.Text = "Installation failed";
                LauncherDialogs.Message(this, error.Message, "Install to Start");
                return null;
            }
            finally { SetCatalogBusy(false); }
        }

        private void ShowForbiddenDownload(DownloadForbiddenException error)
        {
            catalogStatus.Text = "Download blocked (HTTP 403)";
            var message = error.Message + " Try the original download link in your browser." +
                Environment.NewLine + Environment.NewLine + error.Url;
            if (LauncherDialogs.OfferBrowser(this, message, "Download blocked"))
                Attempt(() => Process.Start(new ProcessStartInfo(error.Url) { UseShellExecute = true }));
        }

        private async Task LaunchCatalogProgram()
        {
            var program = catalogList.SelectedItem as CatalogProgram;
            if (catalogBusy || launching || (program?.Downloads.Length ?? 0) == 0) return;
            var emulator = store.Data.FindEmulator(store.Data.StartEmulatorId);
            var source = emulator == null ? null : store.Data.StartOptions;
            if (emulator == null && store.Data.Emulators.Count == 1)
            {
                emulator = store.Data.Emulators[0];
                source = store.Data.DefaultProfile(emulator);
            }
            if (emulator == null)
            {
                LauncherDialogs.Message(this, MissingLaunchSetup(new ProgramEntry()), "Download and launch");
                return;
            }

            var package = SelectCatalogDownload(program);
            if (package == null) return;
            var options = Profile.CopyOptions(source, emulator);
            options.AutoRun = true;
            launching = true;
            RefreshPreview();
            SetCatalogBusy(true, "Preparing download…");
            Request request = null;
            try
            {
                var path = await Catalog.Download(program, package,
                    new Progress<string>(message => catalogStatus.Text = message), SelectCatalogPrg);
                if (path == null)
                {
                    catalogStatus.Text = "Download canceled";
                    return;
                }
                request = Launch.Prepare(emulator, options, path, true);
                catalogStatus.Text = "Starting emulator…";
                await StartPreparedProgram(request, message => catalogStatus.Text = message);
            }
            catch (DownloadForbiddenException error)
            {
                ShowForbiddenDownload(error);
            }
            catch (Exception error)
            {
                catalogStatus.Text = request == null ? "Could not prepare the program" : "Launch failed";
                var details = request == null ? error.Message :
                    error.Message + Environment.NewLine + Environment.NewLine + CommandPreview(request);
                LauncherDialogs.Message(this, details, "Download and launch");
            }
            finally
            {
                launching = false;
                SetCatalogBusy(false);
                RefreshPreview();
            }
        }
    }
}
