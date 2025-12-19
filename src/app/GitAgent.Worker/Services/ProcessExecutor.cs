using Serilog;

namespace GitAgent.Worker.Services;

public interface IProcessExecutor
{
    ProcessResult Execute(string fileName, string arguments, string workingDirectory);
}

public class ProcessExecutor : IProcessExecutor
{
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

        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

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
}

public class ProcessResult
{
    public int ExitCode { get; set; }
    public string Output { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
    public bool Success => ExitCode == 0;
}
