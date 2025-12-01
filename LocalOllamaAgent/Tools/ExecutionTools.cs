using System.ComponentModel;
using System.Diagnostics;

namespace LocalOllamaAgent;

/// <summary>
/// Tools for code execution phase.
/// </summary>
public static class ExecutionTools
{
    [Description("Execute compiled C# code and return execution results.")]
    public static Dictionary<string, object?> ExecuteCode(
        [Description("Path to the compiled DLL to execute.")] string dllPath)
    {
        if (!File.Exists(dllPath))
        {
            return new Dictionary<string, object?>
            {
                ["success"] = false,
                ["error"] = $"DLL not found at: {dllPath}",
                ["output"] = null
            };
        }

        try
        {
            var (exitCode, stdout, stderr) = RunProcess("dotnet", $"\"{dllPath}\"", 
                workingDir: Path.GetDirectoryName(dllPath), 
                timeoutMs: 5000);

            bool success = exitCode == 0;
            string output = stdout;
            
            if (!string.IsNullOrWhiteSpace(stderr))
            {
                output += "\n[STDERR]\n" + stderr;
            }

            if (exitCode == -1)
            {
                return new Dictionary<string, object?>
                {
                    ["success"] = false,
                    ["error"] = "Execution timeout (5 seconds)",
                    ["output"] = output
                };
            }

            return new Dictionary<string, object?>
            {
                ["success"] = success,
                ["exitCode"] = exitCode,
                ["output"] = output,
                ["error"] = success ? null : "Non-zero exit code"
            };
        }
        finally
        {
            // Cleanup temp directory
            try
            {
                var tempDir = Path.GetDirectoryName(dllPath);
                if (tempDir != null && tempDir.Contains("Compile_"))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
            catch { /* Ignore cleanup errors */ }
        }
    }

    private static (int ExitCode, string StdOut, string StdErr) RunProcess(
        string file, string args, string? workingDir = null, int? timeoutMs = null)
    {
        var psi = new ProcessStartInfo(file, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDir ?? Environment.CurrentDirectory
        };
        
        using var p = Process.Start(psi)!;
        string stdout = p.StandardOutput.ReadToEnd();
        string stderr = p.StandardError.ReadToEnd();
        
        if (timeoutMs.HasValue)
        {
            if (!p.WaitForExit(timeoutMs.Value))
            {
                try { p.Kill(); } catch { }
                return (-1, stdout, stderr + "\n<timeout>");
            }
        }
        else
        {
            p.WaitForExit();
        }
        
        return (p.ExitCode, stdout.Trim(), stderr.Trim());
    }
}