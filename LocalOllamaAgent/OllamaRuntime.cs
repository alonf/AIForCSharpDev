using System.Diagnostics;

namespace LocalOllamaAgent;

public static class OllamaRuntime
{
    public const string ContainerName = "ollama";
    public const string Image = "ollama/ollama:latest";
    public const string Model = "llama3.1:8b";
    public const int Port = 11434;

    public static async Task EnsureReadyAsync()
    {
        if (!IsContainerRunning(ContainerName))
        {
            RunProcess("docker", $"run -d --gpus all -p {Port}:{Port} --name {ContainerName} {Image}");
        }
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        for (int i = 0; i < 30; i++)
        {
            try { var r = await http.GetAsync($"http://localhost:{Port}/api/tags"); if (r.IsSuccessStatusCode) break; } catch { }
            await Task.Delay(500);
        }
        RunProcess("docker", $"exec {ContainerName} ollama run {Model} \"Hello\"");
    }

    static bool IsContainerRunning(string name)
    {
        var psi = new ProcessStartInfo("docker", "ps --filter name=" + name + " --format {{.Names}}") { RedirectStandardOutput = true, UseShellExecute = false };
        using var p = Process.Start(psi)!; string o = p.StandardOutput.ReadToEnd(); p.WaitForExit(); return o.Split('\n', StringSplitOptions.RemoveEmptyEntries).Any(l => l.Trim() == name);
    }

    static void RunProcess(string file, string args)
    {
        var psi = new ProcessStartInfo(file, args) { UseShellExecute = false };
        Process.Start(psi)?.WaitForExit();
    }
}