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
        // Check if container exists
        if (IsContainerExists(ContainerName))
        {
            // Container exists, check if it's running
            if (!IsContainerRunning(ContainerName))
            {
                Console.WriteLine($"Starting existing container '{ContainerName}'...");
                RunProcess("docker", $"start {ContainerName}");
            }
            else
            {
                Console.WriteLine($"Container '{ContainerName}' is already running.");
            }
        }
        else
        {
            // Container doesn't exist, create and run it
            Console.WriteLine($"Creating new container '{ContainerName}'...");
            RunProcess("docker", $"run -d --gpus all -p {Port}:{Port} --name {ContainerName} {Image}");
        }

        // Wait for Ollama API to be ready
        Console.WriteLine("Waiting for Ollama API to be ready...");
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        for (int i = 0; i < 30; i++)
        {
            try
            {
                var r = await http.GetAsync($"http://localhost:{Port}/api/tags");
                if (r.IsSuccessStatusCode)
                {
                    Console.WriteLine("Ollama API is ready.");
                    break;
                }
            }
            catch { }
            await Task.Delay(500);
        }

        // Check if model is already available
        if (!await IsModelAvailableAsync(Model))
        {
            Console.WriteLine($"Pulling model '{Model}'... (this may take a while)");
            RunProcess("docker", $"exec {ContainerName} ollama pull {Model}");
            Console.WriteLine($"Model '{Model}' is ready.");
        }
        else
        {
            Console.WriteLine($"Model '{Model}' is already available.");
        }
    }

    static bool IsContainerExists(string name)
    {
        var psi = new ProcessStartInfo("docker", $"ps -a --filter name={name} --format {{{{.Names}}}}")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi)!;
        string output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Any(l => l.Trim() == name);
    }

    static bool IsContainerRunning(string name)
    {
        var psi = new ProcessStartInfo("docker", $"ps --filter name={name} --format {{{{.Names}}}}")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi)!;
        string output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Any(l => l.Trim() == name);
    }

    static async Task<bool> IsModelAvailableAsync(string modelName)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var response = await http.GetAsync($"http://localhost:{Port}/api/tags");
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                // Simple check if model name appears in the response
                return content.Contains(modelName, StringComparison.OrdinalIgnoreCase);
            }
        }
        catch { }
        return false;
    }

    static void RunProcess(string file, string args)
    {
        var psi = new ProcessStartInfo(file, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        Process.Start(psi)?.WaitForExit();
    }
}