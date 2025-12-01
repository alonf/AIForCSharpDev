# LocalOllamaAgent

Multi-agent code generation demo for the Microsoft Agent Framework (MAF). The workflow can run entirely on a local Ollama model or switch to Azure OpenAI on demand while enforcing strict tool usage, artifact preservation, and validation gates.

## What Changed Recently

- Dual back-end selection from the console (local Ollama vs. Azure OpenAI).
- Runtime audits that force `CompileCode` and `ExecuteCode` tool calls before the validator can approve a run.
- Color-coded streaming transcript and tool call counters in the console output.
- Automatic capture of generated source (`GeneratedProgram/Program.cs`) and build artifacts (`GeneratedArtifacts/Run_<timestamp>`).
- Hardened agent instructions that require exact tool output echoes—no summaries or fabricated data.

## Agents and Responsibilities

| Agent | Role | Tools | Must Do |
|-------|------|-------|---------|
| `CodeGenerator` | Drafts the full C# console app. | `GenerateCode(spec)` template helper. | Produce a single ```csharp block followed by `CODE_READY`. |
| `CodeCompiler` | Compiles the generator output. | `CompileCode(source)`; registers tool call counts. | Echo the tool output verbatim (`COMPILED_SUCCESS`, `DLL_PATH`, `ARTIFACT_DIR`, or `COMPILATION_FAILED`). |
| `CodeExecutor` | Runs the produced DLL. | `ExecuteCode(dllPath)`; enforces configured timeout. | Echo the tool output verbatim and include the raw program stdout/stderr. |
| `CodeValidator` | Confirms the execution satisfies the specification. | Reasoning only. | Reject results if compile/execute tool output is missing or altered, or if the output does not match the spec. |

All tools are standard `[Description]`-decorated static methods, instantiated for agents via `AIFunctionFactory.Create(...)`.

## Workflow Diagram

```
User Specification
  |
  v
CodeGenerator
  Tool: GenerateCode(spec)
  |
  v
CodeCompiler
  Tool: CompileCode(source)
  |
  v
CodeExecutor
  Tool: ExecuteCode(dllPath)
  |
  v
CodeValidator
  Tool: (reasoning only)
  |
  v
Validation Outcome (SUCCESS / retry)
  ^
  |
   If VALIDATION_FAILED, feedback loops back to CodeGenerator
```

- `CodeWorkflowManager` enforces the turn order and audits that `CompileCode` and `ExecuteCode` were actually invoked before allowing `VALIDATION_SUCCESS`.
- `ToolCallTracker` increments counters inside each tool so runs can be audited after completion.

## Workflow Enforcement

- The workflow is orchestrated by `CodeWorkflowManager`, a `RoundRobinGroupChatManager` derivative that caps iterations to 15 and terminates only after a verified `VALIDATION_SUCCESS`.
- `Program.cs` wires two audits: `compileAudit` and `executeAudit`. If the validator attempts to end the run without genuine tool output, system messages push the agents back into compliance.
- `ToolCallTracker` keeps running totals of compile and execute calls so the console can report how many real tool invocations occurred during the last run.

## Running the Demo

```bash
dotnet run --project LocalOllamaAgent
```

1. Choose **Local** (default) to run with the bundled Ollama container or **Cloud** to use the hardcoded Azure OpenAI deployment.
2. Supply a plain-English console app specification when prompted—for example, `print Fibonacci numbers up to 100`.
3. Watch the color-coded transcript as the agents iterate, call tools, and stream output in real time.

At the end of a successful run the console prints:
- `Tool calls: CompileCode=<n> ExecuteCode=<m>`
- Location of the saved source file and compiled artifacts

## Prerequisites

- .NET 10 SDK (preview)
- Docker with GPU access for local Ollama inference (`ollama/ollama` container)
- `az login` access to reach Azure OpenAI if you choose the cloud path
- Optional environment variables:
  - `OLLAMA_MODEL` (defaults to `llama3.1:8b`)
  - `EXECUTION_TIMEOUT_MS` (defaults to `8000` ms)

`OllamaRuntime` will create or reuse a container named `ollama`, pull the target model if missing, and wait for the HTTP endpoint to become available before the agents start.

## Output Artifacts

- `GeneratedArtifacts/Run_<timestamp>/App.dll` (and supporting files) — copy of the successful Release build.
- `GeneratedProgram/Program.cs` — latest compiled source extracted from the generator response.
- Console log includes full stdout/stderr from compilation and execution whenever failures occur.

## Project Structure

```
LocalOllamaAgent/
  AgentFactory.cs           // Agent instruction wiring
  CodeExtractor.cs          // Pulls C# code from markdown fences
  CodeWorkflowManager.cs    // Round-robin manager with audit checks
  GeneratedArtifacts/       // Populated at runtime
  OllamaRuntime.cs          // Docker orchestration for local models
  Program.cs                // Entry point, model selection, streaming UI
  Tools/
    CodeGenerationTools.cs
    CompilationTools.cs
    ExecutionTools.cs
    ToolCallTracker.cs
```

## Extending the Demo

- Add new phases (e.g., `TestRunner` or `Optimizer` agents) by creating additional tools and updating `AgentWorkflowBuilder`.
- Swap in different Ollama models via `OLLAMA_MODEL` or point to a new Azure OpenAI deployment with minimal code changes.
- Integrate telemetry or persistence by subscribing to the streaming `WorkflowEvent`s in `Program.cs`.

This sample serves as a hardening blueprint for multi-agent workflows where tool calls must be verifiable, execution must be auditable, and handoffs between roles stay explicit.
