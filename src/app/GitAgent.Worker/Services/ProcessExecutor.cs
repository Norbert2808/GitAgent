using Serilog;
using Microsoft.Extensions.Options;
using GitAgent.Worker.Configuration;

namespace GitAgent.Worker.Services;

public interface IProcessExecutor
{
    ProcessResult Execute(string fileName, string arguments, string workingDirectory);
}

public class ProcessExecutor : IProcessExecutor
{
    private readonly int _timeoutSeconds;

    public ProcessExecutor(IOptions<WorkerConfig> config)
    {
        _timeoutSeconds = config.Value.Timeouts.GitProcessTimeoutSeconds;
    }

    public ProcessResult Execute(string fileName, string arguments, string workingDirectory)
    {
        var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        Log.Debug($"Executing: {fileName} {arguments} in {workingDirectory}");

        try
        {
            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();

            if (!process.WaitForExit(_timeoutSeconds * 1000))
            {
                Log.Error($"Process timeout after {_timeoutSeconds} seconds. Killing process.");
                try
                {
                    process.Kill();
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to kill timed out process");
                }

                return new ProcessResult
                {
                    ExitCode = -1,
                    Output = output,
                    Error = $"Process timed out after {_timeoutSeconds} seconds"
                };
            }

            var result = new ProcessResult
            {
                ExitCode = process.ExitCode,
                Output = output,
                Error = error
            };

            if (result.ExitCode != 0)
            {
                Log.Warning($"Process exited with code {result.ExitCode}. Error: {result.Error}");
            }

            return result;
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Error executing process: {fileName} {arguments}");
            return new ProcessResult
            {
                ExitCode = -1,
                Output = string.Empty,
                Error = $"Process execution error: {ex.Message}"
            };
        }
    }
}

public class ProcessResult
{
    public int ExitCode { get; set; }
    public string Output { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
    public bool Success => ExitCode == 0;
}
