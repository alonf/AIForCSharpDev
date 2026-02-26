using System.Diagnostics;
using System.Text;
using Azure.AI.OpenAI;
using Azure.Identity;
using LocalOllamaAgent;
using LocalOllamaAgent.Tools;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OllamaSharp;

using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder
        .SetMinimumLevel(LogLevel.Warning)
        .AddFilter("Microsoft.Agents", LogLevel.Warning)
        .AddConsole();
});

Console.WriteLine("=== Multi-Agent Code Demo (MAF) ===");

// Select Model Type
Console.WriteLine("Select Model Type:");
Console.WriteLine("1. Local");
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
Console.WriteLine("\nEnter a C# app specification: ");
string? spec = Console.ReadLine()?.Trim();
if (string.IsNullOrWhiteSpace(spec)) spec = "print the first 10 squares";

int compileBaseline = ToolCallTracker.CompileCalls;
int executeBaseline = ToolCallTracker.ExecuteCalls;
Func<bool> compileAudit = () => ToolCallTracker.CompileCalls > compileBaseline;
Func<bool> executeAudit = () => ToolCallTracker.ExecuteCalls > executeBaseline;

// Create agents with tools
var codeGeneratorAgent = AgentFactory.CreateCodeGenerator(chatClient, loggerFactory);
var compilerAgent = AgentFactory.CreateCompiler(chatClient, loggerFactory);
var executorAgent = AgentFactory.CreateExecutor(chatClient, loggerFactory);
var validatorAgent = AgentFactory.CreateValidator(chatClient, spec, loggerFactory);

// Build and run workflow
var workflow = AgentWorkflowBuilder
    .CreateGroupChatBuilderWith(agents => new CodeWorkflowManager(agents, compileAudit, executeAudit))
    .AddParticipants(codeGeneratorAgent, compilerAgent, executorAgent, validatorAgent)
    .Build();

List<ChatMessage> initialMessages =
[
    new(ChatRole.System,
        "You are a coordinated team (CodeGenerator, CodeCompiler, CodeExecutor, CodeValidator). Follow the workflow contract: generator produces a JSON compile manifest + C# code + CODE_READY, compiler compiles via tools, executor runs DLL_PATH results (including GUI smoke evidence for non-console apps), validator checks output against spec."),

    new(ChatRole.System, $"Original user specification: {spec}"),
    new(ChatRole.User,
        $"Specification: {spec}\n\nCodeGenerator: Call GenerateCode(specification) once, then respond with a ```json compile manifest block, a ```csharp code block, and CODE_READY.")
];

StreamingRun run = await InProcessExecution.StreamAsync(workflow, initialMessages);
await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

// Buffer for collecting text until newline
StringBuilder lineBuffer = new();
string? currentAgent = null;
bool compilerToolReminderSent = false;
bool executorToolReminderSent = false;
bool validatorAuditReminderSent = false;
bool validatorFormatReminderSent = false;
string? lastCompilerViolationSignature = null;
string? lastExecutorViolationSignature = null;
string? lastValidatorFormatSignature = null;
string? lastCompileFailureHintSignature = null;
string? lastExecutionFailureHintSignature = null;
string? lastValidationFailureHintSignature = null;
var lastAgentResponseText = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

// Color mapping for agents
var agentColors = new Dictionary<string, ConsoleColor>
{
    ["CodeGenerator"] = ConsoleColor.Cyan,
    ["CodeCompiler"] = ConsoleColor.Yellow,
    ["CodeExecutor"] = ConsoleColor.Green,
    ["CodeValidator"] = ConsoleColor.Magenta
};

Console.WriteLine("\n=== Workflow Execution ===\n");
bool showFrameworkDebugEvents = string.Equals(
    Environment.GetEnvironmentVariable("MAF_DEBUG_EVENTS"),
    "1",
    StringComparison.OrdinalIgnoreCase);

var auditStrategies = new Dictionary<string, Func<string?, string, bool, bool, Task>>(StringComparer.OrdinalIgnoreCase)
{
    ["CodeCompiler"] = (fullMessage, _, compileInvoked, _) => EnforceCompilerAuditAsync(fullMessage, compileInvoked),
    ["CodeExecutor"] = (fullMessage, _, _, executeInvoked) => EnforceExecutorAuditAsync(fullMessage, executeInvoked),
    ["CodeValidator"] = (_, text, compileInvoked, executeInvoked) =>
        EnforceValidatorAuditAsync(text, compileInvoked, executeInvoked)
};

int eventCount = 0;
try
{
    bool shouldStopWatching = false;

    await foreach (WorkflowEvent evt in run.WatchStreamAsync())
    {
        eventCount++;
        switch (evt)
        {
            case WorkflowOutputEvent output:
                HandleWorkflowOutput(output);
                shouldStopWatching = true;
                break;

            case AgentResponseUpdateEvent update:
                await HandleAgentUpdateAsync(update);
                break;

            default:
                WriteUnhandledFrameworkEvent(evt);
                break;
        }

        if (shouldStopWatching)
        {
            break;
        }
    }

    if (eventCount == 0)
    {
        WriteNoEventsWarning();
    }

} // end try
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"\nWorkflow error: {ex.GetType().Name}: {ex.Message}");
    Console.WriteLine(ex.StackTrace);
    Console.ResetColor();
}

// Offer interactive re-launch if a compiled DLL exists
await OfferInteractiveLaunchAsync();

Console.ForegroundColor = ConsoleColor.White;
Console.WriteLine("\nDemo complete.");
Console.ResetColor();

async Task OfferInteractiveLaunchAsync()
{
    string? dllPath = FindLatestCompiledDll();
    if (dllPath is null)
    {
        return;
    }

    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine($"\nCompiled app found: {dllPath}");
    Console.Write("Launch interactively? [Y/n]: ");
    Console.ResetColor();

    string? answer = Console.ReadLine()?.Trim();
    if (!string.IsNullOrEmpty(answer) &&
        !answer.Equals("Y", StringComparison.OrdinalIgnoreCase) &&
        !answer.Equals("yes", StringComparison.OrdinalIgnoreCase))
    {
        return;
    }

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("Launching app... Close the app window when done.");
    Console.ResetColor();

    try
    {
        var psi = new ProcessStartInfo("dotnet", $"\"{dllPath}\"")
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(dllPath)
        };
        // Do NOT set MAF_UI_TEST_MODE so the app stays open
        using var process = Process.Start(psi);
        if (process is not null)
        {
            await process.WaitForExitAsync();
        }
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Launch failed: {ex.Message}");
        Console.ResetColor();
    }
}

string? FindLatestCompiledDll()
{
    string artifactsRoot = Path.Combine(Environment.CurrentDirectory, "GeneratedArtifacts");
    if (!Directory.Exists(artifactsRoot))
    {
        return null;
    }

    var runDirs = Directory.GetDirectories(artifactsRoot, "Run_*")
        .OrderByDescending(d => d)
        .ToArray();

    foreach (string dir in runDirs)
    {
        string candidate = Path.Combine(dir, "App.dll");
        if (File.Exists(candidate))
        {
            return candidate;
        }
    }

    return null;
}

async Task HandleAgentUpdateAsync(AgentResponseUpdateEvent update)
{
    var response = update.AsResponse();
    string? agentName = response.Messages.LastOrDefault()?.AuthorName;
    string text = response.Text;
    string? fullMessage = response.Messages.LastOrDefault()?.Text;

    bool compileToolInvoked = compileAudit();
    bool executeToolInvoked = executeAudit();
    ResetReminderState(compileToolInvoked, executeToolInvoked);

    PrintAgentHeaderIfNeeded(agentName);

    if (!TryRenderAgentText(agentName, text))
    {
        return;
    }

    await EnforceToolAuditAsync(agentName, fullMessage, text, compileToolInvoked, executeToolInvoked);
    await InjectRepairFeedbackAsync(agentName, fullMessage);
}

void ResetReminderState(bool compileToolInvoked, bool executeToolInvoked)
{
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
}

void PrintAgentHeaderIfNeeded(string? agentName)
{
    if (string.IsNullOrEmpty(agentName) || agentName == currentAgent)
    {
        return;
    }

    FlushLineBuffer();
    Console.WriteLine();
    WriteAgentHeader(agentName);
    currentAgent = agentName;
}

void FlushLineBuffer()
{
    if (lineBuffer.Length == 0)
    {
        return;
    }

    Console.WriteLine(lineBuffer.ToString());
    lineBuffer.Clear();
}

void WriteAgentHeader(string agentName)
{
    if (!agentColors.TryGetValue(agentName, out var color))
    {
        Console.WriteLine($"[{agentName}]");
        return;
    }

    Console.ForegroundColor = color;
    Console.WriteLine($"[{agentName}]");
    Console.ResetColor();
}

bool TryRenderAgentText(string? agentName, string text)
{
    if (string.IsNullOrEmpty(text))
    {
        return true;
    }

    string delta = GetDeltaText(agentName, text);
    if (string.IsNullOrEmpty(delta))
    {
        return false;
    }

    lineBuffer.Append(delta);
    FlushCompletedLines();
    return true;
}

string GetDeltaText(string? agentName, string text)
{
    if (string.IsNullOrEmpty(agentName))
    {
        return text;
    }

    if (!lastAgentResponseText.TryGetValue(agentName, out var previousText))
    {
        lastAgentResponseText[agentName] = text;
        return text;
    }

    if (text.StartsWith(previousText, StringComparison.Ordinal))
    {
        lastAgentResponseText[agentName] = text;
        return text[previousText.Length..];
    }

    if (previousText.StartsWith(text, StringComparison.Ordinal))
    {
        lastAgentResponseText[agentName] = text;
        return string.Empty;
    }

    lastAgentResponseText[agentName] = text;
    return text;
}

void FlushCompletedLines()
{
    string buffered = lineBuffer.ToString();
    int lastNewline = buffered.LastIndexOf('\n');
    if (lastNewline < 0)
    {
        return;
    }

    string toPrint = buffered[..(lastNewline + 1)];
    WriteWithCurrentAgentColor(toPrint, appendNewLine: false);

    lineBuffer.Clear();
    if (lastNewline < buffered.Length - 1)
    {
        lineBuffer.Append(buffered[(lastNewline + 1)..]);
    }
}

void WriteWithCurrentAgentColor(string text, bool appendNewLine)
{
    Action<string> writer = appendNewLine ? Console.WriteLine : Console.Write;

    if (currentAgent is null || !agentColors.TryGetValue(currentAgent, out var color))
    {
        writer(text);
        return;
    }

    Console.ForegroundColor = color;
    writer(text);
    Console.ResetColor();
}

async Task EnforceToolAuditAsync(
    string? agentName,
    string? fullMessage,
    string text,
    bool compileToolInvoked,
    bool executeToolInvoked)
{
    if (string.IsNullOrWhiteSpace(agentName))
    {
        return;
    }

    if (!auditStrategies.TryGetValue(agentName, out var strategy))
    {
        return;
    }

    await strategy(fullMessage, text, compileToolInvoked, executeToolInvoked);
}

async Task EnforceCompilerAuditAsync(string? fullMessage, bool compileToolInvoked)
{
    if (compileToolInvoked || !IsCompilerViolation(fullMessage))
    {
        return;
    }

    string signature = fullMessage!.Trim();
    bool repeatedViolation = compilerToolReminderSent &&
        string.Equals(lastCompilerViolationSignature, signature, StringComparison.Ordinal);
    if (repeatedViolation)
    {
        return;
    }

    compilerToolReminderSent = true;
    lastCompilerViolationSignature = signature;
    await run.TrySendMessageAsync(new ChatMessage(ChatRole.System,
        "Tool audit: CodeCompiler has not invoked CompileCode. Call the CompileCode tool now and respond with the tool output only."));
}

bool IsCompilerViolation(string? fullMessage)
{
    if (string.IsNullOrEmpty(fullMessage))
    {
        return false;
    }

    return fullMessage.Contains("CODE_READY", StringComparison.OrdinalIgnoreCase) ||
           fullMessage.Contains("COMPILED_SUCCESS", StringComparison.OrdinalIgnoreCase) ||
           fullMessage.Contains("COMPILATION_FAILED", StringComparison.OrdinalIgnoreCase) ||
           fullMessage.Contains("\"name\": \"CompileCode\"", StringComparison.OrdinalIgnoreCase) ||
           fullMessage.Contains("\"name\":\"CompileCode\"", StringComparison.OrdinalIgnoreCase);
}

async Task EnforceExecutorAuditAsync(string? fullMessage, bool executeToolInvoked)
{
    if (executeToolInvoked || !IsExecutorViolation(fullMessage))
    {
        return;
    }

    string signature = fullMessage!.Trim();
    bool repeatedViolation = executorToolReminderSent &&
        string.Equals(lastExecutorViolationSignature, signature, StringComparison.Ordinal);
    if (repeatedViolation)
    {
        return;
    }

    executorToolReminderSent = true;
    lastExecutorViolationSignature = signature;
    await run.TrySendMessageAsync(new ChatMessage(ChatRole.System,
        "Tool audit: CodeExecutor has not invoked ExecuteCode. Call the ExecuteCode tool now and echo its response verbatim."));
}

bool IsExecutorViolation(string? fullMessage)
{
    if (string.IsNullOrEmpty(fullMessage))
    {
        return false;
    }

    return fullMessage.Contains("DLL_PATH:", StringComparison.OrdinalIgnoreCase) ||
           fullMessage.Contains("SUCCESS - Execution completed", StringComparison.OrdinalIgnoreCase) ||
           fullMessage.Contains("EXECUTION_FAILED", StringComparison.OrdinalIgnoreCase) ||
           fullMessage.Contains("\"name\": \"ExecuteCode\"", StringComparison.OrdinalIgnoreCase) ||
           fullMessage.Contains("\"name\":\"ExecuteCode\"", StringComparison.OrdinalIgnoreCase);
}

async Task EnforceValidatorAuditAsync(string text, bool compileToolInvoked, bool executeToolInvoked)
{
    bool hasValidationDecision =
        text.Contains("VALIDATION_SUCCESS", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("VALIDATION_FAILED", StringComparison.OrdinalIgnoreCase);
    if (!string.IsNullOrWhiteSpace(text) && !hasValidationDecision)
    {
        string signature = text.Trim();
        bool repeatedFormatViolation = validatorFormatReminderSent &&
                                       string.Equals(lastValidatorFormatSignature, signature, StringComparison.Ordinal);
        if (repeatedFormatViolation)
        {
            return;
        }

        validatorFormatReminderSent = true;
        lastValidatorFormatSignature = signature;
        await run.TrySendMessageAsync(new ChatMessage(ChatRole.System,
            "Validator format invalid. Reply with either `VALIDATION_SUCCESS` or `VALIDATION_FAILED` with `REASON:`, `EVIDENCE:`, `NEXT_ACTION:`. Do not emit tool-call syntax."));
        return;
    }

    validatorFormatReminderSent = false;
    lastValidatorFormatSignature = null;

    if (validatorAuditReminderSent || (compileToolInvoked && executeToolInvoked))
    {
        return;
    }

    bool isPrematureSuccess = !string.IsNullOrEmpty(text) &&
                              text.Contains("VALIDATION_SUCCESS", StringComparison.OrdinalIgnoreCase);
    if (!isPrematureSuccess)
    {
        return;
    }

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

async Task InjectRepairFeedbackAsync(string? agentName, string? fullMessage)
{
    if (string.IsNullOrWhiteSpace(agentName) || string.IsNullOrWhiteSpace(fullMessage))
    {
        return;
    }

    if (agentName.Equals("CodeCompiler", StringComparison.OrdinalIgnoreCase) &&
        fullMessage.Contains("COMPILATION_FAILED", StringComparison.OrdinalIgnoreCase))
    {
        await EmitCompileFailureFeedbackAsync(fullMessage);
        return;
    }

    if (agentName.Equals("CodeExecutor", StringComparison.OrdinalIgnoreCase) &&
        fullMessage.Contains("EXECUTION_FAILED", StringComparison.OrdinalIgnoreCase))
    {
        await EmitExecutionFailureFeedbackAsync(fullMessage);
        return;
    }

    if (agentName.Equals("CodeValidator", StringComparison.OrdinalIgnoreCase) &&
        fullMessage.Contains("VALIDATION_FAILED", StringComparison.OrdinalIgnoreCase))
    {
        await EmitValidationFailureFeedbackAsync(fullMessage);
    }
}

async Task EmitCompileFailureFeedbackAsync(string fullMessage)
{
    if (IsDuplicateFeedback(fullMessage, ref lastCompileFailureHintSignature))
    {
        return;
    }

    string primaryError = ExtractFailureDetail(
        fullMessage,
        preferredPrefix: "PRIMARY_ERROR:",
        fallbackHints: ["error CS", ": error ", "error "]);
    string normalized = NormalizeForPrompt(primaryError);

    WriteWorkflowFeedback($"Compile failure context: {normalized}");
    await run.TrySendMessageAsync(new ChatMessage(ChatRole.System,
        $"Repair loop context for CodeGenerator: previous compile failed. PRIMARY_ERROR: {normalized}. Rewrite code to fix this exact issue first, then output a ```json compile manifest block, a full ```csharp program, and CODE_READY."));
}

async Task EmitExecutionFailureFeedbackAsync(string fullMessage)
{
    if (IsDuplicateFeedback(fullMessage, ref lastExecutionFailureHintSignature))
    {
        return;
    }

    string primaryError = ExtractFailureDetail(
        fullMessage,
        preferredPrefix: "ERROR:",
        fallbackHints: ["Unhandled exception", "FAILED", "STDERR:"]);
    string normalized = NormalizeForPrompt(primaryError);

    WriteWorkflowFeedback($"Execution failure context: {normalized}");
    await run.TrySendMessageAsync(new ChatMessage(ChatRole.System,
        $"Repair loop context for CodeGenerator: previous execution failed. ERROR: {normalized}. Rewrite code to avoid this runtime failure, then output a ```json compile manifest block, a full ```csharp program, and CODE_READY."));
}

async Task EmitValidationFailureFeedbackAsync(string fullMessage)
{
    if (IsDuplicateFeedback(fullMessage, ref lastValidationFailureHintSignature))
    {
        return;
    }

    string reason = ExtractFailureDetail(
        fullMessage,
        preferredPrefix: "REASON:",
        fallbackHints: ["VALIDATION_FAILED", "EVIDENCE:", "NEXT_ACTION:"]);
    string normalized = NormalizeForPrompt(reason);

    WriteWorkflowFeedback($"Validation failure context: {normalized}");
    await run.TrySendMessageAsync(new ChatMessage(ChatRole.System,
        $"Repair loop context for CodeGenerator: validator rejected the result. REASON: {normalized}. Address this mismatch in the next rewrite, then output a ```json compile manifest block, a full ```csharp program, and CODE_READY."));
}

bool IsDuplicateFeedback(string message, ref string? signatureSlot)
{
    string signature = message.Trim();
    if (string.Equals(signatureSlot, signature, StringComparison.Ordinal))
    {
        return true;
    }

    signatureSlot = signature;
    return false;
}

string ExtractFailureDetail(string message, string preferredPrefix, IReadOnlyList<string> fallbackHints)
{
    var lines = message.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    string? preferred = lines.FirstOrDefault(l => l.StartsWith(preferredPrefix, StringComparison.OrdinalIgnoreCase));
    if (!string.IsNullOrWhiteSpace(preferred))
    {
        return preferred;
    }

    foreach (string hint in fallbackHints)
    {
        string? candidate = lines.FirstOrDefault(l => l.Contains(hint, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(candidate))
        {
            return candidate;
        }
    }

    string? firstInformative = lines.FirstOrDefault(l =>
        !l.Equals("COMPILATION_FAILED", StringComparison.OrdinalIgnoreCase) &&
        !l.Equals("EXECUTION_FAILED", StringComparison.OrdinalIgnoreCase) &&
        !l.Equals("VALIDATION_FAILED", StringComparison.OrdinalIgnoreCase) &&
        !l.Equals("OUTPUT:", StringComparison.OrdinalIgnoreCase));
    return firstInformative ?? "No detailed failure line provided.";
}

string NormalizeForPrompt(string value)
{
    string normalized = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
    const int maxLen = 320;
    return normalized.Length <= maxLen ? normalized : normalized[..maxLen] + "...";
}

void WriteWorkflowFeedback(string message)
{
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine($"[WF] {message}");
    Console.ResetColor();
}

void HandleWorkflowOutput(WorkflowOutputEvent output)
{
    FlushLineBufferForCurrentAgent();

    var history = output.As<List<ChatMessage>>();
    var finalMessage = history?.LastOrDefault();

    Console.ForegroundColor = ConsoleColor.White;
    Console.WriteLine("\n=== Workflow Complete ===\n");
    Console.ResetColor();

    if (IsSuccessFinalMessage(finalMessage))
    {
        PrintSuccessSummary(history);
        return;
    }

    PrintFailureSummary(finalMessage);
}

void FlushLineBufferForCurrentAgent()
{
    if (lineBuffer.Length == 0)
    {
        return;
    }

    WriteWithCurrentAgentColor(lineBuffer.ToString(), appendNewLine: true);
    lineBuffer.Clear();
}

bool IsSuccessFinalMessage(ChatMessage? finalMessage)
{
    if (finalMessage?.Text is null)
    {
        return false;
    }

    return finalMessage.AuthorName == "CodeValidator" &&
           finalMessage.Text.Contains("VALIDATION_SUCCESS", StringComparison.OrdinalIgnoreCase);
}

void PrintSuccessSummary(IReadOnlyCollection<ChatMessage>? history)
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("* Code generation, execution, and validation completed successfully!");
    Console.ResetColor();

    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine($"Tool calls: CompileCode={ToolCallTracker.CompileCalls - compileBaseline}, ExecuteCode={ToolCallTracker.ExecuteCalls - executeBaseline}");
    Console.ResetColor();

    SaveGeneratedCode(history);
}

void SaveGeneratedCode(IReadOnlyCollection<ChatMessage>? history)
{
    if (history is null)
    {
        return;
    }

    var generatorMessage = history.LastOrDefault(m =>
        m.AuthorName == "CodeGenerator" && m.Text.Contains("```"));
    if (generatorMessage is null)
    {
        return;
    }

    string code = CodeExtractor.Extract(generatorMessage.Text);
    if (string.IsNullOrWhiteSpace(code))
    {
        return;
    }

    var artifactDirectory = TryGetArtifactDirectory(history);
    var targetDir = artifactDirectory ?? Path.Combine(Directory.GetCurrentDirectory(), "GeneratedProgram");
    Directory.CreateDirectory(targetDir);

    var targetPath = Path.Combine(targetDir, "Program.cs");
    File.WriteAllText(targetPath, code);

    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine($"-> Saved code to {targetPath}");
    Console.ResetColor();
}

void PrintFailureSummary(ChatMessage? finalMessage)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine("! Workflow completed but execution may not have succeeded.");
    Console.WriteLine($"Last agent: {finalMessage?.AuthorName}");
    Console.ResetColor();

    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine($"Tool calls observed: CompileCode={ToolCallTracker.CompileCalls - compileBaseline}, ExecuteCode={ToolCallTracker.ExecuteCalls - executeBaseline}");
    Console.ResetColor();
}

void WriteUnhandledFrameworkEvent(WorkflowEvent evt)
{
    if (!showFrameworkDebugEvents)
    {
        return;
    }

    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine($"[DEBUG] Unhandled event type: {evt.GetType().Name}");
    Console.ResetColor();
}

void WriteNoEventsWarning()
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine("WARNING: Workflow produced zero events. Possible causes:");
    Console.WriteLine("  - Package API change (event types renamed)");
    Console.WriteLine("  - Model failed to respond (check Ollama container logs)");
    Console.WriteLine("  - Workflow terminated before any agent ran");
    Console.ResetColor();
}

static string? TryGetArtifactDirectory(IReadOnlyCollection<ChatMessage> history)
{
    var compilerMessage = history.LastOrDefault(m =>
        m.AuthorName == "CodeCompiler" && m.Text.Contains("DLL_PATH:", StringComparison.OrdinalIgnoreCase));

    if (compilerMessage?.Text is null)
    {
        return null;
    }

    return (from line in compilerMessage.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) 
        where line.StartsWith("ARTIFACT_DIR:", StringComparison.OrdinalIgnoreCase) 
        select line.Substring("ARTIFACT_DIR:".Length).Trim() into path 
        select string.IsNullOrWhiteSpace(path) ? null : path).FirstOrDefault();
}
