namespace DevBrain.Api.Setup;

using System.Diagnostics;

public static class ProcessRunner
{
    public static (int exitCode, string stdout, string stderr) Run(string command, string arguments, int timeoutMs = 5000)
    {
        var isWindows = OperatingSystem.IsWindows();
        var psi = new ProcessStartInfo
        {
            FileName = isWindows ? "cmd.exe" : "/bin/bash",
            Arguments = isWindows ? $"/c {command} {arguments}" : $"-c \"{command} {arguments}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();

        if (!process.WaitForExit(timeoutMs))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return (-1, "", "Timed out after " + timeoutMs / 1000 + "s");
        }

        return (process.ExitCode, stdout.Trim(), stderr.Trim());
    }

    public static bool CommandExists(string command)
    {
        var checkCmd = OperatingSystem.IsWindows() ? "where" : "which";
        var (exitCode, _, _) = Run(checkCmd, command, 3000);
        return exitCode == 0;
    }

    public static string? GetCommandPath(string command)
    {
        var checkCmd = OperatingSystem.IsWindows() ? "where" : "which";
        var (exitCode, stdout, _) = Run(checkCmd, command, 3000);
        return exitCode == 0 ? stdout.Split('\n')[0].Trim() : null;
    }
}
