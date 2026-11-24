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

        [DataMember] public bool EntryOptionsMigrated = true;
        [DataMember] public bool GenericExtrasMigrated = true;
        [DataMember] public bool ProfileCopiesMigrated = true;
        [DataMember] public bool RomCopiesMigrated = true;
        [DataMember] public string StartEmulatorId;
        [DataMember] public Profile StartOptions;

        public Emulator FindEmulator(string id) => id == null ? null : Emulators.FirstOrDefault(emulator => emulator.Id == id);

        public Profile DefaultProfile(Emulator emulator) => Profiles.FirstOrDefault(profile =>
            emulator?.DefaultProfileId != null && profile.EmulatorId == emulator.Id && profile.Id == emulator.DefaultProfileId);

        public bool UsesStartDefaults(ProgramEntry entry) => entry.EmulatorId == null && !entry.RequiresEmulatorSelection;

        public Emulator LaunchEmulator(ProgramEntry entry) =>
            FindEmulator(UsesStartDefaults(entry) ? StartEmulatorId : entry.EmulatorId);

        public Profile LaunchOptions(ProgramEntry entry) =>
            (UsesStartDefaults(entry) ? StartOptions : entry.Options) ?? new Profile();

        public void Upgrade()
        {
            if (!EntryOptionsMigrated || Programs == null) MigrateEntryOptions();
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
            if (source == null) return new Profile();
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
        [DataMember] public Profile Options;
        public override string ToString() => Name;
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
    }

    [DataContract]
    internal sealed class Result
    {
        [DataMember] public Release[] Releases { get; set; }
        [DataMember] public string Exe { get; set; }
    }

    internal sealed class SettingsStore : IDisposable
    {
        public static readonly string Home = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CommanderX16ForRightNow");
        private readonly string path;
        private readonly FileStream sessionLock;
        public Settings Data { get; private set; }

        public SettingsStore(string directory)
        {
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
                if (!Data.RomCopiesMigrated || !Data.ProfileCopiesMigrated || !Data.GenericExtrasMigrated || !Data.EntryOptionsMigrated || Data.Programs == null)
                    Save(settings => settings.Upgrade());
            }
            catch
            {
                sessionLock.Dispose();
                throw;
            }
        }

        public void Save(Action<Settings> change)
        {
            Settings copy = Json.Read<Settings>(Json.Write(Data));
            change(copy);
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
        public static Request Prepare(Emulator emulator, Profile profile, string program, bool validate)
        {
            if (emulator == null || string.IsNullOrWhiteSpace(program))
                throw new InvalidOperationException("Choose an emulator and a PRG.");
            string exe = Path.GetFullPath(emulator.Exe);
            string prg = Path.GetFullPath(program);
            string directory = Path.GetDirectoryName(prg);
            if (!string.Equals(Path.GetExtension(prg), ".prg", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Choose a .prg file.");
            if (prg.Contains(","))
                throw new InvalidOperationException("Move or rename the PRG so its path contains no comma. X16Emu uses a comma to specify a load address.");
            if (validate)
            {
                CheckPath(exe, "Emulator executable");
                CheckPath(prg, "PRG");
                CheckPath(directory, "Working directory", true);
            }

            var args = new List<string> { "-prg", prg };
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
            string rom = profile.Rom;
            if (rom == null && !HasExtraOption(extra, "-rom"))
                rom = emulator.DefaultRom;
            AddPath(args, "-rom", rom, "ROM", validate);
            AddPath(args, "-sdcard", profile.SdCard, "SD card image", validate);
            AddPath(args, "-fsroot", profile.FsRoot, "Host filesystem root", validate, true);
            if (!string.IsNullOrWhiteSpace(profile.Gif))
            {
                string path = directory == null ? profile.Gif.Trim() : Path.GetFullPath(Path.Combine(directory, profile.Gif.Trim()));
                if (path.Contains(",") || !string.Equals(Path.GetExtension(path), ".gif", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Choose a .gif output path without commas.");
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
