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
    public CodeWorkflowManager(IReadOnlyList<AIAgent> agents) : base(agents)
    {
        MaximumIterationCount = 15; // Allow iterations for fixing compilation/runtime errors
    }

    protected override ValueTask<bool> ShouldTerminateAsync(
        IReadOnlyList<ChatMessage> history, 
        CancellationToken cancellationToken = default)
    {
        var lastMessage = history.LastOrDefault();
        
        // Terminate when CodeExecutor reports SUCCESS
        if (lastMessage?.AuthorName == "CodeExecutor")
        {
            string? text = lastMessage.Text;
            if (text?.Contains("SUCCESS", StringComparison.OrdinalIgnoreCase) == true &&
                text?.Contains("Execution completed", StringComparison.OrdinalIgnoreCase) == true)
            {
                return ValueTask.FromResult(true);
            }
        }
        
        // Safety: terminate if too many iterations
        if (history.Count > MaximumIterationCount * 3) // 3 agents
        {
            Console.WriteLine($"\n?? Max iterations ({MaximumIterationCount}) reached. Terminating workflow.");
            return ValueTask.FromResult(true);
        }
        
        return ValueTask.FromResult(false);
    }
}