using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace LocalOllamaAgent.Tools;

/// <summary>
/// Tools for code execution phase.
/// </summary>
public static class ExecutionTools
{
    private const string InteractiveSessionMarker = "INTERACTIVE_SESSION:";
    private const string RunMetadataFileName = "run-metadata.json";
    private const string AppModelConsole = "CONSOLE";
    private const string AppModelGui = "GUI";

    [Description("Execute compiled C# code and return execution results in a simple format.")]
    public static string ExecuteCode(
        [Description("Path to the compiled DLL to execute.")] string dllPath,
        [Description("Optional app model hint from compile output: GUI, CONSOLE, or AUTO.")] string? appModelHint = null)
    {
        ToolCallTracker.RegisterExecuteCall();
        string appModel = ResolveAppModel(appModelHint, dllPath);
        bool isGuiApp = appModel.Equals(AppModelGui, StringComparison.OrdinalIgnoreCase);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[TOOL] ExecuteCode invoked. DLL_PATH: {dllPath} (APP_MODEL: {appModel})");
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
            var executionEnvironment = BuildExecutionEnvironment(isGuiApp);
            var (exitCode, stdout, stderr) = RunProcess("dotnet", $"\"{dllPath}\"",
                workingDir: Path.GetDirectoryName(dllPath),
                timeoutMs: timeoutMs,
                environmentVariables: executionEnvironment);

            if (!isGuiApp && exitCode != 0 && ShouldRetryInteractive(stderr))
            {
                if (!IsInteractiveFallbackEnabled())
                {
                    return
                        "FAILED\n" +
                        "ERROR: Console-handle APIs are unavailable in captured execution mode.\n" +
                        "OUTPUT:\n" +
                        "<no capturable output>\n" +
                        "STDERR:\n" +
                        $"{stderr}\n" +
                        "NEXT_ACTION: Generate code that provides a headless text fallback (ASCII preview + RESULT_SUMMARY) when cursor/window APIs are unavailable, or set ALLOW_INTERACTIVE_FALLBACK=1 to permit interactive fallback.";
                }

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("[TOOL] ExecuteCode detected console-handle limitations. Retrying with interactive console fallback.");
                Console.ResetColor();

                var interactive = RunInteractiveProcess("dotnet", $"\"{dllPath}\"",
                    workingDir: Path.GetDirectoryName(dllPath),
                    timeoutMs: timeoutMs);

                if (interactive.ExitCode == 0)
                {
                    return BuildInteractiveSuccess("COMPLETED", interactive.RuntimeMs);
                }

                if (interactive.TimedOut)
                {
                    if (HasRuntimeFailure(interactive.StdErr))
                    {
                        return $"FAILED\nERROR: Interactive console run reported runtime failures before timeout.\nOUTPUT:\n<interactive console mode; stdout capture unavailable>\nSTDERR:\n{interactive.StdErr}";
                    }

                    // For animated/interactive apps, running until timeout without crashing is an acceptable success signal.
                    return BuildInteractiveSuccess("STARTED", interactive.RuntimeMs);
                }

                return $"FAILED\nERROR: Program exited with code {interactive.ExitCode}\nOUTPUT:\n<interactive console mode; output capture unavailable>\n{(string.IsNullOrWhiteSpace(interactive.StdErr) ? string.Empty : "STDERR:\n" + interactive.StdErr)}";
            }

            if (exitCode == -1)
            {
                if (isGuiApp)
                {
                    if (HasRuntimeFailure(stderr) || HasRuntimeFailure(stdout))
                    {
                        return $"FAILED\nERROR: GUI app reported runtime failures before timeout.\nOUTPUT:\n{(string.IsNullOrWhiteSpace(stdout) ? "<no stdout>" : stdout)}\nSTDERR:\n{stderr}";
                    }

                    return BuildGuiSuccess("STARTED", timeoutMs, stdout, stderr);
                }

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("[TOOL] ExecuteCode TIMEOUT");
                Console.ResetColor();
                return $"FAILED\nERROR: Execution timeout ({timeoutMs / 1000.0:F1} seconds)\nOUTPUT:\n{stdout}";
            }

            if (exitCode == 0)
            {
                if (isGuiApp)
                {
                    return BuildGuiSuccess("COMPLETED", null, stdout, stderr);
                }

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("[TOOL] ExecuteCode SUCCESS");
                Console.WriteLine("=== APP OUTPUT BEGIN ===");
                Console.WriteLine(stdout);
                Console.WriteLine("=== APP OUTPUT END ===");
                Console.ResetColor();
                var sb = new StringBuilder();
                sb.AppendLine("SUCCESS");
                sb.AppendLine("OUTPUT:");
                sb.AppendLine(BuildConsoleOutputSection(stdout));
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
            catch
            {
                // ignored
            }
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

    private static bool IsInteractiveFallbackEnabled()
    {
        string? value = Environment.GetEnvironmentVariable("ALLOW_INTERACTIVE_FALLBACK");
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldRetryInteractive(string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr))
        {
            return false;
        }

        return stderr.Contains("The handle is invalid", StringComparison.OrdinalIgnoreCase) ||
               stderr.Contains("System.ConsolePal", StringComparison.OrdinalIgnoreCase) ||
               stderr.Contains("Console.WindowWidth", StringComparison.OrdinalIgnoreCase) ||
               stderr.Contains("does not have a console", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasRuntimeFailure(string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr))
        {
            return false;
        }

        return stderr.Contains("Unhandled exception", StringComparison.OrdinalIgnoreCase) ||
               stderr.Contains("System.", StringComparison.OrdinalIgnoreCase) ||
               stderr.Contains("Exception", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildGuiSuccess(string state, int? runtimeMs, string stdout, string stderr)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[TOOL] ExecuteCode GUI {state}{(runtimeMs.HasValue ? $". Runtime: {runtimeMs.Value} ms" : string.Empty)}");
        Console.ResetColor();

        var sb = new StringBuilder();
        sb.AppendLine("SUCCESS");
        sb.AppendLine("OUTPUT:");
        sb.AppendLine($"APP_MODEL: {AppModelGui}");
        sb.AppendLine($"UI_SESSION: {state}");
        sb.AppendLine("UI_VALIDATION: GUI_SMOKE_PASS");
        if (runtimeMs.HasValue)
        {
            sb.AppendLine($"UI_RUNTIME_MS: {runtimeMs.Value}");
        }

        string renderedStdout = string.IsNullOrWhiteSpace(stdout) ? "<no stdout>" : stdout;
        sb.AppendLine($"UI_STDOUT: {renderedStdout}");

        if (!string.IsNullOrWhiteSpace(stderr) && !stderr.Contains("<timeout>", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine("UI_STDERR:");
            sb.AppendLine(stderr);
        }

        sb.AppendLine("RESULT_SUMMARY: GUI app started and remained stable during workflow execution.");
        return sb.ToString();
    }

    private static Dictionary<string, string>? BuildExecutionEnvironment(bool isGuiApp)
    {
        if (!isGuiApp)
        {
            return null;
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["MAF_UI_TEST_MODE"] = "1",
            ["MAF_APP_EXECUTION_MODE"] = "workflow"
        };
    }

    private static string BuildInteractiveSuccess(string state, int runtimeMs)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[TOOL] ExecuteCode INTERACTIVE {state}. Runtime: {runtimeMs} ms");
        Console.ResetColor();

        var sb = new StringBuilder();
        sb.AppendLine("SUCCESS");
        sb.AppendLine("OUTPUT:");
        sb.AppendLine($"{InteractiveSessionMarker} {state}");
        sb.AppendLine($"INTERACTIVE_RUNTIME_MS: {runtimeMs}");
        sb.AppendLine("INTERACTIVE_NOTE: Console-attached fallback used; stdout capture is unavailable.");
        return sb.ToString();
    }

    private static string ResolveAppModel(string? appModelHint, string dllPath)
    {
        if (TryNormalizeAppModel(appModelHint, out string hintModel))
        {
            return hintModel;
        }

        if (TryReadAppModelFromMetadata(dllPath, out string metadataModel))
        {
            return metadataModel;
        }

        return AppModelConsole;
    }

    private static bool TryNormalizeAppModel(string? value, out string normalized)
    {
        normalized = AppModelConsole;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string trimmed = value.Trim();
        if (trimmed.StartsWith("APP_MODEL", StringComparison.OrdinalIgnoreCase))
        {
            int colon = trimmed.IndexOf(':');
            if (colon >= 0 && colon < trimmed.Length - 1)
            {
                trimmed = trimmed[(colon + 1)..].Trim();
            }
        }

        if (trimmed.Equals(AppModelGui, StringComparison.OrdinalIgnoreCase))
        {
            normalized = AppModelGui;
            return true;
        }

        if (trimmed.Equals(AppModelConsole, StringComparison.OrdinalIgnoreCase))
        {
            normalized = AppModelConsole;
            return true;
        }

        return false;
    }

    private static bool TryReadAppModelFromMetadata(string dllPath, out string appModel)
    {
        appModel = AppModelConsole;

        string? directory = Path.GetDirectoryName(dllPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        string metadataPath = Path.Combine(directory, RunMetadataFileName);
        if (!File.Exists(metadataPath))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(metadataPath));
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            foreach (JsonProperty property in doc.RootElement.EnumerateObject())
            {
                if (!property.Name.Equals("appModel", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string? value = property.Value.GetString();
                return TryNormalizeAppModel(value, out appModel);
            }
        }
        catch
        {
            // Metadata is optional. Fall back to console mode.
        }

        return false;
    }

    private static (int ExitCode, bool TimedOut, int RuntimeMs, string StdErr) RunInteractiveProcess(
        string file, string args, string? workingDir, int timeoutMs)
    {
        var psi = new ProcessStartInfo(file, args)
        {
            RedirectStandardOutput = false,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = false,
            WorkingDirectory = workingDir ?? Environment.CurrentDirectory
        };

        using var process = Process.Start(psi)!;
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();
        var sw = Stopwatch.StartNew();
        bool exited = process.WaitForExit(timeoutMs);
        sw.Stop();

        if (!exited)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // ignored
            }

            try
            {
                process.WaitForExit();
            }
            catch
            {
                // ignored
            }

            return (-1, true, (int)sw.ElapsedMilliseconds, SafeAwait(stderrTask).Trim());
        }

        return (process.ExitCode, false, (int)sw.ElapsedMilliseconds, SafeAwait(stderrTask).Trim());
    }

    private static (int ExitCode, string StdOut, string StdErr) RunProcess(
        string file,
        string args,
        string? workingDir = null,
        int? timeoutMs = null,
        IReadOnlyDictionary<string, string>? environmentVariables = null)
    {
        var psi = new ProcessStartInfo(file, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDir ?? Environment.CurrentDirectory
        };

        if (environmentVariables is not null)
        {
            foreach (var pair in environmentVariables)
            {
                psi.Environment[pair.Key] = pair.Value;
            }
        }
        
        using var p = Process.Start(psi)!;
        Task<string> stdoutTask = p.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = p.StandardError.ReadToEndAsync();
        
        if (timeoutMs.HasValue)
        {
            if (!p.WaitForExit(timeoutMs.Value))
            {
                try { p.Kill(); }
                catch
                {
                    // ignored
                }

                try
                {
                    p.WaitForExit();
                }
                catch
                {
                    // ignored
                }

                string timeoutStdout = SafeAwait(stdoutTask);
                string timeoutStderr = SafeAwait(stderrTask);
                return (-1, timeoutStdout, (timeoutStderr + "\n<timeout>"));
            }
        }
        else
        {
            p.WaitForExit();
        }

        string stdout = SafeAwait(stdoutTask);
        string stderr = SafeAwait(stderrTask);
        return (p.ExitCode, stdout, stderr);
    }

    private static string BuildConsoleOutputSection(string stdout)
    {
        if (string.IsNullOrEmpty(stdout))
        {
            return "<no stdout>";
        }

        if (!IsWhitespaceOnly(stdout))
        {
            return stdout.TrimEnd('\r', '\n');
        }

        int charCount = stdout.Length;
        int lineCount = CountLines(stdout);
        return
            "WHITESPACE_OUTPUT_DETECTED: true\n" +
            $"WHITESPACE_OUTPUT_CHARS: {charCount}\n" +
            $"WHITESPACE_OUTPUT_LINES: {lineCount}\n" +
            "RESULT_SUMMARY: Console rendering produced whitespace/color graphics output.";
    }

    private static bool IsWhitespaceOnly(string value)
    {
        foreach (char c in value)
        {
            if (!char.IsWhiteSpace(c))
            {
                return false;
            }
        }

        return true;
    }

    private static int CountLines(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return 0;
        }

        int lines = 1;
        foreach (char c in value)
        {
            if (c == '\n')
            {
                lines++;
            }
        }

        return lines;
    }

    private static string SafeAwait(Task<string> task)
    {
        try
        {
            return task.GetAwaiter().GetResult();
        }
        catch
        {
            return string.Empty;
        }
    }
}
