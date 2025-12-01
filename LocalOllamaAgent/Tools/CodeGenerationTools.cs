using System.ComponentModel;

namespace LocalOllamaAgent;

/// <summary>
/// Tools for code generation phase.
/// </summary>
public static class CodeGenerationTools
{
    [Description("Generate initial C# console application code structure from specification.")]
    public static string GenerateCode(
        [Description("The specification describing what the program should do.")] string specification)
    {
        // This is a template helper - the LLM will provide the actual implementation
        return $"// Generated code template for: {specification}\n" +
               $"// The LLM should replace this with actual implementation\n" +
               $"using System;\n\n" +
               $"class Program\n" +
               $"{{\n" +
               $"    static void Main()\n" +
               $"    {{\n" +
               $"        // TODO: Implement: {specification}\n" +
               $"    }}\n" +
               $"}}";
    }
}