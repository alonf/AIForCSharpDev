using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using OllamaSharp;
using LocalOllamaAgent;
using System.Text;
using Azure.AI.OpenAI;
using Azure.Identity;

Console.WriteLine("=== Multi-Agent Code Demo (MAF) ===");

// Select Model Type
Console.WriteLine("Select Model Type:");
Console.WriteLine("1. Local (Ollama - llama3.1:8b)");
Console.WriteLine("2. Cloud (Azure OpenAI - Model Router)");
Console.Write("Choice [1]: ");
string? choice = Console.ReadLine()?.Trim();

IChatClient chatClient;

if (choice == "2")
{
    Console.WriteLine("Initializing Cloud Model (Azure OpenAI)...");
    var endpoint = new Uri("https://alonlecturedemo-resource.cognitiveservices.azure.com/");
    var credential = new DefaultAzureCredential();
    string deploymentName = "model-router";

    var azureClient = new AzureOpenAIClient(endpoint, credential);
    chatClient = azureClient.GetChatClient(deploymentName).AsIChatClient();
    Console.WriteLine("Cloud Model Ready.");
}
else
{
    Console.WriteLine("Initializing Local Model (Ollama)...");
    // Initialize Ollama runtime
    await OllamaRuntime.EnsureReadyAsync();
    
    chatClient = new OllamaApiClient(
        new Uri($"http://localhost:{OllamaRuntime.Port}"),
        OllamaRuntime.Model);
    Console.WriteLine("Local Model Ready.");
}

// Get user specification
Console.WriteLine("\nEnter a simple C# console app specification: ");
string? spec = Console.ReadLine()?.Trim();
if (string.IsNullOrWhiteSpace(spec)) spec = "print the first 10 squares";

// Create agents with tools
var codeGeneratorAgent = AgentFactory.CreateCodeGenerator(chatClient);
var compilerAgent = AgentFactory.CreateCompiler(chatClient);
var executorAgent = AgentFactory.CreateExecutor(chatClient);
var validatorAgent = AgentFactory.CreateValidator(chatClient);

// Build and run workflow
var workflow = AgentWorkflowBuilder
    .CreateGroupChatBuilderWith(agents => new CodeWorkflowManager(agents))
    .AddParticipants(codeGeneratorAgent, compilerAgent, executorAgent, validatorAgent)
    .Build();

List<ChatMessage> initialMessages = new()
{
    new ChatMessage(ChatRole.System,
        "You are a coordinated team (CodeGenerator, CodeCompiler, CodeExecutor, CodeValidator). Follow the workflow contract: generator produces code + CODE_READY, compiler compiles using tools, executor runs DLL_PATH results, validator checks output against spec."),
    new ChatMessage(ChatRole.User,
        $"Specification: {spec}\n\nCodeGenerator: Call GenerateCode(specification) once, then respond with the required ```csharp block followed by CODE_READY.")
};

StreamingRun run = await InProcessExecution.StreamAsync(workflow, initialMessages);
await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

// Buffer for collecting text until newline
StringBuilder lineBuffer = new();
string? currentAgent = null;

// Color mapping for agents
var agentColors = new Dictionary<string, ConsoleColor>
{
    ["CodeGenerator"] = ConsoleColor.Cyan,
    ["CodeCompiler"] = ConsoleColor.Yellow,
    ["CodeExecutor"] = ConsoleColor.Green,
    ["CodeValidator"] = ConsoleColor.Magenta
};

Console.WriteLine("\n=== Workflow Execution ===\n");

// Watch workflow execution with improved output handling
await foreach (WorkflowEvent evt in run.WatchStreamAsync())
{
    if (evt is AgentRunUpdateEvent update)
    {
        var response = update.AsResponse();
        string? agentName = response.Messages.LastOrDefault()?.AuthorName;
        string text = response.Text;

        // Agent changed - flush buffer and show agent name with color
        if (!string.IsNullOrEmpty(agentName) && agentName != currentAgent)
        {
            if (lineBuffer.Length > 0)
            {
                Console.WriteLine(lineBuffer.ToString());
                lineBuffer.Clear();
            }
            
            // Print agent name with color
            Console.WriteLine();
            if (agentColors.TryGetValue(agentName, out var color))
            {
                Console.ForegroundColor = color;
                Console.WriteLine($"[{agentName}]");
                Console.ResetColor();
            }
            else
            {
                Console.WriteLine($"[{agentName}]");
            }
            
            currentAgent = agentName;
        }

        // Buffer text and only print on newlines with agent color
        if (!string.IsNullOrEmpty(text))
        {
            lineBuffer.Append(text);

            // Check if we have complete lines to print
            string buffered = lineBuffer.ToString();
            int lastNewline = buffered.LastIndexOf('\n');

            if (lastNewline >= 0)
            {
                // Print everything up to and including the last newline with agent color
                string toPrint = buffered.Substring(0, lastNewline + 1);
                
                if (currentAgent != null && agentColors.TryGetValue(currentAgent, out var color))
                {
                    Console.ForegroundColor = color;
                }
                Console.Write(toPrint);
                Console.ResetColor();

                // Keep remainder in buffer
                lineBuffer.Clear();
                if (lastNewline < buffered.Length - 1)
                {
                    lineBuffer.Append(buffered.Substring(lastNewline + 1));
                }
            }
        }
    }
    else if (evt is WorkflowOutputEvent output)
    {
        // Flush any remaining buffer
        if (lineBuffer.Length > 0)
        {
            if (currentAgent != null && agentColors.TryGetValue(currentAgent, out var color))
            {
                Console.ForegroundColor = color;
            }
            Console.WriteLine(lineBuffer.ToString());
            Console.ResetColor();
        }

        var history = output.As<List<ChatMessage>>();
        var finalMessage = history?.LastOrDefault();

        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("\n=== Workflow Complete ===\n");
        Console.ResetColor();

        if ((finalMessage?.AuthorName == "CodeValidator" &&
             finalMessage.Text?.Contains("VALIDATION_SUCCESS", StringComparison.OrdinalIgnoreCase) == true) ||
            (finalMessage?.AuthorName == "CodeExecutor" &&
             finalMessage.Text?.Contains("SUCCESS", StringComparison.OrdinalIgnoreCase) == true))
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("? Code generation, execution, and validation completed successfully!");
            Console.ResetColor();

            // Save generated code if available
            var generatorMessage = history?.LastOrDefault(m =>
                m.AuthorName == "CodeGenerator" && m.Text?.Contains("```") == true);

            var artifactDirectory = history is null ? null : TryGetArtifactDirectory(history);
            if (generatorMessage != null)
            {
                string code = CodeExtractor.Extract(generatorMessage.Text!);
                if (!string.IsNullOrWhiteSpace(code))
                {
                    var targetDir = artifactDirectory ?? Path.Combine(Directory.GetCurrentDirectory(), "GeneratedProgram");
                    Directory.CreateDirectory(targetDir);
                    var targetPath = Path.Combine(targetDir, "Program.cs");
                    File.WriteAllText(targetPath, code);
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"?? Saved code to {targetPath}");
                    Console.ResetColor();
                }
            }
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("?? Workflow completed but execution may not have succeeded.");
            Console.WriteLine($"Last agent: {finalMessage?.AuthorName}");
            Console.ResetColor();
        }
        break;
    }
}

Console.ForegroundColor = ConsoleColor.White;
Console.WriteLine("\n?? Demo complete.");
Console.ResetColor();

static string? TryGetArtifactDirectory(IReadOnlyCollection<ChatMessage> history)
{
    var compilerMessage = history.LastOrDefault(m =>
        m.AuthorName == "CodeCompiler" && m.Text?.Contains("DLL_PATH:", StringComparison.OrdinalIgnoreCase) == true);

    if (compilerMessage?.Text is null)
    {
        return null;
    }

    foreach (var line in compilerMessage.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        if (line.StartsWith("ARTIFACT_DIR:", StringComparison.OrdinalIgnoreCase))
        {
            var path = line.Substring("ARTIFACT_DIR:".Length).Trim();
            return string.IsNullOrWhiteSpace(path) ? null : path;
        }
    }

    return null;
}
