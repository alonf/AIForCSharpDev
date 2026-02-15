using LocalOllamaAgent.Tools;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace LocalOllamaAgent;

public static class AgentFactory
{
    /// <summary>
    /// Creates the Code Generator agent - responsible for generating C# code from specifications.
    /// </summary>
    public static ChatClientAgent CreateCodeGenerator(IChatClient chatClient, ILoggerFactory? loggerFactory = null)
    {
        var generationTool = AIFunctionFactory.Create(CodeGenerationTools.GenerateCode);
        return new ChatClientAgent(
            chatClient,
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
            tools: [generationTool],
            loggerFactory: loggerFactory);
    }

    /// <summary>
    /// Creates the Compiler agent
    /// </summary>
    public static ChatClientAgent CreateCompiler(IChatClient chatClient, ILoggerFactory? loggerFactory = null)
    {
        var compileTool = AIFunctionFactory.Create(CompilationTools.CompileCode);
        return new ChatClientAgent(
            chatClient,
            name: "CodeCompiler",
            instructions: @"You are a compiler agent.
Your goal is to compile code using the `CompileCode` tool.

Trigger: When you see CODE_READY.

Process:
1. Extract the pure C# code block from the latest message.
   - Remove ``` fences.
   - Convert any literal sequences like \n into real newline characters before calling the tool.
2. Call the `CompileCode` tool with that exact source string immediately.
   - You CANNOT compile code yourself.
   - You MUST use the tool once per CODE_READY event.
3. Wait for the tool result.
4. If the tool returns a DLL path:
    - Output only the lines returned by the tool (for example ""COMPILED_SUCCESS"", ""DLL_PATH: ..."", ""ARTIFACT_DIR: ..."").
5. If the tool returns errors:
    - Output ""COMPILATION_FAILED"".
   - Copy the error details verbatim from the tool.
   - Do NOT add explanations, tutorials, or advice.

Response Rules:
- Never invent file paths or tool output.
- Do NOT append commentary before or after the tool output.
- Your entire reply must match the tool output exactly (same lines, same order). No summaries, notes, or additional sentences are allowed.
- Do NOT output JSON or any structured wrapper. Do NOT explain the tool call. Just use the tool.",
            tools: [compileTool],
            loggerFactory: loggerFactory);
    }

    /// <summary>
    /// Creates the Executor agent
    /// </summary>
    public static ChatClientAgent CreateExecutor(IChatClient chatClient, ILoggerFactory? loggerFactory = null)
    {
        var execTool = AIFunctionFactory.Create(ExecutionTools.ExecuteCode);

        return new ChatClientAgent(
            chatClient,
            name: "CodeExecutor",
            instructions: @"You are a tool-calling agent. Your ONLY job is to execute the DLL provided by the compiler.

Trigger:
- Look for the text ""DLL_PATH: <path>"" in the conversation history.

Action:
- Call the `ExecuteCode` tool with that exact path.
- You MUST call the tool. Do NOT simulate execution.

Response:
- If the tool returns SUCCESS, reply exactly:
  ""SUCCESS - Execution completed""
  ""OUTPUT:""
  <Copy the output from the tool here>
  (Include the ""STDERR:"" section if the tool returned one.)

- If the tool returns FAILED, reply:
  ""EXECUTION_FAILED""
  <Copy the failure details verbatim>

CRITICAL RULES:
1. You are a dumb pipe. You have NO knowledge of math, logic, or code.
2. You MUST NOT invent output.
3. You MUST NOT correct the output.
   - If the tool returns ""99.99"", you MUST output ""99.99"".
   - If the tool returns ""3.19"", you MUST output ""3.19"". Do NOT change it to ""3.14"".
4. You MUST copy the tool output EXACTLY, character for character.
5. Do NOT add your own commentary.
6. Never mention DLL_PATH values or pretend the tool succeeded if it did not.
7. Do NOT wrap the response in JSON, bullet lists, or helper text. Output only the lines above.
- Your entire reply must be an exact echo of the tool output (plus the required SUCCESS/FAILED wrapper shown above). Do not append explanations or advice.

Do NOT output JSON. Do NOT explain the tool call. Just use the tool.",
            tools: [execTool],
            loggerFactory: loggerFactory);
    }

    /// <summary>
    /// Creates the Validator agent
    /// </summary>
    public static ChatClientAgent CreateValidator(IChatClient chatClient, string specification, ILoggerFactory? loggerFactory = null)
    {
        string normalizedSpec = string.IsNullOrWhiteSpace(specification) ? "No specification provided." : specification;
        return new ChatClientAgent(
            chatClient,
            name: "CodeValidator",
            instructions: $@"You are a Quality Assurance agent.
Original user specification:
{normalizedSpec}

Your goal is to validate that the program output matches the user's specification.

Trigger: When CodeExecutor says ""SUCCESS - Execution completed"".

Process:
0. Confirm required tools actually ran:
   - Locate the most recent message from CodeCompiler. It MUST contain the `CompileCode` tool result (""COMPILED_SUCCESS""/""COMPILATION_FAILED"" plus the tool's payload). If it is missing, fabricated, or padded with commentary, reply ""VALIDATION_FAILED"" and ask CodeCompiler to call `CompileCode` now.
   - Locate the most recent message from CodeExecutor. It MUST be a verbatim echo of the `ExecuteCode` tool response (""SUCCESS""/""FAILED"" block). If it is missing, fabricated, or contains commentary beyond the tool output, reply ""VALIDATION_FAILED"" and ask CodeExecutor to call `ExecuteCode` now.
   - Do NOT proceed to value checks until both tool outputs are confirmed present.
1. Re-read the specification and list the explicit requirements (values, formats, units, bounds, counts, success criteria).
2. Retrieve the most recent ""OUTPUT"" section from CodeExecutor.
3. Verify the OUTPUT against every requirement:
   - Check that required constraints are satisfied (format, sign, magnitude, digit count, textual phrases, etc.).
   - Reject outputs containing execution errors, placeholder text, missing data, or contradictions.
   - If you cannot confidently confirm correctness, reject and request another attempt.
4. When rejecting, explain the first concrete mismatch so the team can fix it.

Response Format:
- If correct: ""VALIDATION_SUCCESS"".
- If incorrect: ""VALIDATION_FAILED"" followed by a short explanation of why.

CRITICAL:
- Reject any CodeCompiler message that is not a pure tool response (extra sentences, summaries, JSON, etc.).
- Reject any CodeExecutor message that does not start with ""SUCCESS - Execution completed"" or ""EXECUTION_FAILED"" exactly, or that contains fabricated content.
- Reject any response that contradicts the specification or lacks verified evidence.
- Reject if the required tool outputs are missing, incomplete, or altered.
- Be strict. Do not assume success without proof. If uncertain, reject and request a retry.",
            tools: [],
            loggerFactory: loggerFactory);
    }
}