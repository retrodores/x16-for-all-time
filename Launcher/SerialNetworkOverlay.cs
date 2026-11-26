using System;
using System.IO;
using System.Security.Cryptography;

namespace CommanderX16
{
    internal static class SerialNetworkOverlay
    {
        public const string Version = "0.2.0";

        public static bool Supports(Release release) => Environment.Is64BitOperatingSystem &&
            MainForm.Same(release?.Tag, "r49") && MainForm.Same(release?.Asset, "x16emu_win64-r49.zip");

        public static string Detect(string executable)
        {
            if (!Environment.Is64BitOperatingSystem || !File.Exists(executable)) return null;
            try
            {
                var exe = new FileInfo(executable);
                if (exe.Length != 1094580) return null;
                var rom = Path.Combine(exe.DirectoryName, "rom.bin");
                if (!File.Exists(rom)) return null;
                return Matches(executable, "13e02042ddf8a2989872ec3230c3897f85028e2b9e40e6307d1de6b8ceb1ce65") &&
                    Matches(rom, "b81654cc8c87ed96e3ffc7c8e7c312c9f3b7b870c7bb34de61e61eac931b819a") ? Version : null;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        private static bool Matches(string path, string expected)
        {
            using (var stream = File.OpenRead(path))
            using (var sha256 = SHA256.Create())
                return MainForm.Same(BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", ""), expected);
        }
    }
}
