using System;
using System.IO;
using System.Linq;

namespace CommanderX16
{
    internal static class EmulatorFolders
    {
        public static string Root(Emulator emulator) => Path.GetFullPath(
            emulator.InstallRoot ?? Path.GetDirectoryName(emulator.Exe)).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        public static bool Contains(string folder, string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            string normalized = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return MainForm.Same(folder, normalized) ||
                normalized.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        public static void CheckDirectory(string folder, Settings settings, Emulator emulator)
        {
            if (string.IsNullOrWhiteSpace(Path.GetFileName(folder)) ||
                Contains(folder, SettingsStore.Home) || Contains(folder, AppDomain.CurrentDomain.BaseDirectory) ||
                new[] { Environment.SpecialFolder.UserProfile, Environment.SpecialFolder.DesktopDirectory,
                    Environment.SpecialFolder.MyDocuments, Environment.SpecialFolder.Windows,
                    Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.CommonApplicationData }
                    .Any(special => Contains(folder, Environment.GetFolderPath(special))))
                throw new IOException("Choose a dedicated emulator directory.");

            if (settings.Emulators.Any(other => other.Id != emulator?.Id &&
                    (Contains(folder, other.Exe) || Contains(Root(other), folder))))
                throw new IOException("The folder is shared with another registered emulator.");
            if (settings.Programs.Any(program => Contains(folder, program.Path)))
                throw new IOException("The folder contains a PRG used by the launcher.");

            for (var current = new DirectoryInfo(folder); current != null; current = current.Parent)
                if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("The emulator directory is reached through a symbolic link or junction.");
        }

        public static void CheckDelete(string folder, Settings settings, Emulator emulator)
        {
            CheckDirectory(folder, settings, emulator);
            if (!Directory.Exists(folder))
                throw new IOException("The emulator folder does not exist.");
            CheckTree(new DirectoryInfo(folder));
        }

        private static void CheckTree(DirectoryInfo directory)
        {
            foreach (var item in directory.EnumerateFileSystemInfos())
            {
                if ((item.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("The folder contains a symbolic link or junction.");
                if (item is DirectoryInfo subdirectory)
                    CheckTree(subdirectory);
            }
        }
    }
}
