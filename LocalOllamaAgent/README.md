# LocalOllamaAgent - Multi-Agent Code Generation System

## Architecture

This project demonstrates a **proper agentic system** using Microsoft Agent Framework (MAF) where:
- Each agent has a **single responsibility**
- Agents collaborate through a **workflow**
- Shared **tools** (decorated methods) provide capabilities
- Clean separation of concerns

## Agent Roles

### 1. **CodeGenerator Agent** ??
- **Responsibility**: Generate C# code from user specifications
- **Tools**: 
  - `CodeGenerationTools.GenerateCode` - Template helper
- **Behavior**: 
  - Reads specifications
  - Generates complete C# console applications
  - Outputs code in markdown fences
  - Waits for compiler feedback

### 2. **CodeCompiler Agent** ??
- **Responsibility**: Compile code and provide feedback
- **Tools**: 
  - `CompilationTools.CompileCode` - Compiles C# code using dotnet CLI
- **Behavior**: 
  - Extracts code from markdown
  - Compiles using dotnet build
  - Reports success with `COMPILED_SUCCESS` ? passes to Executor
  - Reports errors ? provides feedback to Generator

### 3. **CodeExecutor Agent** ??
- **Responsibility**: Execute compiled code and report results
- **Tools**: 
  - `ExecutionTools.ExecuteCode` - Runs compiled assemblies
- **Behavior**: 
  - Waits for `COMPILED_SUCCESS`
  - Executes the compiled DLL
  - Reports output to user
  - Replies with `SUCCESS` ? workflow terminates
  - Reports runtime errors ? feedback to Generator

## Workflow

```
User Spec
    ?
CodeGenerator (generates code)
    ?
CodeCompiler (compiles, validates)
    ? (if success)
CodeExecutor (runs, reports)
    ?
User sees output
```

If errors occur at any stage, the agent provides feedback and the generator produces a fix.

## Tools Structure

All tools are **decorated static methods** using MAF conventions:

```csharp
[Description("Tool description for the LLM")]
public static ReturnType ToolName(
    [Description("Parameter description")] ParameterType paramName)
{
    // Implementation
}
```

### Tool Categories

- **`Tools/CodeGenerationTools.cs`** - Code scaffolding helpers
- **`Tools/CompilationTools.cs`** - dotnet build integration
- **`Tools/ExecutionTools.cs`** - dotnet run integration

## Project Structure

```
LocalOllamaAgent/
??? Program.cs                      # Orchestration only
??? AgentFactory.cs                 # Creates specialized agents
??? CodeWorkflowManager.cs          # Workflow termination logic
??? OllamaRuntime.cs                # Ollama container management
??? CodeExtractor.cs                # Markdown fence extraction
??? Tools/
    ??? CodeGenerationTools.cs      # Generation phase tools
    ??? CompilationTools.cs         # Compilation phase tools
    ??? ExecutionTools.cs           # Execution phase tools
```

## Why This Architecture?

### ? Proper Separation of Concerns
- Each agent has **one job**
- Tools are **reusable** and **testable**
- Clear **boundaries** between phases

### ? Follows MAF Best Practices
- Uses `AIFunctionFactory.Create` for tools
- Agents created via factory pattern
- Workflow manager controls orchestration
- Clean termination conditions

### ? Scalable and Maintainable
- Easy to add new agents (e.g., TestRunner, Optimizer)
- Easy to add new tools
- Clear debugging (know which agent did what)
- No monolithic code

## Running

```bash
cd LocalOllamaAgent
dotnet run
```

Enter your specification when prompted, e.g.:
- "print Fibonacci up to 100"
- "calculate factorial of numbers 1-10"
- "FizzBuzz from 1 to 30"

## Prerequisites

- .NET 10 SDK
- Docker (for Ollama)
- Ollama model: llama3.1:8b

## Key Differences from Previous Approach

| Before | After |
|--------|-------|
| Single agent with one tool | Three specialized agents |
| Tool did compile + run | Separate compile and execute tools |
| Manual loop in Program.cs | Workflow orchestration |
| Code in Program.cs | Tools in dedicated classes |
| Unclear agent boundaries | Clear responsibilities |

## Example Workflow Execution

```
User: "print the first 10 squares"

CodeGenerator: 
  ? Calls GenerateCode tool
  ? Outputs code in ```csharp fence

CodeCompiler:
  ? Calls CompileCode tool
  ? Reports: COMPILED_SUCCESS
  ? Passes DLL path

CodeExecutor:
  ? Calls ExecuteCode tool
  ? Reports: SUCCESS - Execution completed
  ? Shows output: 1 4 9 16 25 36 49 64 81 100

Workflow terminates ?
```

## Future Extensions

Easily add more agents:
- **TestRunner Agent** - Generate and run unit tests
- **Optimizer Agent** - Analyze and optimize code
- **SecurityScanner Agent** - Check for vulnerabilities
- **DocumentationGenerator Agent** - Generate XML docs

Each would have its own tools and clear responsibility!
