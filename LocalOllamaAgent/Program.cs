using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using OllamaSharp;
using LocalOllamaAgent;
using System.Text;

Console.WriteLine("=== Local Multi-Agent Code Demo (MAF + OllamaSharp) ===");

// Initialize Ollama runtime
await OllamaRuntime.EnsureReadyAsync();

// Get user specification
Console.WriteLine("Enter a simple C# console app specification: ");
string? spec = Console.ReadLine()?.Trim();
if (string.IsNullOrWhiteSpace(spec)) spec = "print the first 10 squares";

// Create agents with tools
IChatClient ollamaClient = new OllamaApiClient(
    new Uri($"http://localhost:{OllamaRuntime.Port}"),
    OllamaRuntime.Model);

var codeGeneratorAgent = AgentFactory.CreateCodeGenerator(ollamaClient);
var compilerAgent = AgentFactory.CreateCompiler(ollamaClient);
var executorAgent = AgentFactory.CreateExecutor(ollamaClient);

// Build and run workflow
var workflow = AgentWorkflowBuilder
    .CreateGroupChatBuilderWith(agents => new CodeWorkflowManager(agents))
    .AddParticipants(codeGeneratorAgent, compilerAgent, executorAgent)
    .Build();

List<ChatMessage> initialMessages = new()
{
    new ChatMessage(ChatRole.User,
        $"Specification: {spec}\n\nCodeGenerator: Please generate the code.")
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
    ["CodeExecutor"] = ConsoleColor.Green
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

        if (finalMessage?.AuthorName == "CodeExecutor" &&
            finalMessage.Text?.Contains("SUCCESS", StringComparison.OrdinalIgnoreCase) == true)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("? Code generation and execution completed successfully!");
            Console.ResetColor();

            // Save generated code if available
            var generatorMessage = history?.FirstOrDefault(m =>
                m.AuthorName == "CodeGenerator" && m.Text?.Contains("```") == true);

            if (generatorMessage != null)
            {
                string code = CodeExtractor.Extract(generatorMessage.Text!);
                if (!string.IsNullOrWhiteSpace(code))
                {
                    Directory.CreateDirectory("GeneratedProgram");
                    File.WriteAllText(Path.Combine("GeneratedProgram", "Program.cs"), code);
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine("?? Saved code to GeneratedProgram/Program.cs");
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
