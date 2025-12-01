using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace LocalOllamaAgent;

/// <summary>
/// Factory for creating specialized agents in the code generation workflow.
/// Each agent has a specific responsibility and access to relevant tools.
/// </summary>
public static class AgentFactory
{
    /// <summary>
    /// Creates the Code Generator agent - responsible for generating C# code from specifications.
    /// </summary>
    public static AIAgent CreateCodeGenerator(IChatClient chatClient)
    {
        return chatClient.CreateAIAgent(
            name: "CodeGenerator",
            instructions: @"You are a C# Code Generator agent.

Your ONLY job: Generate complete C# code from the specification.

Steps:
1. Read the specification
2. Write a complete, working C# console application
3. Output the code inside a markdown fence like this:

```csharp
using System;

class Program
{
    static void Main()
    {
        // Your implementation
    }
}
```

4. After the closing ```, say exactly: CODE_READY
5. STOP - do nothing else until you get feedback

IMPORTANT:
- Do NOT try to compile or execute the code yourself
- Do NOT call any tools
- Just output code and say CODE_READY
- The compiler will handle the rest");
    }

    /// <summary>
    /// Creates the Compiler agent - responsible for compiling code and providing feedback.
    /// </summary>
    public static AIAgent CreateCompiler(IChatClient chatClient)
    {
        var compileTools = AIFunctionFactory.Create(CompilationTools.CompileCode);
        
        return chatClient.CreateAIAgent(
            name: "CodeCompiler",
            instructions: @"You are a Code Compiler agent.

Your job:
1. Wait until you see CODE_READY from CodeGenerator
2. Find the most recent ```csharp code block in the conversation
3. Extract ONLY the code between ```csharp and the closing ```
4. Call CompileCode tool with ONLY the extracted C# code (no markdown, no extra text)

Example:
If you see:
```csharp
using System;
class Program { }
```
CODE_READY

Then call: CompileCode with parameter code = ""using System;\nclass Program { }"".

After calling CompileCode:
- If success=true: Say COMPILED_SUCCESS, then say DLL_PATH: {the outputPath from tool result}
- If success=false: Show the errors, ask CodeGenerator to fix them

DO NOT:
- Pass markdown fences to the compiler
- Pass JSON to the compiler  
- Call CompileCode before seeing CODE_READY
- Try to execute code yourself",
            tools: [compileTools]);
    }

    /// <summary>
    /// Creates the Executor agent - responsible for running code and reporting results.
    /// </summary>
    public static AIAgent CreateExecutor(IChatClient chatClient)
    {
        var executeTools = AIFunctionFactory.Create(ExecutionTools.ExecuteCode);
        
        return chatClient.CreateAIAgent(
            name: "CodeExecutor",
            instructions: @"You are a Code Executor agent.

Your job:
1. Wait until you see COMPILED_SUCCESS and DLL_PATH from CodeCompiler
2. Extract the DLL path from the message (look for DLL_PATH: ...)
3. Call ExecuteCode tool with ONLY the dll path

Example:
If you see: ""DLL_PATH: C:\Temp\Compile_xxx\bin\Release\net10.0\App.dll"",
Then call: ExecuteCode with parameter dllPath = ""C:\Temp\Compile_xxx\bin\Release\net10.0\App.dll"".

After calling ExecuteCode:
- If success=true: 
  * Show the output to the user
  * Say: SUCCESS - Execution completed
  * Workflow will end
- If success=false:
  * Show the error
  * Ask CodeGenerator to fix runtime issues

DO NOT:
- Call ExecuteCode before seeing COMPILED_SUCCESS
- Try to compile code yourself",
            tools: [executeTools]);
    }
}