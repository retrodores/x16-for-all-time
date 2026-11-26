using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CommanderX16
{
    [DataContract]
    internal sealed class Catalog
    {
        [DataMember(Name = "schema_version")] public int SchemaVersion { get; set; }
        [DataMember(Name = "programs")] public CatalogProgram[] Programs { get; set; }

        public static async Task<CatalogProgram[]> Fetch()
        {
            var catalog = await PowerShell.Execute<Catalog>(new Request { Action = "Catalog" });
            if (catalog?.SchemaVersion != 1 || catalog.Programs == null)
                throw new IOException("Expected X16 Files catalog schema version 1.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var program in catalog.Programs)
                if (string.IsNullOrWhiteSpace(program?.Id) || !ids.Add(program.Id))
                    throw new IOException("The catalog contains a missing or duplicate program ID.");
            return catalog.Programs;
        }

        public static bool IsWebUrl(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) &&
            uri.Host.Length != 0 && uri.UserInfo.Length == 0;

        public static async Task<string> Download(CatalogProgram program, CatalogPackage package, IProgress<string> progress,
            Func<string[], string> choosePrg)
        {
            if (package?.Supported != true)
                throw new IOException("This program has no supported package.");

            string id;
            using (var sha = SHA256.Create())
                id = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(program.Id))).Replace("-", "");
            var parent = Path.Combine(SettingsStore.Home, "Catalog", id);
            var instance = Guid.NewGuid().ToString("N");
            var stage = Path.Combine(parent, "." + instance);
            var files = Path.Combine(stage, "files");
            Directory.CreateDirectory(files);
            try
            {
                var archive = package.Format == "zip";
                var relative = archive ? package.Prg : DirectFileName(package.Url);
                var download = archive ? Path.Combine(stage, "package.zip") : Path.Combine(files, relative);
                progress.Report("Downloading " + program + "…");
                await PowerShell.Execute(new Request { Action = "Download", Url = package.Url, Destination = download });
                if (archive)
                {
                    progress.Report("Extracting " + program + "…");
                    var candidates = await Task.Run(() => ExtractCandidates(download, files, relative));
                    relative = candidates.Length == 1 ? candidates[0] : choosePrg(candidates);
                    if (relative == null) return null;
                }

                var destination = Path.Combine(parent, instance);
                Directory.Move(files, destination);
                return Path.Combine(destination, relative);
            }
            finally
            {
                Directory.Delete(stage, true);
            }
        }

        private static string DirectFileName(string url)
        {
            var name = Uri.UnescapeDataString(new Uri(url).Segments.Last());
            return ValidComponent(name) && string.Equals(Path.GetExtension(name), ".prg", StringComparison.OrdinalIgnoreCase)
                ? name : "program.prg";
        }

        internal static string[] ExtractCandidates(string archivePath, string directory, string entryPoint,
            bool includeBasic = false)
        {
            var candidates = ExtractArchive(archivePath, directory)
                .Where(path => string.IsNullOrEmpty(entryPoint)
                    ? includeBasic ? Launch.IsProgramFile(path) :
                        string.Equals(Path.GetExtension(path), ".prg", StringComparison.OrdinalIgnoreCase)
                    : string.Equals(path.Replace('\\', '/'), entryPoint, StringComparison.Ordinal))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
            if (candidates.Length == 0)
                throw new IOException(string.IsNullOrEmpty(entryPoint)
                    ? includeBasic ? "The ZIP contains no PRG or BAS files." : "The ZIP contains no PRG files."
                    : "The ZIP does not contain the exact PRG path " + entryPoint + ".");
            return candidates;
        }

        internal static void DiscardExtraction(string directory)
        {
            if (directory == null || !Directory.Exists(directory)) return;
            try { Directory.Delete(directory, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        internal static List<string> ExtractArchive(string archivePath, string directory, string prefix = "")
        {
            var root = Path.GetFullPath(directory) + Path.DirectorySeparatorChar;
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var files = new List<string>();
            using (var archive = ZipFile.OpenRead(archivePath))
            {
                foreach (var entry in archive.Entries)
                {
                    string name = entry.FullName.Replace('\\', '/');
                    if (!name.StartsWith(prefix, StringComparison.Ordinal) || name.Length == prefix.Length) continue;
                    name = name.Substring(prefix.Length);
                    bool isFolder = name.EndsWith("/", StringComparison.Ordinal);
                    string[] parts = (isFolder ? name.Substring(0, name.Length - 1) : name).Split('/');
                    if (parts.Any(part => !ValidComponent(part)))
                        throw new IOException("The ZIP contains an unsafe Windows path. " + entry.FullName);

                    string relative = "";
                    foreach (var part in parts)
                    {
                        relative = Path.Combine(relative, part);
                        if (names.TryGetValue(relative, out var original) && original != relative)
                            throw new IOException("The ZIP contains names that differ only by case. " + entry.FullName);
                        names[relative] = relative;
                    }

                    string path = Path.GetFullPath(Path.Combine(root, relative));
                    if (!path.StartsWith(root, StringComparison.Ordinal))
                        throw new IOException("The ZIP contains a path outside the program folder. " + entry.FullName);
                    if (isFolder)
                        Directory.CreateDirectory(path);
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(path));
                        entry.ExtractToFile(path);
                        files.Add(relative);
                    }
                }
            }
            return files;
        }

        private static readonly Regex DeviceName = new Regex(
            @"^(CON|CONIN\$|CONOUT\$|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static bool ValidComponent(string part) =>
            part.Length != 0 && !part.EndsWith(".", StringComparison.Ordinal) &&
            !part.EndsWith(" ", StringComparison.Ordinal) &&
            !part.Any(c => c < 32 || "<>:\"/\\|?*".IndexOf(c) >= 0) &&
            !DeviceName.IsMatch(part.Split('.')[0].TrimEnd(' '));
    }

    [DataContract]
    internal sealed class CatalogProgram
    {
        [DataMember(Name = "appid")] public string Id { get; set; }
        [DataMember(Name = "name")] public string Name { get; set; }
        [DataMember(Name = "category")] public string Category { get; set; }
        [DataMember(Name = "author")] public CatalogAuthor Author { get; set; }
        [DataMember(Name = "version")] public string Version { get; set; }
        [DataMember(Name = "description")] public string Description { get; set; }
        [DataMember(Name = "page_url")] public string PageUrl { get; set; }
        [DataMember(Name = "package")] public CatalogPackage Package { get; set; }

        [DataMember(Name = "packages")] public CatalogPackage[] Packages { get; set; }

        public CatalogPackage[] Downloads => (Packages ?? new[] { Package })
            .Where(package => package?.Supported == true).ToArray();

        public override string ToString() => string.IsNullOrWhiteSpace(Name) ? Id : Name;
    }

    [DataContract]
    internal sealed class CatalogAuthor
    {
        [DataMember(Name = "name")] public string Name { get; set; }
    }

    [DataContract]
    internal sealed class CatalogPackage
    {
        [DataMember(Name = "format")] public string Format { get; set; }
        [DataMember(Name = "url")] public string Url { get; set; }
        [DataMember(Name = "prg")] public string Prg { get; set; }

        public bool Supported => Catalog.IsWebUrl(Url) &&
            (Format == "prg" || Format == "zip");
    }
}
