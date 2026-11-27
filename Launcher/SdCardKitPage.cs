using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.IO;
using System.Text.RegularExpressions;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CommanderX16
{
    internal sealed partial class MainForm
    {
        private const string KitRepository = "https://github.com/cx16forum/sdcard";
        private const string KitRoot = "sdcard_root";
        private readonly TreeView kitTree = new TreeView { Dock = DockStyle.Fill, HideSelection = false, AccessibleName = "SD Card Kit folders and files" };
        private readonly TextBox kitSearch = Entry();
        private readonly Label kitStatus = BrowserStatus();
        private readonly Label kitCount = new Label { AutoSize = true, Margin = new Padding(12, 4, 3, 3) };
        private readonly TextBox kitDetails = BrowserDetails();
        private readonly ProgressBar kitActivity = BrowserActivity();
        private readonly Button refreshKit = Button("Refresh from SD Card Kit");
        private readonly Button openKit = Button("Open project page");
        private readonly Button installKitItem = Button("Install to Favorites");
        private readonly Button launchKitItem = Button(RunDownloadLabel);
        private KitItem[] kitItems;
        private string kitRevision;
        private bool loadingKit;

        private void BuildKitPage(TabPage page, TabControl tabs, TabPage start)
        {
            var body = PageBody(page);
            kitSearch.AccessibleName = "Find a program in the SD Card Kit";
            AddRow(body, BrowserSearch(kitSearch, refreshKit));
            var downloadable = new CheckBox { Text = "Downloadable only", AutoSize = true, Checked = true, Enabled = false };
            AddRow(body, BrowserTreeActions(kitTree, downloadable, kitCount));
            AddRow(body, kitTree, SizeType.Percent, 100);
            AddRow(body, kitDetails, SizeType.Absolute, 112);
            AddRow(body, Flow(openKit, installKitItem, launchKitItem, kitActivity));
            AddRow(body, kitStatus);
            WrapText(kitStatus, body);
            refreshKit.Click += async (sender, args) => await LoadKit();
            kitSearch.TextChanged += (sender, args) => ShowKitItems();
            openKit.Click += (sender, args) => OpenKitItem(kitTree.SelectedNode);
            kitTree.AfterSelect += (sender, args) => UpdateKitSelection();
            installKitItem.Click += async (sender, args) =>
                ShowInstalledEntry(await AddKitItemToStart(), tabs, start, "SD Card Kit");
            launchKitItem.Click += async (sender, args) =>
            {
                var item = kitTree.SelectedNode?.Tag as KitItem;
                if (loadingKit || launching || item?.CanInstall != true) return;
                var installedId = store.Data.Programs.FirstOrDefault(entry => entry.SdKitSource == item.Path)?.Id;
                await LaunchFromBrowser(progress => GetKitProgram(item), SetKitBusy, kitStatus, installedId);
            };
            kitTree.NodeMouseDoubleClick += (sender, args) =>
            {
                if (args.Button != MouseButtons.Left || !(args.Node.Tag is KitItem item) || !item.CanInstall) return;
                kitTree.SelectedNode = args.Node;
                launchKitItem.PerformClick();
            };
            UpdateKitSelection();
            tabs.SelectedIndexChanged += async (sender, args) =>
            {
                if (tabs.SelectedTab == page && kitItems == null) await LoadKit();
            };
        }

        private async Task LoadKit()
        {
            if (loadingKit) return;
            SetKitBusy(true, "Loading the SD Card Kit…");
            try
            {
                var snapshot = await PowerShell.Execute<KitSnapshot>(new Request { Action = "SdCardTree" });
                if (snapshot?.Tree == null || snapshot.Truncated ||
                    !Regex.IsMatch(snapshot.Revision ?? "", @"\A[0-9a-f]{40}\z"))
                    throw new InvalidOperationException("GitHub returned an incomplete file tree.");
                kitItems = snapshot.Tree.Where(item => item.Path?.StartsWith(KitRoot + "/", StringComparison.Ordinal) == true &&
                    item.CanInstall).ToArray();
                kitRevision = snapshot.Revision;
                ShowKitItems();
                kitStatus.Text = "";
            }
            catch (Exception error)
            {
                kitStatus.Text = kitItems == null ? "SD Card Kit unavailable. Try refreshing again." :
                    "Refresh failed. Showing the previously loaded kit.";
                if (kitItems == null) kitDetails.Text = error.Message;
            }
            finally { SetKitBusy(false, null); }
        }

        private void ShowKitItems()
        {
            if (kitItems == null) return;
            var filter = kitSearch.Text.Trim();
            var selectedPath = kitTree.SelectedNode?.Name;
            var expanded = new HashSet<string>(kitTree.Nodes.Cast<TreeNode>()
                .SelectMany(ExpandedKitNodes).Select(node => node.Name), StringComparer.Ordinal);
            var visible = kitItems.Where(item => item.Path.Substring(KitRoot.Length + 1)
                .IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase).ToArray();
            var nodes = new Dictionary<string, TreeNode>(StringComparer.Ordinal);
            kitTree.BeginUpdate();
            try
            {
                kitTree.Nodes.Clear();
                foreach (var item in visible)
                    AddKitNode(item.Path, nodes).Tag = item;
                foreach (var node in nodes.Values)
                    if (filter.Length != 0 || expanded.Contains(node.Name)) node.Expand();
                if (selectedPath != null && nodes.TryGetValue(selectedPath, out var selected))
                {
                    selected.EnsureVisible();
                    kitTree.SelectedNode = selected;
                }
                else if (kitTree.Nodes.Count > 0) kitTree.SelectedNode = kitTree.Nodes[0];
            }
            finally { kitTree.EndUpdate(); }

            kitCount.Text = visible.Length == kitItems.Length
                ? $"{visible.Length:N0} {(visible.Length == 1 ? "program" : "programs")}"
                : $"{visible.Length:N0} of {kitItems.Length:N0} programs";
            UpdateKitSelection();
        }

        private void SetKitBusy(bool busy, string message)
        {
            loadingKit = busy;
            kitActivity.Visible = busy;
            refreshKit.Enabled = kitSearch.Enabled = kitTree.Enabled = !busy;
            if (message != null) kitStatus.Text = message;
            UpdateKitSelection();
        }

        private void UpdateKitSelection()
        {
            var item = kitTree.SelectedNode?.Tag as KitItem;
            bool supported = item?.CanInstall == true;
            openKit.Enabled = !loadingKit && kitTree.SelectedNode != null;
            installKitItem.Enabled = launchKitItem.Enabled = !loadingKit && !launching && supported;
            installKitItem.Visible = launchKitItem.Visible = supported;
            installKitItem.Text = supported && store.Data.Programs.Any(entry => entry.SdKitSource == item.Path)
                ? "Reinstall to Favorites" : "Install to Favorites";
            if (kitItems != null)
                kitDetails.Text = supported ? kitTree.SelectedNode.Text + Environment.NewLine +
                    item.Path.Substring(KitRoot.Length + 1) :
                    kitTree.SelectedNode?.Name.Substring(KitRoot.Length + 1) ?? "No matching programs.";
        }

        private async Task<string> AddKitItemToStart()
        {
            var item = kitTree.SelectedNode?.Tag as KitItem;
            if (loadingKit || launching || item?.CanInstall != true) return null;
            string extracted = null;
            SetKitBusy(true, "Preparing installation…");
            try
            {
                var (path, root) = await GetKitProgram(item);
                if (path == null)
                {
                    kitStatus.Text = "Installation canceled.";
                    return null;
                }
                if (item.IsZip) extracted = root;
                kitStatus.Text = "Adding program to Favorites…";
                string id = null;
                store.Save(data =>
                {
                    var entry = data.Programs.FirstOrDefault(program => program.SdKitSource == item.Path);
                    if (entry == null)
                    {
                        entry = new ProgramEntry { SdKitSource = item.Path, Name = data.UniqueProgramName(Path.GetFileNameWithoutExtension(path)) };
                        if (data.FindEmulator(data.StartEmulatorId) == null && data.Emulators.Count == 1)
                        {
                            var emulator = data.Emulators[0];
                            entry.EmulatorId = emulator.Id;
                            entry.Options = Profile.Snapshot(data.DefaultProfile(emulator), emulator);
                        }
                        data.Programs.Add(entry);
                    }

                    entry.Path = path;
                    entry.SdKitRoot = root;
                    entry.Source = new ProgramSource
                    {
                        Title = item.Path.Substring(KitRoot.Length + 1),
                        Version = kitRevision,
                        Url = KitRepository + "/blob/" + kitRevision + "/" +
                            string.Join("/", item.Path.Split('/').Select(Uri.EscapeDataString))
                    };
                    id = entry.Id;
                });
                extracted = null;
                kitStatus.Text = "Installed " + Path.GetFileNameWithoutExtension(path) + " and added it to Favorites.";
                return id;
            }
            catch (DownloadForbiddenException error)
            {
                ShowForbiddenDownload(error, kitStatus);
                return null;
            }
            catch (Exception error)
            {
                kitStatus.Text = "Installation failed.";
                LauncherDialogs.Message(this, error.Message, "Add to Favorites");
                return null;
            }
            finally
            {
                SetKitBusy(false, null);
                if (extracted != null && Directory.Exists(extracted)) Directory.Delete(extracted, true);
            }
        }

        private async Task<(string Path, string Root)> GetKitProgram(KitItem item)
        {
            var root = await EnsureKitSnapshot();
            var path = Path.Combine(root, item.Path.Substring(KitRoot.Length + 1).Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path)) await RestoreKitFile(item, path);
            if (!item.IsZip) return (path, root);

            kitStatus.Text = "Extracting program ZIP…";
            var extracted = Path.Combine(SettingsStore.Home, "SdCardKit", "Packages", Guid.NewGuid().ToString("N"));
            try
            {
                string[] choices = await Task.Run(() => Catalog.ExtractCandidates(path, extracted, null, includeBasic: true));
                int selected = choices.Length == 1 ? 0 : LauncherDialogs.SelectItem(this, "Choose the startup program", choices);
                if (selected < 0) return (null, null);
                root = extracted;
                extracted = null;
                return (Path.Combine(root, choices[selected]), root);
            }
            finally
            {
                if (extracted != null && Directory.Exists(extracted)) Directory.Delete(extracted, true);
            }
        }

        private async Task RestoreKitFile(KitItem item, string path)
        {
            kitStatus.Text = "Downloading the missing program file…";
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await PowerShell.Execute(new Request
                {
                    Action = "Download",
                    Url = "https://raw.githubusercontent.com/cx16forum/sdcard/" + kitRevision + "/" +
                        string.Join("/", item.Path.Split('/').Select(Uri.EscapeDataString)),
                    Destination = temporary
                });
                File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private async Task<string> EnsureKitSnapshot()
        {
            var parent = Path.Combine(SettingsStore.Home, "SdCardKit");
            var revision = kitRevision;
            var destination = Path.Combine(parent, revision);
            if (Directory.Exists(destination))
            {
                kitStatus.Text = "Preparing program from the installed kit…";
                return destination;
            }

            var stage = Path.Combine(parent, "." + Guid.NewGuid().ToString("N"));
            var archive = Path.Combine(stage, "kit.zip");
            var files = Path.Combine(stage, "files");
            Directory.CreateDirectory(stage);
            try
            {
                kitStatus.Text = "Downloading the SD Card Kit for this revision…";
                await PowerShell.Execute(new Request
                {
                    Action = "SdCardArchive",
                    Url = "https://codeload.github.com/cx16forum/sdcard/zip/" + revision,
                    Destination = archive
                });

                kitStatus.Text = "Extracting the SD Card Kit…";
                var extracted = await Task.Run(() => Catalog.ExtractArchive(archive, files, "sdcard-" + revision + "/" + KitRoot + "/"));
                if (extracted.Count == 0)
                    throw new IOException("The archive does not contain the SD Card Kit.");
                Directory.Move(files, destination);
                return destination;
            }
            finally { Directory.Delete(stage, true); }
        }

        private static IEnumerable<TreeNode> ExpandedKitNodes(TreeNode node)
        {
            if (!node.IsExpanded) yield break;
            yield return node;
            foreach (TreeNode child in node.Nodes)
                foreach (var descendant in ExpandedKitNodes(child))
                    yield return descendant;
        }

        private TreeNode AddKitNode(string path, Dictionary<string, TreeNode> nodes)
        {
            if (nodes.TryGetValue(path, out var existing)) return existing;
            var slash = path.LastIndexOf('/');
            var parent = path.Substring(0, slash);
            var siblings = parent == KitRoot ? kitTree.Nodes : AddKitNode(parent, nodes).Nodes;
            var node = siblings.Add(path, path.Substring(slash + 1));
            nodes.Add(path, node);
            return node;
        }

        private void OpenKitItem(TreeNode node)
        {
            var path = node?.Name ?? KitRoot;
            var segment = node?.Tag is KitItem item && item.Type == "blob" ? "blob" : "tree";
            var url = KitRepository + "/" + segment + "/" + (kitRevision ?? "main") + "/" +
                string.Join("/", path.Split('/').Select(Uri.EscapeDataString));
            Attempt(() => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }));
        }

        [DataContract]
        private sealed class KitSnapshot
        {
            [DataMember(Name = "revision")] public string Revision { get; set; }
            [DataMember(Name = "tree")] public KitItem[] Tree { get; set; }
            [DataMember(Name = "truncated")] public bool Truncated { get; set; }
        }

        [DataContract]
        private sealed class KitItem
        {
            [DataMember(Name = "path")] public string Path { get; set; }
            [DataMember(Name = "type")] public string Type { get; set; }
            public bool IsZip => string.Equals(System.IO.Path.GetExtension(Path), ".zip", StringComparison.OrdinalIgnoreCase);
            public bool CanInstall => Type == "blob" && (IsZip || Launch.IsProgramFile(Path));
        }
    }
}
