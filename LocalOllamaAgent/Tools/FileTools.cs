using System.ComponentModel;

namespace LocalOllamaAgent;

public static class FileTools
{
    [Description("Check if a file exists at the given path.")]
    public static bool FileExists(
        [Description("Absolute file path to check.")] string path)
    {
        return File.Exists(path);
    }
}
