using System.ComponentModel;
using System.Diagnostics;

namespace LocalOllamaAgent;

/// <summary>
/// Tools for code compilation phase.
/// </summary>
public static class CompilationTools
{
    [Description("Compile provided C# code and return compilation results.")]
    public static Dictionary<string, object?> CompileCode(
        [Description("Full C# source code to compile.")] string code)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "Compile_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "Program.cs"), code);
            File.WriteAllText(Path.Combine(tempDir, "App.csproj"), 
                "<Project Sdk=\"Microsoft.NET.Sdk\">" +
                "<PropertyGroup>" +
                "<OutputType>Exe</OutputType>" +
                "<TargetFramework>net10.0</TargetFramework>" +
                "</PropertyGroup>" +
                "</Project>");

            var (exitCode, stdout, stderr) = RunProcess("dotnet", "build --nologo -c Release", tempDir);
            
            bool success = exitCode == 0;
            string dllPath = Path.Combine(tempDir, "bin", "Release", "net10.0", "App.dll");

            return new Dictionary<string, object?>
            {
                ["success"] = success,
                ["errors"] = stderr,
                ["warnings"] = stdout,
                ["outputPath"] = success ? dllPath : null
            };
        }
        finally
        {
            // Keep temp directory for execution phase
            // Cleanup will happen after execution
        }
    }

    private static (int ExitCode, string StdOut, string StdErr) RunProcess(
        string file, string args, string? workingDir = null)
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
        p.WaitForExit();
        
        return (p.ExitCode, stdout.Trim(), stderr.Trim());
    }
}