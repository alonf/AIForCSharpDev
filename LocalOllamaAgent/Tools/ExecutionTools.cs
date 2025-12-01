using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace LocalOllamaAgent;

/// <summary>
/// Tools for code execution phase.
/// </summary>
public static class ExecutionTools
{
    [Description("Execute compiled C# code and return execution results in a simple format.")]
    public static string ExecuteCode(
        [Description("Path to the compiled DLL to execute.")] string dllPath)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[TOOL] ExecuteCode invoked. DLL_PATH: {dllPath}");
        Console.ResetColor();

        if (!File.Exists(dllPath))
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[TOOL] ExecuteCode FAILED: DLL not found at {dllPath}");
            Console.ResetColor();
            return $"FAILED\nERROR: DLL not found at: {dllPath}";
        }

        int timeoutMs = GetTimeout();

        try
        {
            var (exitCode, stdout, stderr) = RunProcess("dotnet", $"\"{dllPath}\"",
                workingDir: Path.GetDirectoryName(dllPath),
                timeoutMs: timeoutMs);

            if (exitCode == -1)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[TOOL] ExecuteCode TIMEOUT");
                Console.ResetColor();
                return $"FAILED\nERROR: Execution timeout ({timeoutMs / 1000.0:F1} seconds)\nOUTPUT:\n{stdout}";
            }

            if (exitCode == 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[TOOL] ExecuteCode SUCCESS");
                Console.WriteLine("=== APP OUTPUT BEGIN ===");
                Console.WriteLine(stdout);
                Console.WriteLine("=== APP OUTPUT END ===");
                Console.ResetColor();
                var sb = new StringBuilder();
                sb.AppendLine("SUCCESS");
                sb.AppendLine("OUTPUT:");
                sb.AppendLine(stdout);
                if (!string.IsNullOrWhiteSpace(stderr))
                {
                    sb.AppendLine("STDERR:");
                    sb.AppendLine(stderr);
                }
                return sb.ToString();
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[TOOL] ExecuteCode FAILED: ExitCode {exitCode}");
            Console.ResetColor();
            return $"FAILED\nERROR: Program exited with code {exitCode}\nOUTPUT:\n{stdout}\n{(string.IsNullOrWhiteSpace(stderr) ? string.Empty : "STDERR:\n" + stderr)}";
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[TOOL] ExecuteCode EXCEPTION: {ex.Message}");
            Console.ResetColor();
            return $"FAILED\nERROR: Exception during execution: {ex.Message}";
        }
        finally
        {
            try
            {
                var tempDir = Path.GetDirectoryName(dllPath);
                if (tempDir != null && tempDir.Contains("Compile_"))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
            catch { }
        }
    }

    private static int GetTimeout()
    {
        if (int.TryParse(Environment.GetEnvironmentVariable("EXECUTION_TIMEOUT_MS"), out var value) && value > 0)
        {
            return value;
        }

        return 8000;
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