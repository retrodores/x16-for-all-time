using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace CommanderX16
{
    internal sealed class DownloadForbiddenException : IOException
    {
        internal DownloadForbiddenException(string url) : base("The download server returned HTTP 403 (Forbidden).")
        {
            Url = url;
        }

        internal string Url { get; }
    }

    internal static class PowerShell
    {
        public static Task<Result> Execute(Request request, IProgress<string> progress = null) => Execute<Result>(request, progress);

        public static Task<T> Execute<T>(Request request, IProgress<string> progress = null) => Task.Run(() => Run<T>(request, progress));

        private static T Run<T>(Request request, IProgress<string> progress)
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
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };
                using (Process process = Process.Start(start))
                {
                    const string prefix = "X16-STAGE:";
                    process.OutputDataReceived += (sender, args) =>
                    {
                        if (args.Data?.StartsWith(prefix, StringComparison.Ordinal) == true)
                            progress?.Report(args.Data.Substring(prefix.Length));
                    };
                    process.BeginOutputReadLine();
                    Task<string> errors = process.StandardError.ReadToEndAsync();
                    process.WaitForExit(-1);
                    if (process.ExitCode == 3 && request.Action == "Download")
                        throw new DownloadForbiddenException(request.Url);
                    if (process.ExitCode != 0)
                    {
                        string error = File.Exists(resultFile) ? File.ReadAllText(resultFile) :
                            (errors.IsCompleted ? errors.GetAwaiter().GetResult() : null);
                        throw new IOException(string.IsNullOrWhiteSpace(error) ? "Windows PowerShell could not complete the operation." : error.Trim());
                    }
                    return Json.Read<T>(File.ReadAllBytes(resultFile));
                }
            }
            finally
            {
                Directory.Delete(temporary, true);
            }
        }
    }
}
