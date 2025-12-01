using System.Diagnostics;
using System.Net.Http.Json;
using System.Linq;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OllamaSharp; // Added

// Demo: Ensure local Ollama (llama3.1:8b) is running, then use Microsoft Agent Framework with OllamaSharp
// (OllamaApiClient implements IChatClient) to generate C# code, compile, and run it.

// Configuration
const string OllamaContainerName = "ollama";
const string OllamaImage = "ollama/ollama:latest";
const string OllamaModel = "llama3.1:8b"; // model tag
const int OllamaPort = 11434; // API port

Console.WriteLine("=== Local Ollama Agent Demo (MAF + OllamaSharp) ===");
await EnsureOllamaAndModelAsync();
Console.WriteLine("Ollama runtime and model ready. Creating MAF agent...");

// Use OllamaSharp client (implements IChatClient)
IChatClient ollamaClient = new OllamaApiClient(new Uri($"http://localhost:{OllamaPort}"), OllamaModel);

AIAgent agent = ollamaClient.CreateAIAgent(
    instructions: "You are an expert C# programmer. Always answer with a complete, compilable C# console program in a single file.",
    name: "LocalCSharpCoder");

// Prompt for code generation
string prompt = @"Write a C# console app that includes a single static method:
public static int NthDigitOfPi(int n)
- It returns the nth digit (0-based after the decimal point) of PI (3.14159...).
- Use a short BBP/spigot/series approach accurate for the first ~200 digits.
- Include a Program.Main that prints NthDigitOfPi(0), NthDigitOfPi(1), NthDigitOfPi(2), NthDigitOfPi(10).
- Do not reference external packages.
- Output only code.";

AgentRunResponse run = await agent.RunAsync(prompt);
string codeResponse = run?.ToString() ?? string.Empty;
Console.WriteLine("--- Raw Model Output ---\n" + codeResponse + "\n------------------------");

// Extract code (naively) - prefer fenced blocks, else use whole text
string extracted = ExtractCSharp(codeResponse);
if (string.IsNullOrWhiteSpace(extracted))
{
    Console.WriteLine("Could not find explicit code fence. Using full response as code.");
    extracted = codeResponse;
}

// Create temp project
string tempDir = Path.Combine(Path.GetTempPath(), "PiDigitGen_" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempDir);
string projFile = Path.Combine(tempDir, "PiDigitConsole.csproj");
File.WriteAllText(projFile, "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <OutputType>Exe</OutputType>\n    <TargetFramework>net10.0</TargetFramework>\n    <ImplicitUsings>enable</ImplicitUsings>\n    <Nullable>enable</Nullable>\n  </PropertyGroup>\n</Project>\n");
string codeFile = Path.Combine(tempDir, "Program.cs");
File.WriteAllText(codeFile, extracted);

Console.WriteLine($"Building generated project in {tempDir}...");
if (!RunProcess("dotnet", $"build \"{projFile}\" -c Release"))
{
    Console.WriteLine("Build failed. Showing source:");
    Console.WriteLine(extracted);
    return;
}

string dllPath = Path.Combine(tempDir, "bin", "Release", "net10.0", "PiDigitConsole.dll");
if (File.Exists(dllPath))
{
    Console.WriteLine("Running generated program...");
    RunProcess("dotnet", $"\"{dllPath}\"");
}
else
{
    Console.WriteLine("Executable not found; build may have failed.");
}

Console.WriteLine("Demo complete.");

static async Task EnsureOllamaAndModelAsync()
{
    if (!IsContainerRunning(OllamaContainerName))
    {
        Console.WriteLine("Ollama container not running. Starting...");
        RunProcess("docker", $"run -d --gpus all -p {OllamaPort}:{OllamaPort} --name {OllamaContainerName} {OllamaImage}");
    }
    else
    {
        Console.WriteLine("Ollama container already running.");
    }

    // Wait for API
    using var http = new HttpClient();
    http.Timeout = TimeSpan.FromSeconds(5);
    for (int i = 0; i < 30; i++)
    {
        try
        {
            var resp = await http.GetAsync($"http://localhost:{OllamaPort}/api/tags");
            if (resp.IsSuccessStatusCode)
            {
                Console.WriteLine("Ollama API reachable.");
                break;
            }
        }
        catch { }
        await Task.Delay(1000);
    }

    // Ensure model present by running it once (pulls if missing)
    Console.WriteLine($"Ensuring model {OllamaModel} is available...");
    RunProcess("docker", $"exec {OllamaContainerName} ollama run {OllamaModel} \"Hello\"");
}

static bool IsContainerRunning(string name)
{
    var psi = new ProcessStartInfo("docker", "ps --filter name=" + name + " --format {{.Names}}")
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false
    };
    using var p = Process.Start(psi)!;
    string output = p.StandardOutput.ReadToEnd();
    p.WaitForExit();
    return output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Any(o => o.Trim() == name);
}

static bool RunProcess(string fileName, string args)
{
    Console.WriteLine($"> {fileName} {args}");
    var psi = new ProcessStartInfo(fileName, args)
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false
    };
    using var p = Process.Start(psi)!;
    p.OutputDataReceived += (_, e) => { if (e.Data != null) Console.WriteLine(e.Data); };
    p.ErrorDataReceived += (_, e) => { if (e.Data != null) Console.WriteLine(e.Data); };
    p.BeginOutputReadLine();
    p.BeginErrorReadLine();
    p.WaitForExit();
    return p.ExitCode == 0;
}

static string ExtractCSharp(string text)
{
    string Extract(string marker)
    {
        var idx = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return string.Empty;
        int after = idx + marker.Length; // correct offset to skip whole marker
        // Skip a single optional newline after the fence
        if (after < text.Length && (text[after] == '\r' || text[after] == '\n'))
        {
            if (text[after] == '\r' && after + 1 < text.Length && text[after + 1] == '\n') after += 2; else after += 1;
        }
        var end = text.IndexOf("```", after, StringComparison.Ordinal);
        if (end > after)
        {
            var code = text.Substring(after, end - after).TrimStart('\uFEFF').Trim();
            // If the very first line is a single stray char (e.g., 'p') followed by code, drop it
            var parts = code.Split(new[] { "\r\n", "\n" }, 2, StringSplitOptions.None);
            if (parts.Length == 2 && parts[0].Length == 1 && parts[1].TrimStart().StartsWith("using "))
                return parts[1];
            return code;
        }
        return string.Empty;
    }

    // Try common language fences
    var code = Extract("```csharp");
    if (!string.IsNullOrEmpty(code)) return code;
    code = Extract("```cs");
    if (!string.IsNullOrEmpty(code)) return code;
    code = Extract("```C#");
    if (!string.IsNullOrEmpty(code)) return code;
    code = Extract("```c#");
    if (!string.IsNullOrEmpty(code)) return code;

    // Generic triple backticks
    var genericIdx = text.IndexOf("```", StringComparison.Ordinal);
    if (genericIdx >= 0)
    {
        int after = genericIdx + 3;
        var end = text.IndexOf("```", after, StringComparison.Ordinal);
        if (end > after)
        {
            return text.Substring(after, end - after).Trim();
        }
    }

    return string.Empty;
}
