using System.Diagnostics;

namespace YATSS
{
    public interface IControllerFirmwareFlasher
    {
        Task FlashAsync(
            ControllerFirmwarePackage package,
            string portName,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default);
    }

    internal static class FirmwareToolRunner
    {
        public static async Task RunAsync(
            string executablePath,
            IReadOnlyList<string> arguments,
            IProgress<string>? progress,
            CancellationToken cancellationToken)
        {
            ProcessStartInfo startInfo = new(executablePath)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using Process process = new() { StartInfo = startInfo };
            List<string> output = new();
            object outputGate = new();
            void CaptureLine(string? line)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    return;
                }

                lock (outputGate)
                {
                    output.Add(line);
                }
                progress?.Report(line);
            }

            process.OutputDataReceived += (_, args) => CaptureLine(args.Data);
            process.ErrorDataReceived += (_, args) => CaptureLine(args.Data);
            if (!process.Start())
            {
                throw new InvalidOperationException($"{Path.GetFileName(executablePath)} did not start");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(cancellationToken);
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                string detail;
                lock (outputGate)
                {
                    detail = string.Join(Environment.NewLine, output.TakeLast(12));
                }
                throw new InvalidOperationException(
                    $"{Path.GetFileName(executablePath)} failed with exit code {process.ExitCode}." +
                    $"{Environment.NewLine}{detail}");
            }
        }
    }
}
