using System.Diagnostics;
using System.Net.Http.Json;
using Microsoft.Agents.AI;

// Simple demo that ensures a local Ollama container with llama3.1:8b is running, then asks it to write C# code
// for a function that computes the Nth digit of PI, generates a console project on the fly, compiles and runs it.

// Configuration
const string OllamaContainerName = "ollama";
const string OllamaImage = "ollama/ollama:latest";
const string OllamaModel = "llama3.1:8b"; // model tag
const int OllamaPort = 11434; // API port

Console.WriteLine("=== Local Ollama Agent Demo ===");
await EnsureOllamaAndModelAsync();
Console.WriteLine("Ollama runtime and model ready. Creating agent...");

// Minimal agent wrapper using Ollama HTTP API directly (no SDK dependency).
var agent = new OllamaCodeAgent($"http://localhost:{OllamaPort}", OllamaModel);

// Prompt for code generation
string prompt = @"You are an expert C# programmer. Write a single self-contained C# method:
public static int NthDigitOfPi(int n)
that returns the nth digit (0-based after decimal point) of PI (3.14159...). Avoid using big external libraries.
Use a short spigot / BBP style algorithm or approximation good for first ~200 digits. Include a small Main demonstrating it.";

string codeResponse = await agent.GenerateAsync(prompt);
Console.WriteLine("--- Raw Model Output ---\n" + codeResponse + "\n------------------------");

// Extract code (naively) - we look for a code fence first
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
    // Try fenced code block first
    var fenceStart = text.IndexOf("```csharp", StringComparison.OrdinalIgnoreCase);
    if (fenceStart >= 0)
    {
        var after = fenceStart + 8;
        var fenceEnd = text.IndexOf("```", after, StringComparison.Ordinal);
        if (fenceEnd > after)
            return text.Substring(after, fenceEnd - after).Trim();
    }
    fenceStart = text.IndexOf("```cs", StringComparison.OrdinalIgnoreCase);
    if (fenceStart >= 0)
    {
        var after = fenceStart + 5;
        var fenceEnd = text.IndexOf("```", after, StringComparison.Ordinal);
        if (fenceEnd > after)
            return text.Substring(after, fenceEnd - after).Trim();
    }
    return string.Empty;
}

public class OllamaCodeAgent
{
    private readonly string _baseUrl;
    private readonly string _model;
    private readonly HttpClient _http = new();

    public OllamaCodeAgent(string baseUrl, string model)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _model = model;
    }

    public async Task<string> GenerateAsync(string prompt)
    {
        // Use Ollama /api/generate to stream; here we aggregate for simplicity.
        var req = new { model = _model, prompt, stream = false }; // full response
        var resp = await _http.PostAsJsonAsync(_baseUrl + "/api/generate", req);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync<GenerateResponse>();
        return json?.response ?? "(empty)";
    }

    private sealed class GenerateResponse
    {
        public string? model { get; set; }
        public string? created_at { get; set; }
        public string? response { get; set; }
        public bool done { get; set; }
    }
}
