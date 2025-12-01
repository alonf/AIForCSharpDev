using System.Text;
using Azure.AI.OpenAI;
using Azure.Identity;
using LocalOllamaAgent;
using LocalOllamaAgent.Tools;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using OllamaSharp;

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

int compileBaseline = ToolCallTracker.CompileCalls;
int executeBaseline = ToolCallTracker.ExecuteCalls;
Func<bool> compileAudit = () => ToolCallTracker.CompileCalls > compileBaseline;
Func<bool> executeAudit = () => ToolCallTracker.ExecuteCalls > executeBaseline;

// Create agents with tools
var codeGeneratorAgent = AgentFactory.CreateCodeGenerator(chatClient);
var compilerAgent = AgentFactory.CreateCompiler(chatClient);
var executorAgent = AgentFactory.CreateExecutor(chatClient);
var validatorAgent = AgentFactory.CreateValidator(chatClient, spec);

// Build and run workflow
var workflow = AgentWorkflowBuilder
    .CreateGroupChatBuilderWith(agents => new CodeWorkflowManager(agents, compileAudit, executeAudit))
    .AddParticipants(codeGeneratorAgent, compilerAgent, executorAgent, validatorAgent)
    .Build();

List<ChatMessage> initialMessages =
[
    new(ChatRole.System,
        "You are a coordinated team (CodeGenerator, CodeCompiler, CodeExecutor, CodeValidator). Follow the workflow contract: generator produces code + CODE_READY, compiler compiles using tools, executor runs DLL_PATH results, validator checks output against spec."),

    new(ChatRole.System, $"Original user specification: {spec}"),
    new(ChatRole.User,
        $"Specification: {spec}\n\nCodeGenerator: Call GenerateCode(specification) once, then respond with the required ```csharp block followed by CODE_READY.")
];

StreamingRun run = await InProcessExecution.StreamAsync(workflow, initialMessages);
await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

// Buffer for collecting text until newline
StringBuilder lineBuffer = new();
string? currentAgent = null;
bool compilerToolReminderSent = false;
bool executorToolReminderSent = false;
bool validatorAuditReminderSent = false;
string? lastCompilerViolationSignature = null;
string? lastExecutorViolationSignature = null;

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
        string? fullMessage = response.Messages.LastOrDefault()?.Text;
        bool compileToolInvoked = compileAudit();
        bool executeToolInvoked = executeAudit();

        if (compileToolInvoked)
        {
            compilerToolReminderSent = false;
            lastCompilerViolationSignature = null;
        }

        if (executeToolInvoked)
        {
            executorToolReminderSent = false;
            lastExecutorViolationSignature = null;
        }

        if (compileToolInvoked && executeToolInvoked)
        {
            validatorAuditReminderSent = false;
        }

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

        if (agentName is not null)
        {
            if (agentName.Equals("CodeCompiler", StringComparison.OrdinalIgnoreCase) && !compileToolInvoked)
            {
                bool violationDetected = !string.IsNullOrEmpty(fullMessage) &&
                    (fullMessage.Contains("CODE_READY", StringComparison.OrdinalIgnoreCase) ||
                     fullMessage.Contains("COMPILED_SUCCESS", StringComparison.OrdinalIgnoreCase) ||
                     fullMessage.Contains("COMPILATION_FAILED", StringComparison.OrdinalIgnoreCase) ||
                     fullMessage.Contains("\"name\": \"CompileCode\"", StringComparison.OrdinalIgnoreCase) ||
                     fullMessage.Contains("\"name\":\"CompileCode\"", StringComparison.OrdinalIgnoreCase));

                if (violationDetected)
                {
                    string signature = fullMessage!.Trim();
                    if (!compilerToolReminderSent || !string.Equals(lastCompilerViolationSignature, signature, StringComparison.Ordinal))
                    {
                        compilerToolReminderSent = true;
                        lastCompilerViolationSignature = signature;
                        await run.TrySendMessageAsync(new ChatMessage(ChatRole.System,
                            "Tool audit: CodeCompiler has not invoked CompileCode. Call the CompileCode tool now and respond with the tool output only."));
                    }
                }
            }
            else if (agentName.Equals("CodeExecutor", StringComparison.OrdinalIgnoreCase) && !executeToolInvoked)
            {
                bool violationDetected = !string.IsNullOrEmpty(fullMessage) &&
                    (fullMessage.Contains("DLL_PATH:", StringComparison.OrdinalIgnoreCase) ||
                     fullMessage.Contains("SUCCESS - Execution completed", StringComparison.OrdinalIgnoreCase) ||
                     fullMessage.Contains("EXECUTION_FAILED", StringComparison.OrdinalIgnoreCase) ||
                     fullMessage.Contains("\"name\": \"ExecuteCode\"", StringComparison.OrdinalIgnoreCase) ||
                     fullMessage.Contains("\"name\":\"ExecuteCode\"", StringComparison.OrdinalIgnoreCase));

                if (violationDetected)
                {
                    string signature = fullMessage!.Trim();
                    if (!executorToolReminderSent || !string.Equals(lastExecutorViolationSignature, signature, StringComparison.Ordinal))
                    {
                        executorToolReminderSent = true;
                        lastExecutorViolationSignature = signature;
                        await run.TrySendMessageAsync(new ChatMessage(ChatRole.System,
                            "Tool audit: CodeExecutor has not invoked ExecuteCode. Call the ExecuteCode tool now and echo its response verbatim."));
                    }
                }
            }
            else if (agentName.Equals("CodeValidator", StringComparison.OrdinalIgnoreCase))
            {
                if (!validatorAuditReminderSent && (!compileToolInvoked || !executeToolInvoked))
                {
                    if (!string.IsNullOrEmpty(text) &&
                        text.Contains("VALIDATION_SUCCESS", StringComparison.OrdinalIgnoreCase))
                    {
                        validatorAuditReminderSent = true;
                        string missing = (!compileToolInvoked, !executeToolInvoked) switch
                        {
                            (true, true) => "CompileCode and ExecuteCode",
                            (true, false) => "CompileCode",
                            (false, true) => "ExecuteCode",
                            _ => "CompileCode or ExecuteCode"
                        };
                        await run.TrySendMessageAsync(new ChatMessage(ChatRole.System,
                            $"Tool audit: {missing} tool output missing. CodeValidator must reply with VALIDATION_FAILED and instruct the responsible agent to call the tool."));
                    }
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
             finalMessage.Text.Contains("VALIDATION_SUCCESS", StringComparison.OrdinalIgnoreCase)) ||
            (finalMessage?.AuthorName == "CodeExecutor" &&
             finalMessage.Text.Contains("SUCCESS", StringComparison.OrdinalIgnoreCase)))
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("* Code generation, execution, and validation completed successfully!");
            Console.ResetColor();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"Tool calls: CompileCode={ToolCallTracker.CompileCalls - compileBaseline}, ExecuteCode={ToolCallTracker.ExecuteCalls - executeBaseline}");
            Console.ResetColor();

            // Save generated code if available
            var generatorMessage = history?.LastOrDefault(m =>
                m.AuthorName == "CodeGenerator" && m.Text.Contains("```"));

            var artifactDirectory = history is null ? null : TryGetArtifactDirectory(history);
            if (generatorMessage != null)
            {
                string code = CodeExtractor.Extract(generatorMessage.Text);
                if (!string.IsNullOrWhiteSpace(code))
                {
                    var targetDir = artifactDirectory ?? Path.Combine(Directory.GetCurrentDirectory(), "GeneratedProgram");
                    Directory.CreateDirectory(targetDir);
                    var targetPath = Path.Combine(targetDir, "Program.cs");
                    File.WriteAllText(targetPath, code);
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"-> Saved code to {targetPath}");
                    Console.ResetColor();
                }
            }
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("! Workflow completed but execution may not have succeeded.");
            Console.WriteLine($"Last agent: {finalMessage?.AuthorName}");
            Console.ResetColor();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"Tool calls observed: CompileCode={ToolCallTracker.CompileCalls - compileBaseline}, ExecuteCode={ToolCallTracker.ExecuteCalls - executeBaseline}");
            Console.ResetColor();
        }
        break;
    }
}

Console.ForegroundColor = ConsoleColor.White;
Console.WriteLine("\nDemo complete.");
Console.ResetColor();

static string? TryGetArtifactDirectory(IReadOnlyCollection<ChatMessage> history)
{
    var compilerMessage = history.LastOrDefault(m =>
        m.AuthorName == "CodeCompiler" && m.Text.Contains("DLL_PATH:", StringComparison.OrdinalIgnoreCase));

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
