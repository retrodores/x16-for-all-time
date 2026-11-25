using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
        private KitItem[] kitItems;
        private bool loadingKit;

        private void BuildKitPage(TabPage page, TabControl tabs)
        {
            var body = PageBody(page);
            kitSearch.AccessibleName = "Find a file or folder";
            AddRow(body, Pair("Find a file or folder", kitSearch, refreshKit));
            AddRow(body, kitCount);
            AddRow(body, kitTree, SizeType.Percent, 100);
            AddRow(body, Flow(openKit));
            AddRow(body, kitStatus);
            refreshKit.Click += async (sender, args) => await LoadKit();
            kitSearch.TextChanged += (sender, args) => ShowKitItems();
            openKit.Click += (sender, args) => OpenKitItem(kitTree.SelectedNode);
            kitTree.NodeMouseDoubleClick += (sender, args) =>
            {
                if (args.Button == MouseButtons.Left && args.Node.Tag is KitItem item && item.Type == "blob")
                    OpenKitItem(args.Node);
            };
            tabs.SelectedIndexChanged += async (sender, args) =>
            {
                if (tabs.SelectedTab == page && kitItems == null) await LoadKit();
            };
        }

        private async Task LoadKit()
        {
            if (loadingKit) return;
            loadingKit = true;
            refreshKit.Enabled = false;
            kitStatus.Text = "Loading the SD Card Kit…";
            try
            {
                var snapshot = await PowerShell.Execute<KitSnapshot>(new Request { Action = "SdCardTree" });
                if (snapshot?.Tree == null || snapshot.Truncated)
                    throw new InvalidOperationException("GitHub returned an incomplete file tree.");
                var items = snapshot.Tree.Where(item => item.Path?.StartsWith(KitRoot + "/", StringComparison.Ordinal) == true &&
                    (item.Type == "tree" || item.Type == "blob")).ToArray();
                if (items.Length == 0)
                    throw new InvalidOperationException("GitHub returned no files under " + KitRoot + ".");
                kitItems = items;
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
                refreshKit.Enabled = true;
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
            var url = KitRepository + "/" + segment + "/main/" +
                string.Join("/", path.Split('/').Select(Uri.EscapeDataString));
            Attempt(() => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }));
        }

        [DataContract]
        private sealed class KitSnapshot
        {
            [DataMember(Name = "tree")] public KitItem[] Tree { get; set; }
            [DataMember(Name = "truncated")] public bool Truncated { get; set; }
        }

        [DataContract]
        private sealed class KitItem
        {
            [DataMember(Name = "path")] public string Path { get; set; }
            [DataMember(Name = "type")] public string Type { get; set; }
        }
    }
}
