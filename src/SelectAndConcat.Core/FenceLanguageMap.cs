namespace SelectAndConcat.Core;

internal static class FenceLanguageMap
{
    private static readonly IReadOnlyDictionary<string, string> ExtensionToLanguage =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".cs"] = "csharp",
            [".ts"] = "ts",
            [".tsx"] = "tsx",
            [".js"] = "js",
            [".jsx"] = "jsx",
            [".json"] = "json",
            [".md"] = "md",
            [".markdown"] = "md",
            [".xml"] = "xml",
            [".html"] = "html",
            [".css"] = "css",
            [".scss"] = "scss",
            [".sql"] = "sql",
            [".ps1"] = "powershell",
            [".yml"] = "yaml",
            [".yaml"] = "yaml",
            [".py"] = "python",
            [".go"] = "go",
            [".rs"] = "rust",
            [".java"] = "java",
            [".kt"] = "kotlin",
            [".php"] = "php",
            [".rb"] = "ruby",
            [".sh"] = "bash",
            [".bat"] = "bat",
            [".cmd"] = "bat",
            [".toml"] = "toml",
            [".ini"] = "ini",
            [".txt"] = "text"
        };

    public static string FromExtension(string extension)
    {
        if (ExtensionToLanguage.TryGetValue(extension, out var language))
        {
            return language;
        }

        return "text";
    }
}
