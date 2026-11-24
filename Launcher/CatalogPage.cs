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
        private readonly ListBox catalogList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        private readonly TextBox catalogDetails = new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            AccessibleName = "Selected program details"
        };
        private readonly Button refreshCatalog = Button("Refresh");
        private readonly Button openProject = Button("Open project page");
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

        private void BuildCatalogPage(TabPage page, TabControl tabs)
        {
            var body = PageBody(page);
            catalogSearch.AccessibleName = "Find a catalog program";
            catalogList.AccessibleName = "X16 Files programs";
            AddRow(body, Pair("Find a program", catalogSearch, refreshCatalog));
            AddRow(body, catalogList, SizeType.Percent, 100);
            AddRow(body, catalogDetails, SizeType.Absolute, 112);
            AddRow(body, Flow(openProject, downloadProgram, catalogActivity));
            AddRow(body, catalogStatus);
            UpdateCatalogSelection();

            tabs.SelectedIndexChanged += async (sender, args) =>
            {
                if (tabs.SelectedTab == page && catalogPrograms == null) await LoadCatalog();
            };
            refreshCatalog.Click += async (sender, args) => await LoadCatalog();
            catalogSearch.TextChanged += (sender, args) => ShowCatalogPrograms();
            catalogList.SelectedIndexChanged += (sender, args) => UpdateCatalogSelection();
            openProject.Click += (sender, args) => Attempt(() =>
            {
                var program = (CatalogProgram)catalogList.SelectedItem;
                Process.Start(new ProcessStartInfo(program.PageUrl) { UseShellExecute = true });
            });
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
            var entries = catalogPrograms.Where(program => new[] { program.ToString(), program.Category, program.Author?.Name }
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
            downloadProgram.Enabled = !catalogBusy && !launching && program?.Package?.Supported == true;
            downloadProgram.Visible = program?.Package?.Supported == true;
            if (program == null)
            {
                if (catalogPrograms != null) catalogDetails.Text = "No matching programs.";
                return;
            }
            var metadata = string.Join(" · ", new[] { program.Category, program.Author?.Name, program.Version }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            catalogDetails.Text = string.Join(Environment.NewLine, new[] { program.ToString(), metadata, program.Description,
                program.Package?.Supported == true ? null : "No verified download available." }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        private void SetCatalogBusy(bool busy, string message = null)
        {
            catalogBusy = busy;
            catalogActivity.Visible = busy;
            refreshCatalog.Enabled = catalogSearch.Enabled = catalogList.Enabled = !busy;
            if (message != null) catalogStatus.Text = message;
            UpdateCatalogSelection();
        }

        private async Task LaunchCatalogProgram()
        {
            var program = catalogList.SelectedItem as CatalogProgram;
            if (catalogBusy || launching || program?.Package?.Supported != true) return;
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

            var options = Profile.CopyOptions(source, emulator);
            options.AutoRun = true;
            launching = true;
            RefreshPreview();
            SetCatalogBusy(true, "Preparing download…");
            Request request = null;
            try
            {
                var path = await Catalog.Download(program, new Progress<string>(message => catalogStatus.Text = message));
                request = Launch.Prepare(emulator, options, path, true);
                catalogStatus.Text = "Starting emulator…";
                await StartPreparedProgram(request, message => catalogStatus.Text = message);
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
