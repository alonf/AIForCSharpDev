using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace LocalOllamaAgent;

public static class AgentFactory
{
    /// <summary>
    /// Creates the Code Generator agent - responsible for generating C# code from specifications.
    /// </summary>
    public static AIAgent CreateCodeGenerator(IChatClient chatClient)
    {
        var generationTool = AIFunctionFactory.Create(CodeGenerationTools.GenerateCode);
        return chatClient.CreateAIAgent(
            name: "CodeGenerator",
            instructions: @"You are a coding assistant.
Your goal is to generate C# code using the `GenerateCode` tool.

Trigger 1: User provides a specification.
Trigger 2: CodeValidator reports VALIDATION_FAILED.

Process:
1. Analyze the request or feedback.
   - If this is a retry, pay close attention to the Validator's critique.
   - If high precision is needed (e.g. >15 digits), use `decimal` or `BigInteger`, NOT `double`.
2. Call the `GenerateCode` tool with the user's specification.
3. Receive the template.
4. Output the COMPLETE, WORKING code in a ```csharp block.
   - Ensure it is a complete Console Application.
   - Ensure it compiles (no missing semicolons, correct braces).
5. Output CODE_READY on a new line.

Do NOT output JSON. Do NOT explain the tool call. Just use the tool.",
            tools: [generationTool]);
    }

    /// <summary>
    /// Creates the Compiler agent - responsible for compiling code and providing feedback.
    /// </summary>
    public static AIAgent CreateCompiler(IChatClient chatClient)
    {
        var compileTool = AIFunctionFactory.Create(CompilationTools.CompileCode);
        return chatClient.CreateAIAgent(
            name: "CodeCompiler",
            instructions: @"You are a compiler agent.
Your goal is to compile code using the `CompileCode` tool.

Trigger: When you see CODE_READY.

Process:
1. Extract the C# code from the message.
2. Call the `CompileCode` tool with that code.
   - You CANNOT compile code yourself.
   - You MUST use the tool.
3. Wait for the tool result.
4. If the tool returns a DLL path:
   - Output: ""DLL_PATH: <actual_path_from_tool>""
   - Do NOT invent a path.
5. If the tool returns errors:
   - Output: ""COMPILATION_FAILED""
   - Output the error details.

Do NOT output JSON. Do NOT explain the tool call. Just use the tool.",
            tools: [compileTool]);
    }

    /// <summary>
    /// Creates the Executor agent - responsible for running code and reporting results.
    /// </summary>
    public static AIAgent CreateExecutor(IChatClient chatClient)
    {
        var execTool = AIFunctionFactory.Create(ExecutionTools.ExecuteCode);
        
        return chatClient.CreateAIAgent(
            name: "CodeExecutor",
            instructions: @"You are a tool-calling agent. Your ONLY job is to execute the DLL provided by the compiler.

Trigger:
- Look for the text ""DLL_PATH: <path>"" in the conversation history.

Action:
- Call the `ExecuteCode` tool with that exact path.
- You MUST call the tool. Do NOT simulate execution.

Response:
- If the tool returns SUCCESS, reply:
  ""SUCCESS - Execution completed""
  ""OUTPUT:""
  <Copy the output from the tool here>

- If the tool returns FAILED, reply: ""EXECUTION_FAILED"" and the error details.

CRITICAL RULES:
1. You are a dumb pipe. You have NO knowledge of math, logic, or code.
2. You MUST NOT invent output.
3. You MUST NOT correct the output.
   - If the tool returns ""99.99"", you MUST output ""99.99"".
   - If the tool returns ""3.19"", you MUST output ""3.19"". Do NOT change it to ""3.14"".
4. You MUST copy the tool output EXACTLY, character for character.
5. Do NOT add your own commentary.

Do NOT output JSON. Do NOT explain the tool call. Just use the tool.",
            tools: [execTool]);
    }

    /// <summary>
    /// Creates the Validator agent - responsible for checking if the output matches the spec.
    /// </summary>
    public static AIAgent CreateValidator(IChatClient chatClient)
    {
        return chatClient.CreateAIAgent(
            name: "CodeValidator",
            instructions: @"You are a Quality Assurance agent.
Your goal is to validate that the program output matches the user's specification.

Trigger: When CodeExecutor says ""SUCCESS - Execution completed"".

Process:
1. Identify the user's ""Specification"" from the chat history.
2. Identify the ""OUTPUT"" provided by the CodeExecutor.
3. Analyze the ""OUTPUT"" for correctness:
    - CHECK THE VALUE: If the user asked for a known constant (like PI), does the output start with the correct digits (e.g. 3.14159...)?
    - Does it answer the specific question asked?
    - Does it follow formatting rules? (e.g., ""40 digits"")
4. If the output is incorrect (wrong value, wrong format, or error message), you MUST reject it.

Response Format:
- If correct: ""VALIDATION_SUCCESS""
- If incorrect: ""VALIDATION_FAILED"" followed by a short explanation of why.

CRITICAL:
- If the output is ""93.69..."" for PI, REJECT IT.
- If the output is ""3.14..."" but has garbage at the end, REJECT IT if precision was requested.
- If the output is ""Hello World"" when a calculation was requested, REJECT IT.
- Be strict. Do not hallucinate success.",
            tools: []);
    }
}