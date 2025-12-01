using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace LocalOllamaAgent;

/// <summary>
/// Workflow manager for the code generation pipeline.
/// Orchestrates: CodeGenerator ? CodeCompiler ? CodeExecutor
/// </summary>
public sealed class CodeWorkflowManager : RoundRobinGroupChatManager
{
    private readonly Func<bool>? _compileAudit;
    private readonly Func<bool>? _executeAudit;

    public CodeWorkflowManager(
        IReadOnlyList<AIAgent> agents,
        Func<bool>? compileAudit = null,
        Func<bool>? executeAudit = null) : base(agents)
    {
        MaximumIterationCount = 15; // Allow iterations for fixing compilation/runtime errors
        _compileAudit = compileAudit;
        _executeAudit = executeAudit;
    }

    protected override ValueTask<bool> ShouldTerminateAsync(
        IReadOnlyList<ChatMessage> history, 
        CancellationToken cancellationToken = default)
    {
        var lastMessage = history.LastOrDefault();
        
        // Terminate when CodeValidator reports VALIDATION_SUCCESS
        if (lastMessage?.AuthorName == "CodeValidator")
        {
            string text = lastMessage.Text;
            if (text.Contains("VALIDATION_SUCCESS", StringComparison.OrdinalIgnoreCase))
            {
                if (_compileAudit != null && !_compileAudit())
                {
                    Console.WriteLine("Validator attempted to finish without CompileCode tool output. Requesting another iteration.");
                    return ValueTask.FromResult(false);
                }

                if (_executeAudit != null && !_executeAudit())
                {
                    Console.WriteLine("Validator attempted to finish without ExecuteCode tool output. Requesting another iteration.");
                    return ValueTask.FromResult(false);
                }

                return ValueTask.FromResult(true);
            }
        }
        
        // Safety: terminate if too many iterations
        if (history.Count > MaximumIterationCount * 3) // 3 agents
        {
            Console.WriteLine($"\n! Max iterations ({MaximumIterationCount}) reached. Terminating workflow.");
            return ValueTask.FromResult(true);
        }
        
        return ValueTask.FromResult(false);
    }
}