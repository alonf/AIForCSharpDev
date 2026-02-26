using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace LocalOllamaAgent.Tools;

/// <summary>
/// Tools for code compilation phase.
/// </summary>
public static class CompilationTools
{
    private const string DefaultCompileSdkVersion = "10.0.103";
    private const string DefaultTargetFramework = "net10.0";
    private const string DefaultSdk = "Microsoft.NET.Sdk";
    private const string RunMetadataFileName = "run-metadata.json";
    private const string AppModelConsole = "CONSOLE";
    private const string AppModelGui = "GUI";
    private static readonly Lock CompileCacheLock = new();
    private static string? _lastCompileSignature;
    private static string? _lastCompileResult;
    private static DateTime _lastCompileUtc;
    private static readonly TimeSpan CompileDedupWindow = TimeSpan.FromSeconds(15);

    [Description("Compile C# input. Accepts either raw C# source or JSON with `code`, optional `project` settings, and optional dependency lists (`packageReferences`, `frameworkReferences`, `references`).")]
    public static string CompileCode(
        [Description("Raw C# source, or JSON with code + project metadata/dependencies.")] string compileInput)
    {
        ToolCallTracker.RegisterCompileCall();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"[TOOL] CompileCode invoked. Source length: {compileInput.Length} chars");
        Console.ResetColor();

        string tempDir = Path.Combine(Path.GetTempPath(), "Compile_" + Guid.NewGuid().ToString("N"));
        string artifactsRoot = Path.Combine(Environment.CurrentDirectory, "GeneratedArtifacts");
        Directory.CreateDirectory(tempDir);
        Directory.CreateDirectory(artifactsRoot);

        try
        {
            CompileRequest request = ParseCompileRequest(compileInput);
            request.Project.TargetFramework = NormalizeTargetFramework(
                request.Project.TargetFramework,
                request.Project.UseWindowsForms,
                request.Project.UseWpf);

            if (string.IsNullOrWhiteSpace(request.Code))
            {
                return "COMPILATION_FAILED\nPRIMARY_ERROR: No C# source code found in compile input.\nERRORS:\nNo `code` field or C# fence could be extracted.";
            }

            string signature = BuildCompileSignature(request);
            if (TryGetDeduplicatedResult(signature, out string dedupResult))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[TOOL] CompileCode deduplicated identical request; reusing last result.");
                Console.ResetColor();
                return dedupResult;
            }

            File.WriteAllText(Path.Combine(tempDir, "Program.cs"), request.Code);
            File.WriteAllText(Path.Combine(tempDir, "App.csproj"), BuildProjectFile(request));
            File.WriteAllText(Path.Combine(tempDir, "global.json"), BuildCompileGlobalJson());

            var (exitCode, stdout, stderr) = RunProcess("dotnet", "build --nologo -c Release", tempDir);
            bool success = exitCode == 0;
            string releaseDir = Path.Combine(tempDir, "bin", "Release");

            if (success)
            {
                string? builtDll = FindBuiltDllPath(releaseDir);
                if (string.IsNullOrWhiteSpace(builtDll))
                {
                    return "COMPILATION_FAILED\nERRORS:\nBuild succeeded but App.dll was not found in output.";
                }

                string buildOutput = Path.GetDirectoryName(builtDll)!;
                string runDirectory = Path.Combine(artifactsRoot, $"Run_{DateTime.UtcNow:yyyyMMdd_HHmmssfff}");
                CopyDirectory(buildOutput, runDirectory);
                string appModel = ResolveAppModel(request.Project);
                WriteRunMetadata(runDirectory, request.Project, appModel);
                string dllPath = Path.Combine(runDirectory, "App.dll");

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[TOOL] CompileCode SUCCESS. DLL_PATH: {dllPath}");
                Console.ResetColor();
                string result = $"COMPILED_SUCCESS\nDLL_PATH: {dllPath}\nARTIFACT_DIR: {runDirectory}\nAPP_MODEL: {appModel}";
                UpdateCompileCache(signature, result);
                return result;
            }

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("[TOOL] CompileCode FAILED.");
            if (!string.IsNullOrWhiteSpace(stderr)) Console.WriteLine("[TOOL] ERRORS:\n" + stderr);
            if (!string.IsNullOrWhiteSpace(stdout)) Console.WriteLine("[TOOL] BUILD_OUTPUT:\n" + stdout);
            Console.ResetColor();

            var sb = new StringBuilder();
            sb.AppendLine("COMPILATION_FAILED");
            string? primaryError = ExtractPrimaryCompilerError(stderr, stdout);
            if (!string.IsNullOrWhiteSpace(primaryError))
            {
                sb.AppendLine("PRIMARY_ERROR: " + primaryError);
            }
            if (!string.IsNullOrWhiteSpace(stderr))
            {
                sb.AppendLine("ERRORS:");
                sb.AppendLine(stderr);
            }
            if (!string.IsNullOrWhiteSpace(stdout))
            {
                sb.AppendLine("BUILD_OUTPUT:");
                sb.AppendLine(stdout);
            }

            string failureResult = sb.ToString();
            UpdateCompileCache(signature, failureResult);
            return failureResult;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[TOOL] CompileCode EXCEPTION: {ex.Message}");
            Console.ResetColor();
            return $"COMPILATION_FAILED\nPRIMARY_ERROR: Exception during compilation: {ex.Message}\nERRORS:\nException during compilation: {ex.Message}";
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
            catch
            {
                // ignored
            }
        }
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var fileName = Path.GetFileName(file);
            File.Copy(file, Path.Combine(destinationDir, fileName), overwrite: true);
        }

        foreach (var directory in Directory.GetDirectories(sourceDir))
        {
            var dirName = Path.GetFileName(directory);
            CopyDirectory(directory, Path.Combine(destinationDir, dirName));
        }
    }

    private static string NormalizeSource(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        string code = raw.Trim();
        code = StripCodeFence(code);
        code = code.Replace("\r\n", "\n");

        if (!code.Contains('\n') && code.Contains("\\n", StringComparison.Ordinal))
        {
            code = UnescapeStructuralNewlines(code);
        }

        return code;
    }

    private static CompileRequest ParseCompileRequest(string input)
    {
        string trimmed = input.Trim();
        if (TryParseCompileRequestJson(trimmed, out var requestFromRaw))
        {
            EnsureCodeFallback(requestFromRaw, trimmed);
            return requestFromRaw;
        }

        string? jsonBlock = ExtractJsonFence(trimmed);
        if (!string.IsNullOrWhiteSpace(jsonBlock) && TryParseCompileRequestJson(jsonBlock, out var requestFromFence))
        {
            EnsureCodeFallback(requestFromFence, trimmed);
            return requestFromFence;
        }

        string extracted = CodeExtractor.Extract(trimmed);
        string code = string.IsNullOrWhiteSpace(extracted)
            ? NormalizeSource(trimmed)
            : NormalizeSource(extracted);

        return new CompileRequest { Code = code };
    }

    private static bool TryParseCompileRequestJson(string text, out CompileRequest request)
    {
        request = new CompileRequest();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            JsonElement root = doc.RootElement;
            request.Code = NormalizeSource(ReadString(root, "code")
                                           ?? ReadString(root, "source")
                                           ?? ReadString(root, "csharp")
                                           ?? string.Empty);

            if (TryGetProperty(root, "project", out JsonElement projectElement) &&
                projectElement.ValueKind == JsonValueKind.Object)
            {
                ApplyProjectProperties(projectElement, request.Project);
                ReadAdditionalProperties(projectElement, request.Project.AdditionalProperties);
            }

            ApplyProjectProperties(root, request.Project);
            ReadAdditionalProperties(root, request.Project.AdditionalProperties);
            ReadPackageReferences(root, request.PackageReferences);
            ReadFrameworkReferences(root, request.FrameworkReferences);
            ReadAssemblyReferences(root, request.References);

            if (TryGetProperty(root, "arguments", out JsonElement argumentsElement) &&
                argumentsElement.ValueKind == JsonValueKind.Object)
            {
                if (string.IsNullOrWhiteSpace(request.Code))
                {
                    request.Code = NormalizeSource(ReadString(argumentsElement, "code")
                                                   ?? ReadString(argumentsElement, "source")
                                                   ?? ReadString(argumentsElement, "compileInput")
                                                   ?? string.Empty);
                }

                if (TryGetProperty(argumentsElement, "project", out JsonElement argsProject) &&
                    argsProject.ValueKind == JsonValueKind.Object)
                {
                    ApplyProjectProperties(argsProject, request.Project);
                    ReadAdditionalProperties(argsProject, request.Project.AdditionalProperties);
                }

                ApplyProjectProperties(argumentsElement, request.Project);
                ReadAdditionalProperties(argumentsElement, request.Project.AdditionalProperties);
                ReadPackageReferences(argumentsElement, request.PackageReferences);
                ReadFrameworkReferences(argumentsElement, request.FrameworkReferences);
                ReadAssemblyReferences(argumentsElement, request.References);
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void EnsureCodeFallback(CompileRequest request, string fullMessage)
    {
        if (!string.IsNullOrWhiteSpace(request.Code))
        {
            return;
        }

        string extracted = CodeExtractor.Extract(fullMessage);
        request.Code = NormalizeSource(extracted);
    }

    private static void ApplyProjectProperties(JsonElement source, ProjectSettings project)
    {
        project.Sdk = ReadString(source, "sdk") ?? project.Sdk;
        project.OutputType = ReadString(source, "outputType") ?? project.OutputType;
        project.TargetFramework = ReadString(source, "targetFramework") ?? project.TargetFramework;
        project.Nullable = ReadString(source, "nullable") ?? project.Nullable;
        project.ImplicitUsings = ReadString(source, "implicitUsings") ?? project.ImplicitUsings;
        project.LangVersion = ReadString(source, "langVersion") ?? project.LangVersion;
        project.UseWindowsForms = ReadBoolean(source, "useWindowsForms", project.UseWindowsForms);
        project.UseWpf = ReadBoolean(source, "useWpf", project.UseWpf);
        project.AllowUnsafeBlocks = ReadBoolean(source, "allowUnsafeBlocks", project.AllowUnsafeBlocks);
        project.EnablePreviewFeatures = ReadBoolean(source, "enablePreviewFeatures", project.EnablePreviewFeatures);

        if (TryGetProperty(source, "treatWarningsAsErrors", out JsonElement treatWarnings))
        {
            bool parsed = ParseBooleanValue(treatWarnings, fallback: false);
            project.TreatWarningsAsErrors = parsed;
        }
    }

    private static void ReadAdditionalProperties(JsonElement source, IDictionary<string, string> destination)
    {
        if (!TryGetProperty(source, "properties", out JsonElement propertiesElement) ||
            propertiesElement.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (JsonProperty property in propertiesElement.EnumerateObject())
        {
            string value = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Number => property.Value.ToString(),
                _ => string.Empty
            };

            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            destination[property.Name] = value;
        }
    }

    private static void ReadPackageReferences(JsonElement source, ICollection<PackageReferenceItem> destination)
    {
        if (!TryGetProperty(source, "packageReferences", out JsonElement packageArray) &&
            !TryGetProperty(source, "packages", out packageArray))
        {
            return;
        }

        if (packageArray.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement package in packageArray.EnumerateArray())
        {
            string? id = null;
            string? version = null;

            if (package.ValueKind == JsonValueKind.Object)
            {
                id = ReadString(package, "id")
                     ?? ReadString(package, "include")
                     ?? ReadString(package, "name");
                version = ReadString(package, "version");
            }
            else if (package.ValueKind == JsonValueKind.String)
            {
                string raw = package.GetString() ?? string.Empty;
                (id, version) = ParseInlinePackage(raw);
            }

            if (string.IsNullOrWhiteSpace(id) || !seen.Add(id))
            {
                continue;
            }

            destination.Add(new PackageReferenceItem(id, version));
        }
    }

    private static void ReadFrameworkReferences(JsonElement source, ICollection<string> destination)
    {
        if (!TryGetProperty(source, "frameworkReferences", out JsonElement frameworkArray) ||
            frameworkArray.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement framework in frameworkArray.EnumerateArray())
        {
            if (framework.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            string? value = framework.GetString();
            if (string.IsNullOrWhiteSpace(value) || !seen.Add(value))
            {
                continue;
            }

            destination.Add(value);
        }
    }

    private static void ReadAssemblyReferences(JsonElement source, ICollection<string> destination)
    {
        if (!TryGetProperty(source, "references", out JsonElement referenceArray) &&
            !TryGetProperty(source, "dllReferences", out referenceArray))
        {
            return;
        }

        if (referenceArray.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement reference in referenceArray.EnumerateArray())
        {
            if (reference.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            string? path = reference.GetString();
            if (string.IsNullOrWhiteSpace(path) || !seen.Add(path))
            {
                continue;
            }

            destination.Add(path);
        }
    }

    private static string BuildProjectFile(CompileRequest request)
    {
        string sdk = string.IsNullOrWhiteSpace(request.Project.Sdk) ? DefaultSdk : request.Project.Sdk.Trim();
        string outputType = string.IsNullOrWhiteSpace(request.Project.OutputType) ? "Exe" : request.Project.OutputType.Trim();
        string targetFramework = string.IsNullOrWhiteSpace(request.Project.TargetFramework)
            ? DefaultTargetFramework
            : request.Project.TargetFramework.Trim();
        string nullable = string.IsNullOrWhiteSpace(request.Project.Nullable) ? "enable" : request.Project.Nullable.Trim();
        string implicitUsings = string.IsNullOrWhiteSpace(request.Project.ImplicitUsings) ? "enable" : request.Project.ImplicitUsings.Trim();

        var sb = new StringBuilder();
        sb.AppendLine($"<Project Sdk=\"{EscapeXml(sdk)}\">");
        sb.AppendLine("  <PropertyGroup>");
        sb.AppendLine($"    <OutputType>{EscapeXml(outputType)}</OutputType>");
        sb.AppendLine($"    <TargetFramework>{EscapeXml(targetFramework)}</TargetFramework>");
        sb.AppendLine($"    <Nullable>{EscapeXml(nullable)}</Nullable>");
        sb.AppendLine($"    <ImplicitUsings>{EscapeXml(implicitUsings)}</ImplicitUsings>");
        if (!string.IsNullOrWhiteSpace(request.Project.LangVersion))
        {
            sb.AppendLine($"    <LangVersion>{EscapeXml(request.Project.LangVersion.Trim())}</LangVersion>");
        }

        if (request.Project.UseWindowsForms)
        {
            sb.AppendLine("    <UseWindowsForms>true</UseWindowsForms>");
        }

        if (request.Project.UseWpf)
        {
            sb.AppendLine("    <UseWPF>true</UseWPF>");
        }

        if (request.Project.AllowUnsafeBlocks)
        {
            sb.AppendLine("    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>");
        }

        if (request.Project.EnablePreviewFeatures)
        {
            sb.AppendLine("    <EnablePreviewFeatures>true</EnablePreviewFeatures>");
        }

        if (request.Project.TreatWarningsAsErrors is { } treatWarningsAsErrors)
        {
            sb.AppendLine($"    <TreatWarningsAsErrors>{(treatWarningsAsErrors ? "true" : "false")}</TreatWarningsAsErrors>");
        }

        foreach (var entry in request.Project.AdditionalProperties.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!IsValidMsBuildPropertyName(entry.Key))
            {
                continue;
            }

            sb.AppendLine($"    <{entry.Key}>{EscapeXml(entry.Value)}</{entry.Key}>");
        }

        sb.AppendLine("  </PropertyGroup>");

        if (request.PackageReferences.Count > 0)
        {
            sb.AppendLine("  <ItemGroup>");
            foreach (PackageReferenceItem package in request.PackageReferences)
            {
                string include = EscapeXml(package.Id);
                if (string.IsNullOrWhiteSpace(package.Version))
                {
                    sb.AppendLine($"    <PackageReference Include=\"{include}\" />");
                }
                else
                {
                    sb.AppendLine($"    <PackageReference Include=\"{include}\" Version=\"{EscapeXml(package.Version)}\" />");
                }
            }

            sb.AppendLine("  </ItemGroup>");
        }

        if (request.FrameworkReferences.Count > 0)
        {
            sb.AppendLine("  <ItemGroup>");
            foreach (string frameworkReference in request.FrameworkReferences)
            {
                sb.AppendLine($"    <FrameworkReference Include=\"{EscapeXml(frameworkReference)}\" />");
            }

            sb.AppendLine("  </ItemGroup>");
        }

        if (request.References.Count > 0)
        {
            sb.AppendLine("  <ItemGroup>");
            foreach (string referencePath in request.References)
            {
                string includeName = Path.GetFileNameWithoutExtension(referencePath);
                if (string.IsNullOrWhiteSpace(includeName))
                {
                    includeName = "ExternalReference";
                }

                sb.AppendLine($"    <Reference Include=\"{EscapeXml(includeName)}\">");
                sb.AppendLine($"      <HintPath>{EscapeXml(referencePath)}</HintPath>");
                sb.AppendLine("    </Reference>");
            }

            sb.AppendLine("  </ItemGroup>");
        }

        sb.AppendLine("</Project>");
        return sb.ToString();
    }

    private static string BuildCompileSignature(CompileRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine(request.Code);
        sb.AppendLine("---PROJECT---");
        sb.AppendLine(request.Project.Sdk);
        sb.AppendLine(request.Project.OutputType);
        sb.AppendLine(request.Project.TargetFramework);
        sb.AppendLine(request.Project.Nullable);
        sb.AppendLine(request.Project.ImplicitUsings);
        sb.AppendLine(request.Project.LangVersion ?? string.Empty);
        sb.AppendLine(request.Project.UseWindowsForms.ToString());
        sb.AppendLine(request.Project.UseWpf.ToString());
        sb.AppendLine(request.Project.AllowUnsafeBlocks.ToString());
        sb.AppendLine(request.Project.EnablePreviewFeatures.ToString());
        sb.AppendLine(request.Project.TreatWarningsAsErrors?.ToString() ?? string.Empty);

        foreach (var prop in request.Project.AdditionalProperties.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append(prop.Key).Append('=').AppendLine(prop.Value);
        }

        sb.AppendLine("---PACKAGES---");
        foreach (var pkg in request.PackageReferences.OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append(pkg.Id).Append('@').AppendLine(pkg.Version ?? string.Empty);
        }

        sb.AppendLine("---FRAMEWORK-REFS---");
        foreach (string frameworkRef in request.FrameworkReferences.OrderBy(v => v, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine(frameworkRef);
        }

        sb.AppendLine("---ASSEMBLY-REFS---");
        foreach (string reference in request.References.OrderBy(v => v, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine(reference);
        }

        return sb.ToString();
    }

    private static bool TryGetDeduplicatedResult(string signature, out string result)
    {
        lock (CompileCacheLock)
        {
            bool signatureMatch = string.Equals(_lastCompileSignature, signature, StringComparison.Ordinal);
            bool withinWindow = DateTime.UtcNow - _lastCompileUtc <= CompileDedupWindow;
            if (signatureMatch && withinWindow && !string.IsNullOrWhiteSpace(_lastCompileResult))
            {
                result = _lastCompileResult;
                return true;
            }
        }

        result = string.Empty;
        return false;
    }

    private static void UpdateCompileCache(string signature, string result)
    {
        lock (CompileCacheLock)
        {
            _lastCompileSignature = signature;
            _lastCompileResult = result;
            _lastCompileUtc = DateTime.UtcNow;
        }
    }

    private static string ResolveAppModel(ProjectSettings project)
    {
        bool winForms = project.UseWindowsForms;
        bool wpf = project.UseWpf;
        bool winExe = project.OutputType.Equals("WinExe", StringComparison.OrdinalIgnoreCase);
        return (winForms || wpf || winExe) ? AppModelGui : AppModelConsole;
    }

    private static void WriteRunMetadata(string runDirectory, ProjectSettings project, string appModel)
    {
        try
        {
            var metadata = new RunMetadata
            {
                AppModel = appModel,
                TargetFramework = project.TargetFramework,
                OutputType = project.OutputType,
                UseWindowsForms = project.UseWindowsForms,
                UseWpf = project.UseWpf
            };

            string metadataPath = Path.Combine(runDirectory, RunMetadataFileName);
            string json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(metadataPath, json);
        }
        catch
        {
            // Metadata improves execution strategy but should not fail compilation success.
        }
    }

    private static string NormalizeTargetFramework(string? targetFramework, bool useWindowsForms, bool useWpf)
    {
        string tf = string.IsNullOrWhiteSpace(targetFramework)
            ? DefaultTargetFramework
            : targetFramework.Trim();
        bool needsWindowsTf = useWindowsForms || useWpf;
        if (!needsWindowsTf || tf.Contains("-windows", StringComparison.OrdinalIgnoreCase))
        {
            return tf;
        }

        return tf + "-windows";
    }

    private static string? FindBuiltDllPath(string releaseDir)
    {
        if (!Directory.Exists(releaseDir))
        {
            return null;
        }

        return Directory.GetFiles(releaseDir, "App.dll", SearchOption.AllDirectories)
            .OrderBy(path => path.Length)
            .FirstOrDefault();
    }

    private static string? ExtractJsonFence(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        int searchIndex = 0;
        while (true)
        {
            int startFence = text.IndexOf("```", searchIndex, StringComparison.Ordinal);
            if (startFence < 0)
            {
                return null;
            }

            int endFence = text.IndexOf("```", startFence + 3, StringComparison.Ordinal);
            if (endFence < 0)
            {
                return null;
            }

            string block = text[(startFence + 3)..endFence];
            string normalized = block.Replace("\r\n", "\n").Replace('\r', '\n');
            string[] lines = normalized.Split('\n');
            if (lines.Length > 0 && lines[0].Trim().Equals("json", StringComparison.OrdinalIgnoreCase))
            {
                return string.Join('\n', lines.Skip(1)).Trim();
            }

            searchIndex = endFence + 3;
        }
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static bool ReadBoolean(JsonElement element, string propertyName, bool fallback)
    {
        if (!TryGetProperty(element, propertyName, out JsonElement value))
        {
            return fallback;
        }

        return ParseBooleanValue(value, fallback);
    }

    private static bool ParseBooleanValue(JsonElement value, bool fallback)
    {
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out bool parsed) => parsed,
            _ => fallback
        };
    }

    private static (string? Id, string? Version) ParseInlinePackage(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return (null, null);
        }

        string trimmed = raw.Trim();
        int atIndex = trimmed.LastIndexOf('@');
        if (atIndex <= 0 || atIndex == trimmed.Length - 1)
        {
            return (trimmed, null);
        }

        string id = trimmed[..atIndex].Trim();
        string version = trimmed[(atIndex + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(id))
        {
            return (null, null);
        }

        return (id, string.IsNullOrWhiteSpace(version) ? null : version);
    }

    private static bool IsValidMsBuildPropertyName(string propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return false;
        }

        if (!(char.IsLetter(propertyName[0]) || propertyName[0] == '_'))
        {
            return false;
        }

        for (int i = 1; i < propertyName.Length; i++)
        {
            char c = propertyName[i];
            if (!(char.IsLetterOrDigit(c) || c == '_' || c == '.'))
            {
                return false;
            }
        }

        return true;
    }

    private static string EscapeXml(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&apos;", StringComparison.Ordinal);
    }

    private static string StripCodeFence(string code)
    {
        if (!code.StartsWith("```", StringComparison.Ordinal))
        {
            return code;
        }

        int firstNewLine = code.IndexOf('\n');
        if (firstNewLine >= 0)
        {
            code = code[(firstNewLine + 1)..];
        }

        int closingFence = code.LastIndexOf("```", StringComparison.Ordinal);
        if (closingFence >= 0)
        {
            code = code[..closingFence];
        }

        return code.Trim();
    }

    private static string UnescapeStructuralNewlines(string code)
    {
        var sb = new StringBuilder(code.Length);
        bool inString = false;
        bool inVerbatimString = false;
        bool inChar = false;
        bool escapeActive = false;

        for (int i = 0; i < code.Length; i++)
        {
            char c = code[i];

            if (inVerbatimString)
            {
                sb.Append(c);

                if (c == '"' && i + 1 < code.Length && code[i + 1] == '"')
                {
                    sb.Append(code[i + 1]);
                    i++;
                    continue;
                }

                if (c == '"')
                {
                    inVerbatimString = false;
                }

                continue;
            }

            if (inString)
            {
                sb.Append(c);

                if (escapeActive)
                {
                    escapeActive = false;
                    continue;
                }

                if (c == '\\')
                {
                    escapeActive = true;
                    continue;
                }

                if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (inChar)
            {
                sb.Append(c);

                if (escapeActive)
                {
                    escapeActive = false;
                    continue;
                }

                if (c == '\\')
                {
                    escapeActive = true;
                    continue;
                }

                if (c == '\'')
                {
                    inChar = false;
                }

                continue;
            }

            if (c == '@' && i + 1 < code.Length && code[i + 1] == '"')
            {
                sb.Append(c);
                inVerbatimString = true;
                continue;
            }

            if (c == '"')
            {
                sb.Append(c);
                inString = true;
                continue;
            }

            if (c == '\'')
            {
                sb.Append(c);
                inChar = true;
                continue;
            }

            if (c == '\\' && i + 1 < code.Length)
            {
                char next = code[i + 1];

                if (next == 'r' && i + 3 < code.Length && code[i + 2] == '\\' && code[i + 3] == 'n')
                {
                    sb.Append('\n');
                    i += 3;
                    continue;
                }

                if (next == 'n')
                {
                    sb.Append('\n');
                    i++;
                    continue;
                }

                if (next == 't')
                {
                    sb.Append('\t');
                    i++;
                    continue;
                }

                if (next == '\\')
                {
                    sb.Append('\\');
                    i++;
                    continue;
                }
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    private static string BuildCompileGlobalJson()
    {
        string? repositoryGlobalJson = TryLoadNearestGlobalJson();
        if (!string.IsNullOrWhiteSpace(repositoryGlobalJson))
        {
            return repositoryGlobalJson;
        }

        string version = ResolveCompileSdkVersion();
        return
            "{\n" +
            "  \"sdk\": {\n" +
            $"    \"version\": \"{version}\",\n" +
            "    \"rollForward\": \"latestPatch\",\n" +
            "    \"allowPrerelease\": false\n" +
            "  }\n" +
            "}";
    }

    private static string? TryLoadNearestGlobalJson()
    {
        foreach (string root in EnumerateSearchRoots())
        {
            string? current = Directory.Exists(root) ? root : Path.GetDirectoryName(root);
            while (!string.IsNullOrWhiteSpace(current))
            {
                string candidate = Path.Combine(current, "global.json");
                if (File.Exists(candidate))
                {
                    try
                    {
                        string json = File.ReadAllText(candidate);
                        if (IsValidGlobalJson(json))
                        {
                            return json;
                        }
                    }
                    catch
                    {
                        // ignore malformed or unreadable files and keep searching
                    }
                }

                current = Directory.GetParent(current)?.FullName;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateSearchRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roots = new[]
        {
            Environment.CurrentDirectory,
            AppContext.BaseDirectory
        };

        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            string normalized = Path.GetFullPath(root);
            if (seen.Add(normalized))
            {
                yield return normalized;
            }
        }
    }

    private static bool IsValidGlobalJson(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                   document.RootElement.TryGetProperty("sdk", out _);
        }
        catch
        {
            return false;
        }
    }

    private static string ResolveCompileSdkVersion()
    {
        string? envVersion = Environment.GetEnvironmentVariable("AGENT_DOTNET_SDK_VERSION");
        if (!string.IsNullOrWhiteSpace(envVersion))
        {
            return envVersion.Trim();
        }

        return DefaultCompileSdkVersion;
    }

    private static string? ExtractPrimaryCompilerError(string stderr, string stdout)
    {
        static string? FindFirstErrorLine(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            foreach (string raw in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string line = raw.Trim();
                if (line.Contains(": error ", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains(" error CS", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("error ", StringComparison.OrdinalIgnoreCase))
                {
                    return line;
                }
            }

            return null;
        }

        string? error = FindFirstErrorLine(stderr);
        if (!string.IsNullOrWhiteSpace(error))
        {
            return error;
        }

        error = FindFirstErrorLine(stdout);
        if (!string.IsNullOrWhiteSpace(error))
        {
            return error;
        }

        foreach (string raw in stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string line = raw.Trim();
            if (!string.IsNullOrWhiteSpace(line))
            {
                return line;
            }
        }

        foreach (string raw in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string line = raw.Trim();
            if (!string.IsNullOrWhiteSpace(line))
            {
                return line;
            }
        }

        return null;
    }

    private static (int ExitCode, string StdOut, string StdErr) RunProcess(
        string file, string args, string? workingDir = null)
    {
        var psi = new ProcessStartInfo(file, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDir ?? Environment.CurrentDirectory
        };
        
        using var p = Process.Start(psi)!;
        string stdout = p.StandardOutput.ReadToEnd();
        string stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        
        return (p.ExitCode, stdout.Trim(), stderr.Trim());
    }

    private sealed class CompileRequest
    {
        public string Code { get; set; } = string.Empty;
        public ProjectSettings Project { get; } = new();
        public List<PackageReferenceItem> PackageReferences { get; } = [];
        public List<string> FrameworkReferences { get; } = [];
        public List<string> References { get; } = [];
    }

    private sealed class ProjectSettings
    {
        public string Sdk { get; set; } = DefaultSdk;
        public string OutputType { get; set; } = "Exe";
        public string TargetFramework { get; set; } = DefaultTargetFramework;
        public string Nullable { get; set; } = "enable";
        public string ImplicitUsings { get; set; } = "enable";
        public string? LangVersion { get; set; }
        public bool UseWindowsForms { get; set; }
        public bool UseWpf { get; set; }
        public bool AllowUnsafeBlocks { get; set; }
        public bool EnablePreviewFeatures { get; set; }
        public bool? TreatWarningsAsErrors { get; set; }
        public Dictionary<string, string> AdditionalProperties { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class RunMetadata
    {
        public string AppModel { get; set; } = AppModelConsole;
        public string TargetFramework { get; set; } = DefaultTargetFramework;
        public string OutputType { get; set; } = "Exe";
        public bool UseWindowsForms { get; set; }
        public bool UseWpf { get; set; }
    }

    private readonly record struct PackageReferenceItem(string Id, string? Version);
}
