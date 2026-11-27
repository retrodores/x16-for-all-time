using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CommanderX16
{
    [DataContract]
    internal sealed class RunHistoryEntry
    {
        [DataMember] public DateTime Started;
        [DataMember] public string Name;
        [DataMember] public string Mode;
        [DataMember] public string Path;
        [DataMember] public Request Command;
        [DataMember] public Emulator Emulator;
        [DataMember] public Profile Options;
        [DataMember(EmitDefaultValue = false)] public string CatalogId;
        [DataMember(EmitDefaultValue = false)] public CatalogPackage Package;
        [DataMember(EmitDefaultValue = false)] public string EntryPoint;
        [DataMember(EmitDefaultValue = false)] public string KitPath;
        [DataMember(EmitDefaultValue = false)] public string KitRevision;
        [DataMember(EmitDefaultValue = false)] public string KitRoot;
        [DataMember(EmitDefaultValue = false)] public string LocalArchive;

        public string Source => CatalogId != null ? "X16 Files" : KitPath != null ? "SD Card Kit" : "Local";
        public bool CanDownload => CatalogId != null || KitPath != null;

        public RunHistoryEntry WithSettings(Emulator emulator, Profile options)
        {
            Emulator = Json.Read<Emulator>(Json.Write(emulator));
            Options = Profile.CopyOptions(options, emulator);
            return this;
        }

        public RunHistoryEntry ForReplay()
        {
            var replay = (RunHistoryEntry)MemberwiseClone();
            replay.Mode = "History";
            return replay;
        }

        public static string RelativeEntry(string root, string path)
        {
            string directory = System.IO.Path.GetFullPath(root).TrimEnd(System.IO.Path.DirectorySeparatorChar) +
                System.IO.Path.DirectorySeparatorChar;
            string file = System.IO.Path.GetFullPath(path);
            return file.StartsWith(directory, StringComparison.OrdinalIgnoreCase)
                ? file.Substring(directory.Length).Replace('\\', '/') : null;
        }
    }

    internal sealed partial class MainForm
    {
        private readonly ListView historyList = new ListView
        {
            Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false,
            HideSelection = false, AccessibleName = "Launch history"
        };
        private readonly TextBox historyDetails = new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
            WordWrap = false, ScrollBars = ScrollBars.Horizontal, AccessibleName = "Recorded launch command"
        };
        private readonly Button replayHistory = Button("Run again");
        private readonly Label historyStatus = new Label { AutoSize = true };

        private RunHistoryEntry SelectedHistory => historyList.SelectedItems.Count == 1
            ? historyList.SelectedItems[0].Tag as RunHistoryEntry : null;

        private void BuildHistoryPage(TabPage page)
        {
            var body = PageBody(page);
            historyList.Columns.Add("Program", 160);
            historyList.Columns.Add("Run time", 150);
            historyList.Columns.Add("Source", 140);
            historyList.Columns.Add("Emulator", 125);
            historyList.Columns.Add("MHz", 60);
            historyList.Columns.Add("RAM", 75);
            historyList.Columns.Add("CPU", 65);
            AddRow(body, historyList, SizeType.Percent, 100);
            AddRow(body, historyDetails, SizeType.Absolute, 83);
            AddRow(body, Flow(replayHistory, historyStatus));
            historyList.SelectedIndexChanged += (sender, args) => UpdateHistorySelection();
            historyList.ItemActivate += async (sender, args) => await ReplayHistory(SelectedHistory);
            replayHistory.Click += async (sender, args) => await ReplayHistory(SelectedHistory);
            pages.SelectedIndexChanged += (sender, args) => { if (pages.SelectedTab == page) RefreshHistory(); };
            UpdateHistorySelection();
        }

        private void RefreshHistory()
        {
            historyList.BeginUpdate();
            try
            {
                historyList.Items.Clear();
                foreach (var entry in (store.Data.History ?? new List<RunHistoryEntry>()).AsEnumerable().Reverse())
                {
                    string args = entry.Command.Arguments;
                    var row = new ListViewItem(new[]
                    {
                        entry.Name, entry.Started.ToLocalTime().ToString("g"), entry.Mode + " · " + entry.Source,
                        entry.Emulator.Name, Launch.OptionValue(args, "-mhz") ?? "Default",
                        Launch.OptionValue(args, "-ram", " KiB") ?? "Default",
                        Launch.HasExtraOption(args, "-c816") ? "65C816" :
                            Launch.HasExtraOption(args, "-c02") ? "65C02" : "Default"
                    }) { Tag = entry };
                    historyList.Items.Add(row);
                }
                if (historyList.Items.Count > 0) historyList.Items[0].Selected = true;
            }
            finally { historyList.EndUpdate(); }
            UpdateHistorySelection();
        }

        private void UpdateHistorySelection()
        {
            var entry = SelectedHistory;
            replayHistory.Enabled = !launching && entry != null;
            historyDetails.Text = entry != null ? CommandPreview(entry.Command) :
                historyList.Items.Count == 0 ? "New launches will appear here." : "";
        }

        private void RecordHistory(RunHistoryEntry entry, Request request)
        {
            entry.Started = DateTime.UtcNow;
            entry.Command = request;
            try
            {
                store.Save(data => (data.History ?? (data.History = new List<RunHistoryEntry>())).Add(entry));
                if (pages.SelectedTab?.Text == "History") RefreshHistory();
            }
            catch (Exception error)
            {
                LauncherDialogs.Message(this, error.Message, "Could not save launch history");
            }
        }

        private async Task ReplayHistory(RunHistoryEntry entry)
        {
            if (launching || entry == null) return;
            var replay = entry.ForReplay();
            string extraction = null;
            launching = true;
            RefreshPreview();
            UpdateHistorySelection();
            try
            {
                if (!File.Exists(replay.Path))
                {
                    if (File.Exists(replay.LocalArchive))
                    {
                        extraction = Path.Combine(Path.GetTempPath(), "X16ForAllTime-" + Guid.NewGuid().ToString("N"));
                        historyStatus.Text = "Extracting ZIP…";
                        var files = await Task.Run(() => Catalog.ExtractCandidates(replay.LocalArchive, extraction,
                            replay.EntryPoint, includeBasic: true));
                        replay.Path = Path.Combine(extraction, files[0]);
                    }
                    else if (replay.CanDownload)
                    {
                        if (!LauncherDialogs.Confirm(this, "The local program file is missing. Download it again from " +
                            replay.Source + "?", "Run from History")) return;
                        await RestoreHistoryProgram(replay);
                        if (replay.Path == null)
                        {
                            historyStatus.Text = "Download canceled";
                            return;
                        }
                    }
                    else
                    {
                        LauncherDialogs.Message(this, "The local program file is missing." + Environment.NewLine +
                            (replay.LocalArchive ?? replay.Path), "Run from History");
                        return;
                    }
                }

                var gifDirectory = replay.LocalArchive == null ? null : Path.GetDirectoryName(replay.LocalArchive);
                var request = Launch.Prepare(replay.Emulator, replay.Options, replay.Path, true, replay.KitRoot, gifDirectory);
                historyStatus.Text = "Starting emulator…";
                string launchedDirectory = extraction;
                Action cleanup = launchedDirectory == null ? (Action)null : () => Catalog.DiscardExtraction(launchedDirectory);
                await StartPreparedProgram(request, message => historyStatus.Text = message, cleanup, replay);
                extraction = null;
            }
            catch (DownloadForbiddenException error)
            {
                ShowForbiddenDownload(error, historyStatus);
            }
            catch (Exception error)
            {
                historyStatus.Text = "Could not run program";
                LauncherDialogs.Message(this, error.Message, "Run from History");
            }
            finally
            {
                Catalog.DiscardExtraction(extraction);
                launching = false;
                RefreshPreview();
                UpdateHistorySelection();
            }
        }

        private async Task RestoreHistoryProgram(RunHistoryEntry entry)
        {
            historyStatus.Text = "Preparing download…";
            if (entry.KitPath != null)
            {
                if (!Regex.IsMatch(entry.KitRevision ?? "", @"\A[0-9a-f]{40}\z"))
                    throw new IOException("The SD Card Kit revision for this run is unavailable.");
                var item = new KitItem { Path = entry.KitPath, Type = "blob" };
                (entry.Path, entry.KitRoot) = await GetKitProgram(item, entry.KitRevision, entry.EntryPoint, historyStatus);
                return;
            }

            var program = new CatalogProgram { Id = entry.CatalogId, Name = entry.Name };
            var package = entry.Package;
            if (package == null)
            {
                program = (await Catalog.Fetch()).FirstOrDefault(item => item.Id == entry.CatalogId);
                if (program == null || program.Downloads.Length == 0)
                    throw new IOException("This program no longer has a download in X16 Files.");
                package = SelectCatalogDownload(program);
                if (package == null) { entry.Path = null; return; }
            }
            if (package.Format == "zip" && entry.EntryPoint != null)
                package = new CatalogPackage { Format = package.Format, Url = package.Url, Prg = entry.EntryPoint };
            string previousFile = Path.GetFileName(entry.Path);
            string ChooseStartup(string[] choices)
            {
                var matches = choices.Where(path => Same(Path.GetFileName(path), previousFile)).ToArray();
                return matches.Length == 1 ? matches[0] : SelectCatalogPrg(choices);
            }
            entry.Package = package;
            entry.Path = await Catalog.Download(program, package, new Progress<string>(message => historyStatus.Text = message),
                ChooseStartup, value => entry.EntryPoint = value);
        }
    }
}
