using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        private readonly Label kitStatus = new Label { AutoSize = true };
        private readonly Label kitCount = new Label { AutoSize = true };
        private readonly Button refreshKit = Button("Refresh from GitHub");
        private readonly Button openKit = Button("Open on GitHub");
        private readonly Button installKitItem = Button("Add to Start");
        private KitItem[] kitItems;
        private string kitRevision;
        private bool loadingKit;

        private void BuildKitPage(TabPage page, TabControl tabs, TabPage start)
        {
            var body = PageBody(page);
            kitSearch.AccessibleName = "Find a file or folder";
            AddRow(body, Pair("Find a file or folder", kitSearch, refreshKit));
            AddRow(body, kitCount);
            AddRow(body, kitTree, SizeType.Percent, 100);
            AddRow(body, Flow(openKit, installKitItem));
            AddRow(body, kitStatus);
            refreshKit.Click += async (sender, args) => await LoadKit();
            kitSearch.TextChanged += (sender, args) => ShowKitItems();
            openKit.Click += (sender, args) => OpenKitItem(kitTree.SelectedNode);
            kitTree.AfterSelect += (sender, args) => UpdateKitSelection();
            installKitItem.Click += async (sender, args) =>
            {
                var id = await AddKitItemToStart();
                if (id == null) return;
                search.Clear();
                RefreshPrograms(id);
                tabs.SelectedTab = start;
                launchStatus.Text = "Added from SD Card Kit";
            };
            kitTree.NodeMouseDoubleClick += (sender, args) =>
            {
                if (args.Button != MouseButtons.Left || !(args.Node.Tag is KitItem item) || item.Type != "blob") return;
                if (item.CanInstall)
                {
                    kitTree.SelectedNode = args.Node;
                    installKitItem.PerformClick();
                }
                else OpenKitItem(args.Node);
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
            loadingKit = true;
            UpdateKitSelection();
            kitStatus.Text = "Loading the SD Card Kit…";
            try
            {
                var snapshot = await PowerShell.Execute<KitSnapshot>(new Request { Action = "SdCardTree" });
                if (snapshot?.Tree == null || snapshot.Truncated ||
                    !Regex.IsMatch(snapshot.Revision ?? "", @"\A[0-9a-f]{40}\z"))
                    throw new InvalidOperationException("GitHub returned an incomplete file tree.");
                var items = snapshot.Tree.Where(item => item.Path?.StartsWith(KitRoot + "/", StringComparison.Ordinal) == true &&
                    (item.Type == "tree" || item.Type == "blob")).ToArray();
                if (items.Length == 0)
                    throw new InvalidOperationException("GitHub returned no files under " + KitRoot + ".");
                kitItems = items;
                kitRevision = snapshot.Revision;
                ShowKitItems();
                kitStatus.Text = "";
            }
            catch (Exception error)
            {
                kitStatus.Text = (kitItems == null ? "Could not load the kit. " :
                    "Refresh failed. Showing the previously loaded kit. ") + error.Message;
            }
            finally
            {
                loadingKit = false;
                UpdateKitSelection();
            }
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

            int count = visible.Count(item => item.Type == "blob");
            int total = kitItems.Count(item => item.Type == "blob");
            kitCount.Text = filter.Length == 0 ? $"{total:N0} files" : $"{count:N0} of {total:N0} files";
            UpdateKitSelection();
        }

        private void UpdateKitSelection()
        {
            var item = kitTree.SelectedNode?.Tag as KitItem;
            refreshKit.Enabled = !loadingKit;
            installKitItem.Enabled = !loadingKit && !launching && item?.CanInstall == true;
            installKitItem.Text = item != null && store.Data.Programs.Any(entry => entry.SdKitSource == item.Path)
                ? "Reinstall to Start" : "Add to Start";
        }

        private async Task<string> AddKitItemToStart()
        {
            var item = kitTree.SelectedNode?.Tag as KitItem;
            if (loadingKit || launching || item?.CanInstall != true) return null;
            string extracted = null;
            loadingKit = true;
            UpdateKitSelection();
            try
            {
                var root = await EnsureKitSnapshot();
                var path = Path.Combine(root, item.Path.Substring(KitRoot.Length + 1).Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path))
                    throw new IOException("The program file is missing from the downloaded SD Card Kit.");

                if (item.IsZip)
                {
                    kitStatus.Text = "Extracting program ZIP…";
                    extracted = Path.Combine(SettingsStore.Home, "SdCardKit", "Packages", Guid.NewGuid().ToString("N"));
                    string[] choices = await Task.Run(() => Catalog.ExtractCandidates(path, extracted, null, includeBasic: true));
                    int selected = choices.Length == 1 ? 0 : LauncherDialogs.SelectItem(this, "Choose the startup program", choices);
                    if (selected < 0)
                    {
                        kitStatus.Text = "Installation canceled.";
                        return null;
                    }
                    root = extracted;
                    path = Path.Combine(root, choices[selected]);
                }

                string id = null;
                store.Save(data =>
                {
                    var entry = data.Programs.FirstOrDefault(program => program.SdKitSource == item.Path);
                    if (entry == null)
                    {
                        entry = new ProgramEntry { SdKitSource = item.Path, Name = Path.GetFileNameWithoutExtension(path) };
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
                    id = entry.Id;
                });
                extracted = null;
                kitStatus.Text = "Added to Start.";
                return id;
            }
            catch (Exception error)
            {
                kitStatus.Text = "Installation failed.";
                LauncherDialogs.Message(this, error.Message, "Add to Start");
                return null;
            }
            finally
            {
                if (extracted != null && Directory.Exists(extracted)) Directory.Delete(extracted, true);
                loadingKit = false;
                UpdateKitSelection();
            }
        }

        private async Task<string> EnsureKitSnapshot()
        {
            var parent = Path.Combine(SettingsStore.Home, "SdCardKit");
            var revision = kitRevision;
            var destination = Path.Combine(parent, revision);
            if (Directory.Exists(destination)) return destination;

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
