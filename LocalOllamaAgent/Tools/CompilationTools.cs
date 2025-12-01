using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace LocalOllamaAgent;

/// <summary>
/// Tools for code compilation phase.
/// </summary>
public static class CompilationTools
{
    [Description("Compile provided C# code and return compilation results in a simple, LLM-friendly format.")]
    public static string CompileCode(
        [Description("Full C# source code to compile.")] string code)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"[TOOL] CompileCode invoked. Source length: {code?.Length ?? 0} chars");
        Console.ResetColor();

        string tempDir = Path.Combine(Path.GetTempPath(), "Compile_" + Guid.NewGuid().ToString("N"));
        string artifactsRoot = Path.Combine(Environment.CurrentDirectory, "GeneratedArtifacts");
        Directory.CreateDirectory(tempDir);
        Directory.CreateDirectory(artifactsRoot);

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
            string buildOutput = Path.Combine(tempDir, "bin", "Release", "net10.0");

            if (success)
            {
                if (!Directory.Exists(buildOutput))
                {
                    return "COMPILATION_FAILED\nERRORS:\nBuild output missing after successful compilation.";
                }

                string runDirectory = Path.Combine(artifactsRoot, $"Run_{DateTime.UtcNow:yyyyMMdd_HHmmssfff}");
                CopyDirectory(buildOutput, runDirectory);
                string dllPath = Path.Combine(runDirectory, "App.dll");

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[TOOL] CompileCode SUCCESS. DLL_PATH: {dllPath}");
                Console.ResetColor();
                return $"COMPILED_SUCCESS\nDLL_PATH: {dllPath}\nARTIFACT_DIR: {runDirectory}";
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[TOOL] CompileCode FAILED.");
                if (!string.IsNullOrWhiteSpace(stderr)) Console.WriteLine("[TOOL] ERRORS:\n" + stderr);
                if (!string.IsNullOrWhiteSpace(stdout)) Console.WriteLine("[TOOL] BUILD_OUTPUT:\n" + stdout);
                Console.ResetColor();

                var sb = new StringBuilder();
                sb.AppendLine("COMPILATION_FAILED");
                if (!string.IsNullOrWhiteSpace(stderr))
                {
                    sb.AppendLine("ERRORS:");
                    sb.AppendLine(stderr);
                }
                if (!string.IsNullOrWhiteSpace(stdout))
                {
                    sb.AppendLine("BUILD_OUTPUT:");
                    sb.AppendLine(stdout);
                }
                return sb.ToString();
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[TOOL] CompileCode EXCEPTION: {ex.Message}");
            Console.ResetColor();
            return $"COMPILATION_FAILED\nERRORS:\nException during compilation: {ex.Message}";
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
            catch { }
        }
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var fileName = Path.GetFileName(file);
            File.Copy(file, Path.Combine(destinationDir, fileName), overwrite: true);
        }

        foreach (var directory in Directory.GetDirectories(sourceDir))
        {
            var dirName = Path.GetFileName(directory);
            CopyDirectory(directory, Path.Combine(destinationDir, dirName));
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