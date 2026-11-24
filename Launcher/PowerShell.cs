using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace CommanderX16
{
    internal static class PowerShell
    {
        public static Task<Result> Execute(Request request) => Task.Run(() => Run(request));

        private static Result Run(Request request)
        {
            string script = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "X16.ps1");
            string shell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            if (!File.Exists(script) || !File.Exists(shell))
                throw new IOException("The launcher requires its X16.ps1 script and Windows PowerShell 5.1.");

            string temporary = Path.Combine(Path.GetTempPath(), "CommanderX16-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporary);
            try
            {
                string requestFile = Path.Combine(temporary, "request.json");
                string resultFile = Path.Combine(temporary, "result.txt");
                File.WriteAllBytes(requestFile, Json.Write(request));
                var start = new ProcessStartInfo(shell)
                {
                    Arguments = "-NoProfile -NonInteractive -ExecutionPolicy RemoteSigned -File " +
                        Launch.Quote(script) + " -RequestFile " + Launch.Quote(requestFile) +
                        " -ResultFile " + Launch.Quote(resultFile),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                };
                using (Process process = Process.Start(start))
                {
                    Task<string> errors = process.StandardError.ReadToEndAsync();
                    process.WaitForExit(-1);
                    if (process.ExitCode != 0)
                    {
                        string error = File.Exists(resultFile) ? File.ReadAllText(resultFile) :
                            (errors.IsCompleted ? errors.GetAwaiter().GetResult() : null);
                        throw new IOException(string.IsNullOrWhiteSpace(error) ? "Windows PowerShell could not complete the operation." : error.Trim());
                    }
                    return Json.Read<Result>(File.ReadAllBytes(resultFile));
                }
            }
            finally
            {
                Directory.Delete(temporary, true);
            }
        }
    }
}
