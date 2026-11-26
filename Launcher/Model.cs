using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace CommanderX16
{
    [DataContract]
    internal abstract class Metadata : IExtensibleDataObject
    {
        public ExtensionDataObject ExtensionData { get; set; }
    }

    [DataContract]
    internal sealed class Settings : Metadata
    {
        [DataMember] public int Format = 1;
        [DataMember] public string InstallParent;
        [DataMember] public List<Emulator> Emulators = new List<Emulator>();
        [DataMember] public List<Profile> Profiles = new List<Profile>();
        [DataMember] public List<ProgramEntry> Programs = new List<ProgramEntry>();

        [DataMember] public bool RenamedDataDirectory = true;
        [DataMember] public bool StartOrderInitialized = true;
        [DataMember] public bool EntryOptionsMigrated = true;
        [DataMember] public bool GenericExtrasMigrated = true;
        [DataMember] public bool ProfileCopiesMigrated = true;
        [DataMember] public bool RomCopiesMigrated = true;
        [DataMember] public bool BuildDefaultsMigrated = true;
        [DataMember] public string StartEmulatorId;
        [DataMember] public Profile StartOptions;

        public Emulator FindEmulator(string id) => id == null ? null : Emulators.FirstOrDefault(emulator => emulator.Id == id);

        public Profile DefaultProfile(Emulator emulator) => Profiles.FirstOrDefault(profile =>
            emulator?.DefaultProfileId != null && profile.EmulatorId == emulator.Id && profile.Id == emulator.DefaultProfileId);

        public bool UsesStartDefaults(ProgramEntry entry) => entry.EmulatorId == null && !entry.RequiresEmulatorSelection;

        public Emulator ConfiguredLaunchEmulator(ProgramEntry entry = null)
        {
            var emulator = FindEmulator(entry == null || UsesStartDefaults(entry) ? StartEmulatorId : entry.EmulatorId);
            return File.Exists(emulator?.Exe) ? emulator : null;
        }

        public Emulator ResolveLaunch(ProgramEntry entry, out Profile options)
        {
            var emulator = entry != null && !UsesStartDefaults(entry) ? ConfiguredLaunchEmulator(entry) : null;
            if (emulator != null)
            {
                options = entry.Options ?? Profile.CopyOptions(null, emulator);
                return emulator;
            }

            emulator = ConfiguredLaunchEmulator();
            if (emulator != null)
            {
                options = StartOptions ?? Profile.CopyOptions(null, emulator);
                return emulator;
            }

            var available = Emulators.Where(build => File.Exists(build.Exe)).Take(2).ToArray();
            emulator = available.Length == 1 ? available[0] : null;
            options = DefaultProfile(emulator) ?? Profile.CopyOptions(null, emulator);
            return emulator;
        }

        public bool HasProgramName(string name, string exceptId = null) =>
            Programs.Any(entry => entry.Id != exceptId && MainForm.Same(entry.Name?.Trim(), name.Trim()));

        public string UniqueProgramName(string name, string version = null)
        {
            name = name.Trim();
            if (!HasProgramName(name)) return name;
            if (!string.IsNullOrWhiteSpace(version))
            {
                name += " (" + version.Trim() + ")";
                if (!HasProgramName(name)) return name;
            }
            for (int number = 2; ; number++)
            {
                string candidate = $"{name} ({number})";
                if (!HasProgramName(candidate)) return candidate;
            }
        }

        private void InitializeStartOrder()
        {
            Programs = Programs.OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase).ToList();
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in Programs)
            {
                string name = string.IsNullOrWhiteSpace(entry.Name) ? UniqueProgramName("Program") : entry.Name.Trim();
                if (!used.Add(name))
                {
                    entry.Name = UniqueProgramName(name, entry.Source?.Version);
                    used.Add(entry.Name);
                }
                else entry.Name = name;
            }
            StartOrderInitialized = true;
        }

        public void Upgrade()
        {
            if (!EntryOptionsMigrated || Programs == null) MigrateEntryOptions();
            if (!StartOrderInitialized) InitializeStartOrder();
            if (!GenericExtrasMigrated)
            {
                foreach (var preset in Profiles)
                    preset.MigrateExtra();
                foreach (var entry in Programs)
                    entry.Options?.MigrateExtra();
                GenericExtrasMigrated = true;
            }
            if (!RomCopiesMigrated)
                foreach (var preset in Profiles.Where(preset => string.IsNullOrWhiteSpace(preset.Rom)))
                    preset.Rom = null;
            if (!ProfileCopiesMigrated)
            {
                // Older releases implicitly used the sole registered emulator for unassigned entries.
                if (Emulators.Count == 1 && Programs.Any(UsesStartDefaults))
                {
                    StartEmulatorId = Emulators[0].Id;
                    StartOptions = Profile.Snapshot(null, Emulators[0]);
                    foreach (var entry in Programs.Where(UsesStartDefaults).Where(entry => entry.Options != null))
                        entry.EmulatorId = StartEmulatorId;
                }
                foreach (var entry in Programs)
                {
                    var emulator = FindEmulator(entry.EmulatorId);
                    if (emulator != null)
                        entry.Options = Profile.Snapshot(entry.Options, emulator);
                    entry.ProfileId = null;
                }
                ProfileCopiesMigrated = true;
            }
            if (!RomCopiesMigrated)
            {
                foreach (var entry in Programs)
                {
                    var emulator = FindEmulator(entry.EmulatorId);
                    if (emulator != null)
                        entry.Options = Profile.Snapshot(entry.Options, emulator, legacyRom: true);
                }
                var start = FindEmulator(StartEmulatorId);
                if (start != null)
                    StartOptions = Profile.Snapshot(StartOptions, start, legacyRom: true);
                RomCopiesMigrated = true;
            }
            if (!BuildDefaultsMigrated)
            {
                // Copy legacy build-wide values before retiring those editable build fields.
                EnsureDefaultProfiles();
                foreach (var profile in Profiles)
                {
                    var build = FindEmulator(profile.EmulatorId);
                    if (build == null) continue;
                    var copy = Profile.Snapshot(profile, build);
                    profile.Extra = copy.Extra;
                    profile.Rom = copy.Rom;
                }
                foreach (var build in Emulators)
                {
                    build.Extra = null;
                    build.DefaultRom = null;
                }
                BuildDefaultsMigrated = true;
            }
        }

        public void EnsureDefaultProfiles()
        {
            foreach (var build in Emulators)
            {
                if (DefaultProfile(build) != null) continue;
                string name = "Default";
                for (int suffix = 2; Profiles.Any(existing => existing.EmulatorId == build.Id &&
                    string.Equals(existing.Name, name, StringComparison.OrdinalIgnoreCase)); suffix++)
                    name = "Default (" + suffix + ")";
                var profile = Profile.Snapshot(null, build);
                profile.Id = Guid.NewGuid().ToString("N");
                profile.EmulatorId = build.Id;
                profile.Name = name;
                Profiles.Add(profile);
                build.DefaultProfileId = profile.Id;
            }
        }

        public void RemoveEmulator(Emulator emulator)
        {
            Emulators.RemoveAll(build => build.Id == emulator.Id);
            if (StartEmulatorId == emulator.Id)
                StartEmulatorId = null;
            foreach (var entry in Programs.Where(program => program.EmulatorId == emulator.Id))
            {
                entry.EmulatorId = null;
                entry.RequiresEmulatorSelection = true;
            }
            foreach (var preset in Profiles.Where(profile => profile.EmulatorId == emulator.Id))
            {
                preset.EmulatorId = null;
                preset.DetachedFrom = emulator.Name;
            }
        }

        private void MigrateEntryOptions()
        {
            if (Programs == null) ImportLegacyPrograms();
            foreach (var entry in Programs)
            {
                var emulator = Emulators.FirstOrDefault(e => e.Id == entry.EmulatorId);
                var preset = Profiles.FirstOrDefault(p => p.Id == entry.ProfileId && p.EmulatorId == entry.EmulatorId);
                entry.Options = Profile.CopyOptions(preset, emulator, clearInactive: true);
            }
            EntryOptionsMigrated = true;
        }

        private void ImportLegacyPrograms()
        {
            Programs = new List<ProgramEntry>();
            foreach (var profile in Profiles)
            {
                if (string.IsNullOrWhiteSpace(profile.Prg) || !Path.IsPathRooted(profile.Prg)) continue;
                string path;
                try { path = Path.GetFullPath(profile.Prg); }
                catch (ArgumentException) { continue; }
                catch (NotSupportedException) { continue; }
                catch (PathTooLongException) { continue; }
                if (!string.Equals(Path.GetExtension(path), ".prg", StringComparison.OrdinalIgnoreCase)) continue;
                var program = Programs.FirstOrDefault(p => string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase));
                if (program == null)
                {
                    program = new ProgramEntry { Path = path, Name = Path.GetFileNameWithoutExtension(path) };
                    Programs.Add(program);
                }
                if (program.ProfileId == null && Emulators.Any(e => e.Id == profile.EmulatorId))
                {
                    program.EmulatorId = profile.EmulatorId;
                    program.ProfileId = profile.Id;
                }
            }
        }
    }

    [DataContract]
    internal sealed class Emulator : Metadata
    {
        [DataMember] public string Id = Guid.NewGuid().ToString("N");
        [DataMember] public string Name;
        [DataMember] public string Exe;
        [DataMember] public string Release;
        [DataMember] public string Asset;
        [DataMember] public string InstallRoot;
        [DataMember(EmitDefaultValue = false)] public string SerialOverlayVersion;
        public bool SupportsSerialCard => SerialOverlayVersion == SerialNetworkOverlay.Version;
        [DataMember] public bool UpstreamOptions;
        [DataMember] public string Extra;
        [DataMember] public string DefaultRom;
        [DataMember] public string DefaultProfileId;
        public override string ToString() => Name;
    }

    [DataContract]
    internal sealed class Profile : Metadata
    {
        [DataMember] public string Id = Guid.NewGuid().ToString("N");
        [DataMember] public string EmulatorId;
        [DataMember] public string Name;
        [DataMember] public string Prg { get; set; }
        [DataMember] public string WorkingDirectory { get; set; }
        [DataMember] public bool AutoRun = true;
        [DataMember] public bool Fullscreen;
        [DataMember] public string Cpu;
        [DataMember] public string Mhz;
        [DataMember] public string Ram;
        [DataMember] public string Scale;
        [DataMember] public string Rom;
        [DataMember] public string SdCard;
        [DataMember] public string FsRoot;
        [DataMember] public string Extra;
        [DataMember] public string Gif;
        [DataMember(Name = "SerialCard", EmitDefaultValue = false)] private bool legacySerialCard;
        [DataMember(EmitDefaultValue = false)] public bool? EnableSerialCard;
        [DataMember(EmitDefaultValue = false)] public string SerialCardAddress;
        [DataMember] public string DetachedFrom;
        public override string ToString() => string.IsNullOrEmpty(DetachedFrom) ? Name : Name + " (from " + DetachedFrom + ")";

        public void MigrateExtra()
        {
            if (legacySerialCard && !Launch.HasExtraOption(Extra, "-serialcard"))
                Extra = string.IsNullOrWhiteSpace(Extra) ? "-serialcard" : "-serialcard " + Extra.Trim();
            if (string.IsNullOrWhiteSpace(Extra))
                Extra = null;
            legacySerialCard = false;
        }

        public static Profile Snapshot(Profile source, Emulator emulator, bool legacyRom = false)
        {
            var copy = CopyOptions(source, emulator);
            copy.Extra = copy.Extra ?? emulator?.Extra ?? "";
            if (copy.Rom == null || (legacyRom && string.IsNullOrWhiteSpace(copy.Rom)))
                copy.Rom = Launch.HasExtraOption(copy.Extra, "-rom") ? "" : emulator?.DefaultRom ?? "";
            copy.Id = null;
            copy.EmulatorId = null;
            copy.Name = null;
            copy.DetachedFrom = null;
            return copy;
        }

        public static Profile CopyOptions(Profile source, Emulator emulator, bool clearInactive = false)
        {
            if (source == null)
                return new Profile
                {
                    Scale = emulator?.UpstreamOptions == true && !Launch.HasExtraOption(emulator.Extra, "-scale") ? "2" : null
                };
            var copy = Json.Read<Profile>(Json.Write(source));
            if (clearInactive && emulator?.UpstreamOptions == false)
            {
                copy.Cpu = copy.Mhz = copy.Ram = copy.Scale = null;
                copy.Rom = copy.SdCard = copy.FsRoot = null;
                copy.Fullscreen = false;
            }
            return copy;
        }
    }

    [DataContract]
    internal sealed class ProgramEntry : Metadata
    {
        [DataMember] public string Id = Guid.NewGuid().ToString("N");
        [DataMember] public string Path;
        [DataMember] public string Name;
        [DataMember] public string EmulatorId;
        [DataMember] public string ProfileId;
        [DataMember] public bool RequiresEmulatorSelection;
        [DataMember(EmitDefaultValue = false)] public string CatalogId;
        [DataMember(EmitDefaultValue = false)] public string SdKitSource;
        [DataMember(EmitDefaultValue = false)] public string SdKitRoot;
        [DataMember(EmitDefaultValue = false)] public string StartGifPath;
        [DataMember] public Profile Options;
        [DataMember(EmitDefaultValue = false)] public ProgramSource Source;
        [DataMember(EmitDefaultValue = false)] public ProgramInstallation PreviousInstall;

        public void RememberInstallation(string replacementPath)
        {
            if (!string.IsNullOrEmpty(Path) && !string.Equals(Path, replacementPath, StringComparison.OrdinalIgnoreCase))
                PreviousInstall = new ProgramInstallation { Path = Path, Source = Source };
        }

        public override string ToString() => Name;
    }

    [DataContract]
    internal sealed class ProgramSource
    {
        [DataMember] public string Title;
        [DataMember] public string Author;
        [DataMember] public string Version;
        [DataMember] public string Url;
    }

    [DataContract]
    internal sealed class ProgramInstallation
    {
        [DataMember] public string Path;
        [DataMember] public ProgramSource Source;
    }

    [DataContract]
    internal sealed class Release
    {
        [DataMember] public string Tag { get; set; }
        [DataMember] public string Asset { get; set; }
        [DataMember] public string Url { get; set; }
        [DataMember] public string Digest { get; set; }
        public override string ToString() => Tag + "   " + Asset;
    }

    [DataContract]
    internal sealed class Request
    {
        [DataMember] public string Action;
        [DataMember] public string Exe;
        [DataMember] public string Arguments;
        [DataMember] public string WorkingDirectory;
        [DataMember] public Release Release;
        [DataMember] public string Destination;
        [DataMember(EmitDefaultValue = false)] public string Url;
        [DataMember] public bool Overwrite;
        [DataMember(EmitDefaultValue = false)] public bool SerialOverlay;
    }

    [DataContract]
    internal sealed class Result
    {
        [DataMember] public Release[] Releases { get; set; }
        [DataMember] public string Exe { get; set; }
        [DataMember] public string Backup { get; set; }
    }

    internal sealed class SettingsStore : IDisposable
    {
        public static readonly string Home = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "X16ForAllTime");
        private readonly string path;
        private readonly FileStream sessionLock;
        public Settings Data { get; private set; }

        public SettingsStore(string directory)
        {
            string legacy = null;
            if (MainForm.Same(directory, Home))
            {
                legacy = Path.Combine(Path.GetDirectoryName(Home), "CommanderX16ForRightNow");
                if (!Directory.Exists(Home) && Directory.Exists(legacy))
                    Directory.Move(legacy, Home);
            }
            Directory.CreateDirectory(directory);
            path = Path.Combine(directory, "settings.json");
            sessionLock = new FileStream(Path.Combine(directory, "settings.lock"), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None);
            try
            {
                string source = File.Exists(path) ? path : path + ".bak";
                Data = File.Exists(source) ? Json.Read<Settings>(File.ReadAllBytes(source)) : new Settings();
                if (Data == null || Data.Format != 1 || Data.Emulators == null || Data.Profiles == null)
                    throw new InvalidDataException("Unsupported settings file. Your settings were left on disk.");
                if (!Data.StartOrderInitialized || !Data.BuildDefaultsMigrated || !Data.RomCopiesMigrated || !Data.ProfileCopiesMigrated || !Data.GenericExtrasMigrated || !Data.EntryOptionsMigrated || Data.Programs == null)
                    Save(settings => settings.Upgrade());
                if (legacy != null && !Data.RenamedDataDirectory && !Directory.Exists(legacy))
                    MigratePaths(legacy, directory);
                foreach (var emulator in Data.Emulators)
                    emulator.SerialOverlayVersion = SerialNetworkOverlay.Detect(emulator.Exe);
            }
            catch
            {
                sessionLock.Dispose();
                throw;
            }
        }

        private void MigratePaths(string legacy, string directory)
        {
            string typo = Path.Combine(directory, "Emulatorsw");
            string emulators = Path.Combine(directory, "Emulators");
            if (Directory.Exists(typo) && !Directory.Exists(emulators))
                Directory.Move(typo, emulators);
            bool typoMoved = !Directory.Exists(typo) && Directory.Exists(emulators);

            string UpdatePath(string value)
            {
                if (value == null) return null;
                if (MainForm.Same(value, legacy)) return directory;
                if (value.StartsWith(legacy + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    value = directory + value.Substring(legacy.Length);
                if (typoMoved && (MainForm.Same(value, typo) ||
                    value.StartsWith(typo + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                    return emulators + value.Substring(typo.Length);
                return value;
            }

            string UpdateArguments(string value)
            {
                if (value == null) return null;
                if (typoMoved)
                    value = Regex.Replace(value, Regex.Escape(Path.Combine(legacy, "Emulatorsw") + Path.DirectorySeparatorChar),
                        _ => emulators + Path.DirectorySeparatorChar, RegexOptions.IgnoreCase);
                return Regex.Replace(value, Regex.Escape(legacy + Path.DirectorySeparatorChar),
                    _ => directory + Path.DirectorySeparatorChar, RegexOptions.IgnoreCase);
            }

            void UpdateProfile(Profile profile)
            {
                if (profile == null) return;
                profile.Prg = UpdatePath(profile.Prg);
                profile.WorkingDirectory = UpdatePath(profile.WorkingDirectory);
                profile.Rom = UpdatePath(profile.Rom);
                profile.SdCard = UpdatePath(profile.SdCard);
                profile.FsRoot = UpdatePath(profile.FsRoot);
                profile.Gif = UpdatePath(profile.Gif);
                profile.Extra = UpdateArguments(profile.Extra);
            }

            Save(settings =>
            {
                settings.InstallParent = UpdatePath(settings.InstallParent);
                if (MainForm.Same(settings.InstallParent?.TrimEnd(Path.DirectorySeparatorChar), typo))
                    settings.InstallParent = emulators;
                foreach (var emulator in settings.Emulators)
                {
                    emulator.Exe = UpdatePath(emulator.Exe);
                    emulator.InstallRoot = UpdatePath(emulator.InstallRoot);
                    emulator.DefaultRom = UpdatePath(emulator.DefaultRom);
                    emulator.Extra = UpdateArguments(emulator.Extra);
                }
                UpdateProfile(settings.StartOptions);
                foreach (var profile in settings.Profiles) UpdateProfile(profile);
                foreach (var program in settings.Programs)
                {
                    program.Path = UpdatePath(program.Path);
                    program.SdKitRoot = UpdatePath(program.SdKitRoot);
                    program.StartGifPath = UpdatePath(program.StartGifPath);
                    if (program.PreviousInstall != null)
                        program.PreviousInstall.Path = UpdatePath(program.PreviousInstall.Path);
                    UpdateProfile(program.Options);
                }
                settings.RenamedDataDirectory = true;
            });
        }

        public void Save(Action<Settings> change)
        {
            Settings copy = Json.Read<Settings>(Json.Write(Data));
            change(copy);
            copy.EnsureDefaultProfiles();
            if (copy.Programs.Any(entry => string.IsNullOrWhiteSpace(entry.Name)) ||
                copy.Programs.Select(entry => entry.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != copy.Programs.Count)
                throw new InvalidOperationException("Give each Start program a distinct name.");
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
                {
                    byte[] bytes = Json.Write(copy);
                    file.Write(bytes, 0, bytes.Length);
                    file.Flush(true);
                }
                if (File.Exists(path))
                    File.Replace(temporary, path, path + ".bak");
                else
                    File.Move(temporary, path);
                Data = copy;
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }

        public void Dispose() => sessionLock.Dispose();
    }

    internal static class Json
    {
        public static byte[] Write<T>(T value)
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
                return stream.ToArray();
            }
        }

        public static T Read<T>(byte[] bytes)
        {
            using (var stream = new MemoryStream(bytes))
                return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
        }
    }

    internal static class Launch
    {
        internal static bool IsBasicProgram(string path) =>
            string.Equals(Path.GetExtension(path), ".bas", StringComparison.OrdinalIgnoreCase);

        internal static bool IsProgramFile(string path) =>
            IsBasicProgram(path) || string.Equals(Path.GetExtension(path), ".prg", StringComparison.OrdinalIgnoreCase);

        public static Request Prepare(Emulator emulator, Profile profile, string program, bool validate, string kitRoot = null)
        {
            if (emulator == null || string.IsNullOrWhiteSpace(program))
                throw new InvalidOperationException("Choose an emulator and a program file.");
            string exe = Path.GetFullPath(emulator.Exe);
            string prg = Path.GetFullPath(program);
            string directory = Path.GetDirectoryName(prg);
            if (!IsProgramFile(prg))
                throw new InvalidOperationException("Choose a .prg or .bas file.");
            bool basic = IsBasicProgram(prg);
            if (!basic && prg.Contains(","))
                throw new InvalidOperationException("Move or rename the PRG so its path contains no comma. X16Emu uses a comma to specify a load address.");
            if (validate)
            {
                CheckPath(exe, "Emulator executable");
                if (string.Equals(emulator.Asset, "x16emu_win32-r49.zip", StringComparison.OrdinalIgnoreCase) &&
                    !File.Exists(Path.Combine(Path.GetDirectoryName(exe), "libgcc_s_dw2-1.dll")))
                    throw new IOException("The official R49 Win32 ZIP is missing libgcc_s_dw2-1.dll. " +
                        (Environment.Is64BitOperatingSystem ? "Install R49 Win64 and select it for this program." :
                        "Select a working Win32 build."));
                CheckPath(prg, "Program file");
                CheckPath(directory, "Working directory", true);
            }

            var extra = profile.Extra ?? emulator.Extra;
            if (kitRoot != null && emulator.UpstreamOptions && string.IsNullOrWhiteSpace(profile.FsRoot) &&
                string.IsNullOrWhiteSpace(profile.SdCard) && !HasExtraOption(extra, "-fsroot") &&
                !HasExtraOption(extra, "-sdcard"))
            {
                profile = Profile.CopyOptions(profile, emulator);
                profile.FsRoot = kitRoot;
            }

            var args = new List<string> { basic ? "-bas" : "-prg", prg };
            if (profile.AutoRun)
                args.Add("-run");
            string options = OptionArguments(emulator, profile, directory, validate);
            string arguments = string.Join(" ", args.Select(Quote));
            if (options.Length != 0) arguments += " " + options;
            return new Request { Action = "Launch", Exe = exe, Arguments = arguments, WorkingDirectory = directory };
        }

        public static string OptionArguments(Emulator emulator, Profile profile, string directory = null, bool validate = false)
        {
            if (emulator == null)
                throw new InvalidOperationException("Choose an emulator.");
            var args = new List<string>();
            if (!emulator.UpstreamOptions &&
                (profile.Fullscreen || new[] { profile.Cpu, profile.Mhz, profile.Ram, profile.Scale,
                    profile.SdCard, profile.FsRoot, profile.Gif }.Any(value => !string.IsNullOrWhiteSpace(value))))
                throw new InvalidOperationException("This build is configured for custom arguments only. Clear the standard options or enable them in Emulators.");
            HardwareOptions.Validate(profile);
            Add(args, profile.Cpu);
            Add(args, "-mhz", profile.Mhz);
            Add(args, "-ram", profile.Ram);
            Add(args, "-scale", profile.Scale);
            if (profile.Fullscreen) args.Add("-fullscreen");
            string extra = (profile.Extra ?? emulator.Extra)?.Trim();
            extra = RemoveSerialCardOption(extra, out var suppliedAddress);
            if (emulator.SupportsSerialCard && profile.EnableSerialCard != false)
            {
                args.Add("-serialcard");
                Add(args, suppliedAddress ?? profile.SerialCardAddress);
            }
            string rom = profile.Rom;
            if (!HasExtraOption(extra, "-rom"))
            {
                if (rom == null) rom = emulator.DefaultRom;
                if (string.IsNullOrWhiteSpace(rom) && emulator.SupportsSerialCard)
                    rom = Path.Combine(Path.GetDirectoryName(emulator.Exe), "rom.bin");
            }
            AddPath(args, "-rom", rom, "ROM", validate);
            AddPath(args, "-sdcard", profile.SdCard, "SD card image", validate);
            AddPath(args, "-fsroot", profile.FsRoot, "Host filesystem root", validate, true);
            if (!string.IsNullOrWhiteSpace(profile.Gif))
            {
                string path = directory == null ? profile.Gif.Trim() : Path.GetFullPath(Path.Combine(directory, profile.Gif.Trim()));
                string extension = Path.GetExtension(path);
                if (path.Contains(",") || !(string.Equals(extension, ".gif", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(extension, ".gi", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("Choose a .gif or .gi output path without commas.");
                if (validate)
                {
                    CheckPath(Path.GetDirectoryName(path), "GIF output folder", true);
                    if (File.Exists(path))
                        throw new IOException("A GIF already exists at " + path + ". Choose a different output file.");
                }
                Add(args, "-gif", path);
            }
            string[] duplicates = args.Where(arg => arg.StartsWith("-", StringComparison.Ordinal))
                .Concat(new[] { "-prg", "-bas", "-run" }).ToArray();
            if (args.Contains("-c02") || args.Contains("-c816"))
                duplicates = duplicates.Concat(new[] { "-c02", "-c816" }).ToArray();
            foreach (string option in duplicates)
                if (HasExtraOption(extra, option))
                    throw new InvalidOperationException(option + " conflicts with the launcher settings. Remove it from Additional arguments.");
            string arguments = string.Join(" ", args.Select(Quote));
            if (!string.IsNullOrWhiteSpace(extra))
                arguments += " " + extra;
            return arguments;
        }

        private static readonly Regex ArgumentTokens = new Regex(
            @"(?:[^\s""]|""(?:\\.|[^""])*"")+", RegexOptions.CultureInvariant);

        public static Profile WithGifOverride(Profile source, Emulator emulator, string output)
        {
            var copy = Profile.CopyOptions(source, emulator);
            copy.Gif = output.Trim();
            string extra = copy.Extra ?? emulator?.Extra ?? "";
            var tokens = ArgumentTokens.Matches(extra).Cast<Match>().ToArray();
            for (int i = tokens.Length - 1; i >= 0; i--)
            {
                var flag = tokens[i];
                if (!IsOption(flag, "-gif")) continue;
                var value = i + 1 < tokens.Length && !tokens[i + 1].Value.Trim('"').StartsWith("-", StringComparison.Ordinal)
                    ? tokens[i + 1] : flag;
                extra = extra.Remove(flag.Index, value.Index + value.Length - flag.Index);
            }
            copy.Extra = extra.Trim();
            return copy;
        }

        internal static string RemoveSerialCardOption(string extra, out string address)
        {
            address = null;
            extra = extra ?? "";
            var tokens = ArgumentTokens.Matches(extra).Cast<Match>().ToArray();
            for (int i = tokens.Length - 1; i >= 0; i--)
            {
                var flag = tokens[i];
                if (!IsOption(flag, "-serialcard")) continue;
                var operand = i + 1 < tokens.Length && !tokens[i + 1].Value.Trim('"').StartsWith("-", StringComparison.Ordinal)
                    ? tokens[i + 1] : flag;
                if (operand != flag) address = address ?? operand.Value.Trim('"');
                extra = extra.Remove(flag.Index, operand.Index + operand.Length - flag.Index);
            }
            return extra.Trim();
        }

        internal static bool HasExtraOption(string extra, string option) =>
            ArgumentTokens.Matches(extra ?? "").Cast<Match>().Any(token => IsOption(token, option));

        private static bool IsOption(Match token, string option) =>
            string.Equals(token.Value.Trim('"'), option, StringComparison.OrdinalIgnoreCase);

        private static void Add(List<string> args, params string[] values)
        {
            if (!string.IsNullOrWhiteSpace(values.Last()))
                args.AddRange(values);
        }

        private static void AddPath(List<string> args, string option, string path, string label,
            bool validate, bool directory = false)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;
            string fullPath = Path.GetFullPath(path.Trim());
            if (validate)
                CheckPath(fullPath, label, directory);
            args.Add(option);
            args.Add(fullPath);
        }

        private static void CheckPath(string path, string label, bool directory = false)
        {
            if (!(directory ? Directory.Exists(path) : File.Exists(path)))
                throw new IOException(label + " was not found." + Environment.NewLine + path);
        }

        public static string Quote(string value)
        {
            if (value.Length != 0 && !value.Any(c => char.IsWhiteSpace(c) || c == '"'))
                return value;
            var quoted = new StringBuilder("\"");
            int backslashes = 0;
            foreach (char c in value)
            {
                if (c == '\\')
                {
                    backslashes++;
                    continue;
                }
                quoted.Append('\\', c == '"' ? 2 * backslashes + 1 : backslashes);
                quoted.Append(c);
                backslashes = 0;
            }
            return quoted.Append('\\', 2 * backslashes).Append('"').ToString();
        }

        public static string PowerShellLiteral(string value) => "'" + value.Replace("'", "''") + "'";
    }
}
