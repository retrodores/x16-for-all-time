using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace CommanderX16
{
    internal static class EmulatorProcess
    {
        public static Task Start(Request request) => Task.Run(() => StartAndCheck(request));

        private static void StartAndCheck(Request request)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo(request.Exe, request.Arguments)
                {
                    WorkingDirectory = request.WorkingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                }
            };
            var output = new StringBuilder();
            var standardOut = new TaskCompletionSource<bool>();
            var standardError = new TaskCompletionSource<bool>();
            void Record(string line, TaskCompletionSource<bool> completion)
            {
                if (line == null)
                {
                    completion.TrySetResult(true);
                    return;
                }
                if (string.IsNullOrWhiteSpace(line)) return;
                lock (output)
                {
                    output.AppendLine(line.Length > 1000 ? line.Substring(0, 1000) : line);
                    if (output.Length > 4000) output.Remove(0, output.Length - 4000);
                }
            }
            process.ErrorDataReceived += (sender, args) => Record(args.Data, standardError);
            process.OutputDataReceived += (sender, args) => Record(args.Data, standardOut);
            bool monitorInBackground = false;
            try
            {
                if (!process.Start()) throw new IOException("Windows did not start the emulator.");
                process.BeginErrorReadLine();
                process.BeginOutputReadLine();
                if (process.WaitForExit(1600))
                {
                    Task.WaitAll(new Task[] { standardOut.Task, standardError.Task }, 250);
                    string message;
                    lock (output) message = output.ToString().Trim();
                    string errorCode = process.ExitCode < 0 ? "0x" + process.ExitCode.ToString("X8") : process.ExitCode.ToString();
                    if (process.ExitCode == unchecked((int)0xC0000135))
                        message = "Windows could not load a required DLL. Check that the full emulator archive is unpacked next to x16emu.exe.";
                    throw new IOException("X16Emu exited during startup (code " + errorCode + ")." +
                        (message.Length == 0 ? "" : Environment.NewLine + message));
                }
                process.Exited += (sender, args) => process.Dispose();
                process.EnableRaisingEvents = true;
                monitorInBackground = true;
            }
            finally
            {
                if (!monitorInBackground) process.Dispose();
            }
        }
    }
}
