using System;
using System.Globalization;
using System.Linq;

namespace CommanderX16
{
    internal static class HardwareOptions
    {
        public static readonly string[] Cpu = { "-c02", "-c816" };
        public static readonly string[] Clock = { "2", "4", "8" };
        public static readonly string[] Ram = { "512", "1024", "1536", "2048" };
        public static readonly string[] Scale = { "1", "2", "3", "4" };

        public static string CpuName(string value) => value == "-c816" ? "65C816" : "65C02";

        public static bool NeedsAdvanced(Profile profile) =>
            OutsideStandard(profile.Mhz, Clock) || OutsideStandard(profile.Ram, Ram);

        private static bool OutsideStandard(string value, string[] known) =>
            !string.IsNullOrWhiteSpace(value) && !known.Contains(value);

        public static void Validate(Profile profile)
        {
            if (OutsideStandard(profile.Cpu, Cpu))
                throw new InvalidOperationException("Choose the emulator default, 65C02, or 65C816.");
            if (!IsNumber(profile.Mhz, 1, 40))
                throw new InvalidOperationException("Clock speed must be a whole number from 1 to 40 MHz.");
            if (!IsNumber(profile.Ram, 8, 2048, 8))
                throw new InvalidOperationException("Banked RAM must be a multiple of 8 KiB from 8 to 2048 KiB.");
            if (OutsideStandard(profile.Scale, Scale))
                throw new InvalidOperationException("Choose a display scale from 1× to 4×.");
        }

        private static bool IsNumber(string value, int min, int max, int step = 1) =>
            string.IsNullOrWhiteSpace(value) ||
            (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) &&
                parsed >= min && parsed <= max && parsed % step == 0);
    }
}
