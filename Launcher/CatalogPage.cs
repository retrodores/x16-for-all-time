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
        private readonly Label catalogCount = new Label { AutoSize = true, Margin = new Padding(12, 4, 3, 3) };
        private readonly TreeView catalogTree = new TreeView { Dock = DockStyle.Fill, HideSelection = false };
        private readonly TextBox catalogDetails = BrowserDetails();
        private readonly Button refreshCatalog = Button("Refresh from X16 Files");
        private readonly Button openProject = Button("Open project page");
        private readonly Button installProgram = Button("Install to Start");
        private readonly Button downloadProgram = Button("Download and launch");
        private readonly Label catalogStatus = BrowserStatus();
        private readonly ProgressBar catalogActivity = BrowserActivity();
        private CatalogProgram[] catalogPrograms;
        private bool catalogBusy;

        private static TextBox BrowserDetails() => new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            AccessibleName = "Selected program details"
        };

        private static Label BrowserStatus() => new Label
        {
            Dock = DockStyle.Fill, AutoEllipsis = true, Height = 24, ForeColor = Navy
        };

        private static ProgressBar BrowserActivity() => new ProgressBar
        {
            Size = new Size(86, 16), Margin = new Padding(4, 7, 4, 0), Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 25, Visible = false, AccessibleName = "Download activity"
        };

        private static TableLayoutPanel BrowserSearch(TextBox search, Button refresh)
        {
            var clear = Button("Clear", () =>
            {
                search.Clear();
                search.Focus();
            });
            clear.AccessibleName = "Clear program search";
            clear.Enabled = search.Enabled && search.TextLength > 0;
            search.TextChanged += (sender, args) => clear.Enabled = search.Enabled && search.TextLength > 0;
            search.EnabledChanged += (sender, args) => clear.Enabled = search.Enabled && search.TextLength > 0;
            return Pair("Find a program", search, clear, refresh);
        }

        private static FlowLayoutPanel BrowserTreeActions(TreeView tree, Control filter, Label count)
        {
            var expand = Button("Expand all", tree.ExpandAll);
            var collapse = Button("Collapse all", () =>
            {
                var selected = tree.SelectedNode;
                while (selected?.Parent != null) selected = selected.Parent;
                tree.CollapseAll();
                tree.SelectedNode = selected;
            });
            tree.EnabledChanged += (sender, args) => expand.Enabled = collapse.Enabled = tree.Enabled;
            return Flow(filter, count, expand, collapse);
        }

        private void ShowInstalledEntry(string id, TabControl tabs, TabPage start, string source)
        {
            if (id == null) return;
            search.Clear();
            tabs.SelectedTab = start;
            RefreshPrograms(id);
            var added = store.Data.Programs.FirstOrDefault(entry => entry.Id == id);
            launchStatus.Text = added?.PreviousInstall != null && source == "X16 Files"
                ? "Reinstalled. See the program menu for the previous copy."
                : "Added to Start from " + source;
        }

        private void BuildCatalogPage(TabPage page, TabControl tabs, TabPage start)
        {
            var body = PageBody(page);
            catalogSearch.AccessibleName = "Find a catalog program";
            catalogTree.AccessibleName = "X16 Files programs";
            AddRow(body, BrowserSearch(catalogSearch, refreshCatalog));
            AddRow(body, BrowserTreeActions(catalogTree, downloadableOnly, catalogCount));
            AddRow(body, catalogTree, SizeType.Percent, 100);
            AddRow(body, catalogDetails, SizeType.Absolute, 112);
            var correction = Button("Report listing correction…", () => Attempt(() =>
            {
                var program = (CatalogProgram)catalogTree.SelectedNode?.Tag;
                if (program != null) OpenReport("https://github.com/retrodores/x16files.com", "Listing correction", program.Id);
            }));
            correction.Enabled = false;
            catalogTree.AfterSelect += (sender, args) => correction.Enabled = catalogTree.SelectedNode?.Tag is CatalogProgram;
            AddRow(body, Flow(openProject, installProgram, downloadProgram, correction, catalogActivity));
            AddRow(body, catalogStatus);
            WrapText(catalogStatus, body);
            UpdateCatalogSelection();

            tabs.SelectedIndexChanged += async (sender, args) =>
            {
                if (tabs.SelectedTab == page && catalogPrograms == null) await LoadCatalog();
            };
            refreshCatalog.Click += async (sender, args) => await LoadCatalog();
            catalogSearch.TextChanged += (sender, args) => ShowCatalogPrograms();
            downloadableOnly.CheckedChanged += (sender, args) => ShowCatalogPrograms();
            catalogTree.AfterSelect += (sender, args) => UpdateCatalogSelection();
            catalogTree.NodeMouseDoubleClick += (sender, args) =>
            {
                if (args.Button != MouseButtons.Left || !(args.Node.Tag is CatalogProgram)) return;
                catalogTree.SelectedNode = args.Node;
                installProgram.PerformClick();
            };
            openProject.Click += (sender, args) => Attempt(() =>
            {
                var program = (CatalogProgram)catalogTree.SelectedNode.Tag;
                Process.Start(new ProcessStartInfo(program.PageUrl) { UseShellExecute = true });
            });
            installProgram.Click += async (sender, args) =>
                ShowInstalledEntry(await InstallCatalogProgram(), tabs, start, "X16 Files");
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
                catalogStatus.Text = "";
            }
            catch (Exception error)
            {
                catalogStatus.Text = catalogPrograms == null
                    ? "X16 Files catalog unavailable. Try refreshing again."
                    : "Refresh failed. Showing the previously loaded catalog.";
                if (catalogPrograms == null)
                    catalogDetails.Text = error.Message;
            }
            finally { SetCatalogBusy(false); }
        }

        private void ShowCatalogPrograms()
        {
            if (catalogPrograms == null) return;
            var id = (catalogTree.SelectedNode?.Tag as CatalogProgram)?.Id;
            var filter = catalogSearch.Text.Trim();
            var entries = catalogPrograms
                .Where(program => !downloadableOnly.Checked || program.Downloads.Length > 0)
                .Where(program => new[] { program.ToString(), program.Category, program.Author?.Name }
                    .Any(value => value?.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0))
                .OrderBy(program => program.ToString(), StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(program => program.Id, StringComparer.Ordinal).ToArray();
            catalogCount.Text = entries.Length == catalogPrograms.Length
                ? $"{entries.Length:N0} {(entries.Length == 1 ? "program" : "programs")}"
                : $"{entries.Length:N0} of {catalogPrograms.Length:N0} programs";
            var selectedCategory = catalogTree.SelectedNode?.Level == 0 ? catalogTree.SelectedNode.Name : null;
            var expanded = new System.Collections.Generic.HashSet<string>(catalogTree.Nodes.Cast<TreeNode>()
                .Where(node => node.IsExpanded).Select(node => node.Name), StringComparer.OrdinalIgnoreCase);
            catalogTree.BeginUpdate();
            try
            {
                catalogTree.Nodes.Clear();
                TreeNode selected = null;
                var groups = entries.GroupBy(program => string.IsNullOrWhiteSpace(program.Category)
                    ? "Uncategorized" : program.Category.Trim(), StringComparer.OrdinalIgnoreCase);
                foreach (var group in groups.OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase))
                {
                    var label = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(group.Key);
                    var category = catalogTree.Nodes.Add(group.Key, $"{label} ({group.Count():N0})");
                    if (string.Equals(group.Key, selectedCategory, StringComparison.OrdinalIgnoreCase)) selected = category;
                    foreach (var program in group)
                    {
                        var node = category.Nodes.Add(program.ToString());
                        node.Tag = program;
                        if (program.Id == id) selected = node;
                    }
                    if (filter.Length != 0 || expanded.Contains(group.Key)) category.Expand();
                }
                if (selected != null)
                {
                    selected.EnsureVisible();
                    catalogTree.SelectedNode = selected;
                }
                else if (catalogTree.Nodes.Count > 0)
                    catalogTree.SelectedNode = catalogTree.Nodes[0];
            }
            finally { catalogTree.EndUpdate(); }
            UpdateCatalogSelection();
        }

        private void UpdateCatalogSelection()
        {
            var program = catalogTree.SelectedNode?.Tag as CatalogProgram;
            openProject.Enabled = !catalogBusy && Catalog.IsWebUrl(program?.PageUrl);
            downloadProgram.Enabled = !catalogBusy && !launching && (program?.Downloads.Length ?? 0) > 0;
            installProgram.Enabled = downloadProgram.Enabled;
            installProgram.Visible = downloadProgram.Visible = (program?.Downloads.Length ?? 0) > 0;
            installProgram.Text = program != null && store.Data.Programs.Any(entry => entry.CatalogId == program.Id)
                ? "Reinstall to Start" : "Install to Start";
            if (program == null)
            {
                if (catalogPrograms != null)
                    catalogDetails.Text = catalogTree.SelectedNode == null ? "No matching programs." : "";
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
            refreshCatalog.Enabled = catalogSearch.Enabled = downloadableOnly.Enabled = catalogTree.Enabled = !busy;
            if (message != null) catalogStatus.Text = message;
            UpdateCatalogSelection();
        }

        private CatalogPackage SelectCatalogDownload(CatalogProgram program)
        {
            var downloads = program.Downloads;
            if (downloads.Length == 1) return downloads[0];
            var selected = LauncherDialogs.SelectItem(this, "Choose a download", downloads.Select(item => item.Url).ToArray());
            return selected < 0 ? null : downloads[selected];
        }

        private string SelectCatalogPrg(string[] paths)
        {
            var selected = LauncherDialogs.SelectItem(this, "Choose the startup PRG", paths);
            return selected < 0 ? null : paths[selected];
        }

        private async Task<string> InstallCatalogProgram()
        {
            var program = catalogTree.SelectedNode?.Tag as CatalogProgram;
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
                        entry = new ProgramEntry { CatalogId = program.Id, Name = data.UniqueProgramName(program.ToString(), program.Version) };
                        // Use the same default emulator fallback as a catalog launch.
                        if (data.FindEmulator(data.StartEmulatorId) == null && data.Emulators.Count == 1)
                        {
                            var emulator = data.Emulators[0];
                            entry.EmulatorId = emulator.Id;
                            entry.Options = Profile.Snapshot(data.DefaultProfile(emulator), emulator);
                        }
                        data.Programs.Add(entry);
                    }
                    entry.RememberInstallation(path);
                    entry.Path = path;
                    entry.Source = new ProgramSource
                    {
                        Title = program.ToString(), Author = program.Author?.Name,
                        Version = program.Version, Url = program.PageUrl
                    };
                    id = entry.Id;
                });
                catalogStatus.Text = "Installed " + program + " and added it to Start.";
                return id;
            }
            catch (DownloadForbiddenException error)
            {
                ShowForbiddenDownload(error, catalogStatus);
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

        private void ShowForbiddenDownload(DownloadForbiddenException error, Label status)
        {
            status.Text = "Download blocked (HTTP 403)";
            var message = error.Message + " Try the original download link in your browser." +
                Environment.NewLine + Environment.NewLine + error.Url;
            if (LauncherDialogs.OfferBrowser(this, message, "Download blocked"))
                Attempt(() => Process.Start(new ProcessStartInfo(error.Url) { UseShellExecute = true }));
        }

        private async Task LaunchCatalogProgram()
        {
            var program = catalogTree.SelectedNode?.Tag as CatalogProgram;
            if (catalogBusy || launching || (program?.Downloads.Length ?? 0) == 0) return;
            var installedId = store.Data.Programs.FirstOrDefault(entry => entry.CatalogId == program.Id)?.Id;
            await LaunchFromBrowser(async progress =>
            {
                var package = SelectCatalogDownload(program);
                if (package == null) return (null, null);
                var path = await Catalog.Download(program, package, progress, SelectCatalogPrg);
                return (path, null);
            }, SetCatalogBusy, catalogStatus, installedId);
        }

        private async Task LaunchFromBrowser(
            Func<IProgress<string>, Task<(string Path, string Root)>> getProgram,
            Action<bool, string> setBusy, Label status, string installedId)
        {
            var installed = store.Data.Programs.FirstOrDefault(entry => entry.Id == installedId);
            var emulator = store.Data.ResolveLaunch(installed, out var source);
            if (emulator == null)
            {
                LauncherDialogs.Message(this, MissingLaunchSetup(), "Download and launch");
                return;
            }

            var options = Profile.CopyOptions(source, emulator);
            options.AutoRun = true;
            launching = true;
            RefreshPreview();
            setBusy(true, "Preparing download…");
            Request request = null;
            try
            {
                var program = await getProgram(new Progress<string>(message => status.Text = message));
                if (program.Path == null)
                {
                    status.Text = "Download canceled";
                    return;
                }

                request = Launch.Prepare(emulator, options, program.Path, true, program.Root);
                status.Text = "Starting emulator…";
                await StartPreparedProgram(request, message => status.Text = message);
                try
                {
                    if (RepairStartEmulator(installedId, emulator, options, program.Path, program.Root))
                        status.Text = "Emulator launched. The Start entry was updated.";
                }
                catch (Exception error)
                {
                    status.Text = "Emulator launched. Could not update the Start entry.";
                    LauncherDialogs.Message(this, error.Message, "Update Start entry");
                }
            }
            catch (DownloadForbiddenException error)
            {
                ShowForbiddenDownload(error, status);
            }
            catch (Exception error)
            {
                status.Text = request == null ? "Could not prepare the program" : "Launch failed";
                var details = request == null ? error.Message :
                    error.Message + Environment.NewLine + Environment.NewLine + CommandPreview(request);
                LauncherDialogs.Message(this, details, "Download and launch");
            }
            finally
            {
                launching = false;
                setBusy(false, null);
                RefreshPreview();
            }
        }

        private bool RepairStartEmulator(string installedId, Emulator emulator, Profile options, string path, string root)
        {
            if (installedId == null) return false;
            var installed = store.Data.Programs.FirstOrDefault(entry => entry.Id == installedId);
            if (installed == null || store.Data.ConfiguredLaunchEmulator(installed) != null ||
                store.Data.FindEmulator(emulator.Id) == null) return false;
            bool repairAssignment = !store.Data.UsesStartDefaults(installed);
            bool repairPath = !File.Exists(installed.Path);
            if (!repairAssignment && !repairPath) return false;

            store.Save(data =>
            {
                var entry = data.Programs.Single(item => item.Id == installedId);
                if (repairAssignment)
                {
                    entry.EmulatorId = emulator.Id;
                    entry.Options = Profile.Snapshot(options, emulator);
                    entry.RequiresEmulatorSelection = false;
                    entry.ProfileId = null;
                }
                if (repairPath)
                {
                    entry.Path = path;
                    entry.SdKitRoot = root;
                }
            });
            RefreshPrograms();
            return true;
        }
    }
}
