using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace LocalOllamaAgent.Tools;

/// <summary>
/// Tools for code compilation phase.
/// </summary>
public static class CompilationTools
{
    [Description("Compile provided C# code and return compilation results in a simple, LLM-friendly format.")]
    public static string CompileCode(
        [Description("Full C# source code to compile.")] string code)
    {
        ToolCallTracker.RegisterCompileCall();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"[TOOL] CompileCode invoked. Source length: {code.Length} chars");
        Console.ResetColor();

        string tempDir = Path.Combine(Path.GetTempPath(), "Compile_" + Guid.NewGuid().ToString("N"));
        string artifactsRoot = Path.Combine(Environment.CurrentDirectory, "GeneratedArtifacts");
        Directory.CreateDirectory(tempDir);
        Directory.CreateDirectory(artifactsRoot);

        try
        {
            string normalizedCode = NormalizeSource(code);
            File.WriteAllText(Path.Combine(tempDir, "Program.cs"), normalizedCode);
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
            catch
            {
                // ignored
            }
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

    private static string NormalizeSource(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        string code = raw.Trim();
        code = StripCodeFence(code);
        code = code.Replace("\r\n", "\n");

        if (!code.Contains('\n') && code.Contains("\\n", StringComparison.Ordinal))
        {
            code = UnescapeStructuralNewlines(code);
        }

        return code;
    }

    private static string StripCodeFence(string code)
    {
        if (!code.StartsWith("```", StringComparison.Ordinal))
        {
            return code;
        }

        int firstNewLine = code.IndexOf('\n');
        if (firstNewLine >= 0)
        {
            code = code[(firstNewLine + 1)..];
        }

        int closingFence = code.LastIndexOf("```", StringComparison.Ordinal);
        if (closingFence >= 0)
        {
            code = code[..closingFence];
        }

        return code.Trim();
    }

    private static string UnescapeStructuralNewlines(string code)
    {
        var sb = new StringBuilder(code.Length);
        bool inString = false;
        bool inVerbatimString = false;
        bool inChar = false;
        bool escapeActive = false;

        for (int i = 0; i < code.Length; i++)
        {
            char c = code[i];

            if (inVerbatimString)
            {
                sb.Append(c);

                if (c == '"' && i + 1 < code.Length && code[i + 1] == '"')
                {
                    sb.Append(code[i + 1]);
                    i++;
                    continue;
                }

                if (c == '"')
                {
                    inVerbatimString = false;
                }

                continue;
            }

            if (inString)
            {
                sb.Append(c);

                if (escapeActive)
                {
                    escapeActive = false;
                    continue;
                }

                if (c == '\\')
                {
                    escapeActive = true;
                    continue;
                }

                if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (inChar)
            {
                sb.Append(c);

                if (escapeActive)
                {
                    escapeActive = false;
                    continue;
                }

                if (c == '\\')
                {
                    escapeActive = true;
                    continue;
                }

                if (c == '\'')
                {
                    inChar = false;
                }

                continue;
            }

            if (c == '@' && i + 1 < code.Length && code[i + 1] == '"')
            {
                sb.Append(c);
                inVerbatimString = true;
                continue;
            }

            if (c == '"')
            {
                sb.Append(c);
                inString = true;
                continue;
            }

            if (c == '\'')
            {
                sb.Append(c);
                inChar = true;
                continue;
            }

            if (c == '\\' && i + 1 < code.Length)
            {
                char next = code[i + 1];

                if (next == 'r' && i + 3 < code.Length && code[i + 2] == '\\' && code[i + 3] == 'n')
                {
                    sb.Append('\n');
                    i += 3;
                    continue;
                }

                if (next == 'n')
                {
                    sb.Append('\n');
                    i++;
                    continue;
                }

                if (next == 't')
                {
                    sb.Append('\t');
                    i++;
                    continue;
                }

                if (next == '\\')
                {
                    sb.Append('\\');
                    i++;
                    continue;
                }
            }

            sb.Append(c);
        }

        return sb.ToString();
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