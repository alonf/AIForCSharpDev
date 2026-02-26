using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace LocalOllamaAgent;

/// <summary>
/// Workflow manager for the code generation pipeline.
/// Orchestrates: CodeGenerator -> CodeCompiler -> CodeExecutor -> CodeValidator
/// </summary>
public sealed class CodeWorkflowManager : RoundRobinGroupChatManager
{
    private readonly Dictionary<string, AIAgent> _agentsByName;
    private readonly Func<bool>? _compileAudit;
    private readonly Func<bool>? _executeAudit;
    private readonly int _participantCount;

    public CodeWorkflowManager(
        IReadOnlyList<AIAgent> agents,
        Func<bool>? compileAudit = null,
        Func<bool>? executeAudit = null) : base(agents)
    {
        MaximumIterationCount = 15; // Allow iterations for fixing compilation/runtime errors
        _agentsByName = BuildAgentMap(agents);
        _compileAudit = compileAudit;
        _executeAudit = executeAudit;
        _participantCount = Math.Max(1, agents.Count);
    }

    protected override ValueTask<AIAgent> SelectNextAgentAsync(
        IReadOnlyList<ChatMessage> history,
        CancellationToken cancellationToken = default)
    {
        string? nextAgentName = DetermineNextAgentName(history.LastOrDefault());
        if (nextAgentName is not null && _agentsByName.TryGetValue(nextAgentName, out var nextAgent))
        {
            return ValueTask.FromResult(nextAgent);
        }

        return base.SelectNextAgentAsync(history, cancellationToken);
    }

    protected override ValueTask<bool> ShouldTerminateAsync(
        IReadOnlyList<ChatMessage> history, 
        CancellationToken cancellationToken = default)
    {
        var lastMessage = history.LastOrDefault();

        if (TryGetValidatorTermination(lastMessage, out bool validatorDecision))
        {
            return ValueTask.FromResult(validatorDecision);
        }

        if (!HasReachedIterationLimit(history))
        {
            return ValueTask.FromResult(false);
        }

        Console.WriteLine($"\n! Max iterations ({MaximumIterationCount}) reached. Terminating workflow.");
        return ValueTask.FromResult(true);
    }

    private bool TryGetValidatorTermination(ChatMessage? lastMessage, out bool shouldTerminate)
    {
        shouldTerminate = false;

        if (!IsFromAgent(lastMessage, "CodeValidator") || !Contains(lastMessage, "VALIDATION_SUCCESS"))
        {
            return false;
        }

        if (!HasCompileAuditSatisfied())
        {
            Console.WriteLine("Validator attempted to finish without CompileCode tool output. Requesting another iteration.");
            return true;
        }

        if (!HasExecuteAuditSatisfied())
        {
            Console.WriteLine("Validator attempted to finish without ExecuteCode tool output. Requesting another iteration.");
            return true;
        }

        shouldTerminate = true;
        return true;
    }

    private bool HasCompileAuditSatisfied() => _compileAudit is null || _compileAudit();

    private bool HasExecuteAuditSatisfied() => _executeAudit is null || _executeAudit();

    private bool HasReachedIterationLimit(IReadOnlyList<ChatMessage> history)
    {
        int turnCount = CountAgentTurns(history);
        return turnCount > MaximumIterationCount * _participantCount;
    }

    private int CountAgentTurns(IReadOnlyList<ChatMessage> history)
    {
        int turns = 0;
        string? previousAgent = null;

        foreach (ChatMessage message in history)
        {
            string? author = message.AuthorName;
            if (string.IsNullOrWhiteSpace(author) || !_agentsByName.ContainsKey(author))
            {
                continue;
            }

            if (string.Equals(author, previousAgent, StringComparison.Ordinal))
            {
                continue;
            }

            turns++;
            previousAgent = author;
        }

        return turns;
    }

    private static Dictionary<string, AIAgent> BuildAgentMap(IEnumerable<AIAgent> agents)
    {
        var map = new Dictionary<string, AIAgent>(StringComparer.Ordinal);
        foreach (var agent in agents)
        {
            if (string.IsNullOrWhiteSpace(agent.Name))
            {
                continue;
            }

            map[agent.Name] = agent;
        }

        return map;
    }

    private static string? DetermineNextAgentName(ChatMessage? lastMessage)
    {
        return GetSystemRouting(lastMessage)
            ?? GetGeneratorRouting(lastMessage)
            ?? GetCompilerRouting(lastMessage)
            ?? GetExecutorRouting(lastMessage)
            ?? GetValidatorRouting(lastMessage);
    }

    private static string? GetSystemRouting(ChatMessage? lastMessage)
    {
        if (!IsSystemMessage(lastMessage))
        {
            return null;
        }

        if (Contains(lastMessage, "Repair loop context for CodeGenerator"))
        {
            return "CodeGenerator";
        }

        if (Contains(lastMessage, "Tool audit: CodeCompiler"))
        {
            return "CodeCompiler";
        }

        if (Contains(lastMessage, "Tool audit: CodeExecutor"))
        {
            return "CodeExecutor";
        }

        if (Contains(lastMessage, "CodeValidator must reply with VALIDATION_FAILED"))
        {
            return "CodeValidator";
        }

        if (Contains(lastMessage, "Validator format invalid"))
        {
            return "CodeValidator";
        }

        return null;
    }

    private static string? GetGeneratorRouting(ChatMessage? lastMessage)
    {
        if (!IsFromAgent(lastMessage, "CodeGenerator"))
        {
            return null;
        }

        return Contains(lastMessage, "CODE_READY") ? "CodeCompiler" : "CodeGenerator";
    }

    private static string? GetCompilerRouting(ChatMessage? lastMessage)
    {
        if (!IsFromAgent(lastMessage, "CodeCompiler"))
        {
            return null;
        }

        if (Contains(lastMessage, "COMPILED_SUCCESS") && Contains(lastMessage, "DLL_PATH:"))
        {
            return "CodeExecutor";
        }

        return "CodeGenerator";
    }

    private static string? GetExecutorRouting(ChatMessage? lastMessage)
    {
        if (!IsFromAgent(lastMessage, "CodeExecutor"))
        {
            return null;
        }

        if (Contains(lastMessage, "SUCCESS - Execution completed"))
        {
            return "CodeValidator";
        }

        return Contains(lastMessage, "WAITING_FOR_DLL_PATH") ? "CodeCompiler" : "CodeGenerator";
    }

    private static string? GetValidatorRouting(ChatMessage? lastMessage)
    {
        if (!IsFromAgent(lastMessage, "CodeValidator"))
        {
            return null;
        }

        if (Contains(lastMessage, "VALIDATION_SUCCESS"))
        {
            return null;
        }

        if (Contains(lastMessage, "VALIDATION_FAILED"))
        {
            return "CodeGenerator";
        }

        // Any non-conforming validator response should continue the repair loop.
        return "CodeGenerator";
    }

    private static bool IsFromAgent(ChatMessage? message, string agentName) =>
        string.Equals(message?.AuthorName, agentName, StringComparison.Ordinal);

    private static bool IsSystemMessage(ChatMessage? message) => message?.Role == ChatRole.System;

    private static bool Contains(ChatMessage? message, string token) =>
        !string.IsNullOrEmpty(message?.Text) &&
        message.Text.Contains(token, StringComparison.OrdinalIgnoreCase);
}
