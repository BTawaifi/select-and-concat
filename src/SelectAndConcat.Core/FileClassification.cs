namespace SelectAndConcat.Core;

internal static class FileClassification
{
    private static readonly HashSet<string> KnownTextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".json", ".yaml", ".yml", ".xml", ".csv", ".tsv",
        ".ini", ".cfg", ".config", ".toml", ".log", ".ps1", ".bat", ".cmd", ".sh",
        ".c", ".h", ".cpp", ".hpp", ".cs", ".vb", ".fs", ".java", ".kt", ".kts", ".go",
        ".rs", ".swift", ".m", ".mm", ".php", ".py", ".rb", ".pl", ".lua", ".js", ".jsx",
        ".ts", ".tsx", ".html", ".htm", ".css", ".scss", ".less", ".sql", ".r", ".scala",
        ".dart", ".sln", ".csproj", ".props", ".targets", ".editorconfig", ".gitignore", ".gitattributes",
        ".dockerfile"
    };

    private static readonly HashSet<string> KnownBinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".sys", ".msi", ".zip", ".7z", ".rar", ".tar", ".gz", ".pdf",
        ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".png", ".jpg", ".jpeg", ".gif",
        ".webp", ".bmp", ".ico", ".mp3", ".wav", ".flac", ".ogg", ".mp4", ".mkv", ".avi",
        ".mov", ".class", ".jar", ".so", ".dylib", ".bin", ".dat", ".db", ".sqlite", ".woff",
        ".woff2", ".ttf", ".otf", ".eot"
    };

    private static readonly HashSet<string> SensitiveExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pem", ".key", ".pfx", ".p12", ".asc", ".kdbx", ".pkcs12"
    };

    private static readonly HashSet<string> SensitiveFilenames = new(StringComparer.OrdinalIgnoreCase)
    {
        "id_rsa", "id_dsa", "id_ecdsa", "id_ed25519",
        "credentials.json", "service-account.json", "client_secret.json"
    };

    public static bool IsObviousBinaryExtension(string extension) => KnownBinaryExtensions.Contains(extension);

    public static bool IsKnownTextExtension(string extension) => KnownTextExtensions.Contains(extension);

    public static bool IsSensitiveFile(string path)
    {
        var fileName = Path.GetFileName(path);
        if (string.IsNullOrEmpty(fileName))
        {
            return false;
        }

        if (fileName.Equals(".env", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith(".env.", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (SensitiveFilenames.Contains(fileName))
        {
            return true;
        }

        var extension = Path.GetExtension(path);
        return SensitiveExtensions.Contains(extension);
    }
}

