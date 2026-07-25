using System.Text;

namespace SelectAndConcat.Core;

public sealed class SelectionProcessor
{
    private const string TruncatedSuffix = "\n\n[TRUNCATED: content shortened at 512 KiB limit.]";

    private static readonly HashSet<string> IgnoredDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", ".idea", "node_modules", "bin", "obj", "dist", "build", "__pycache__", ".mvn", "vendor"
    };

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(IntPtr hFile, out BY_HANDLE_FILE_INFORMATION lpFileInformation);

    private struct BY_HANDLE_FILE_INFORMATION
    {
        public uint dwFileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
        public uint dwVolumeSerialNumber;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        public uint nNumberOfLinks;
        public uint nFileIndexHigh;
        public uint nFileIndexLow;
    }

    public ProcessResult Process(IReadOnlyList<string> rawPaths, ProcessingOptions? options = null)
    {
        options ??= new ProcessingOptions();

        var skipped = new List<SkippedFile>();
        var normalized = NormalizeAndExpandPaths(rawPaths, skipped, options.MaxFiles);

        var candidates = new List<IncludedFile>();
        foreach (var path in normalized)
        {
            var loaded = TryLoadTextFile(path, options, out var includedFile, out var reason);
            if (!loaded || includedFile is null)
            {
                skipped.Add(new SkippedFile(path, reason ?? "Unknown read failure."));
                continue;
            }

            candidates.Add(includedFile);
        }

        var included = ApplyTotalPayloadLimit(candidates, skipped, options);
        var payload = BuildPayload(included, skipped);

        while (included.Count > 0 && Encoding.UTF8.GetByteCount(payload) > options.MaxPayloadBytes)
        {
            var removed = included[^1];
            included.RemoveAt(included.Count - 1);
            skipped.Add(new SkippedFile(removed.FullPath, "Skipped because formatted output exceeded the total payload limit."));
            payload = BuildPayload(included, skipped);
        }

        var exitCode = included.Count == 0
            ? ProcessExitCode.Failure
            : skipped.Count > 0
                ? ProcessExitCode.PartialSuccess
                : ProcessExitCode.Success;

        return new ProcessResult(exitCode, payload, included, skipped);
    }

    private static List<string> NormalizeAndExpandPaths(
        IReadOnlyList<string> rawPaths,
        ICollection<SkippedFile> skipped,
        int maxFiles)
    {
        var accepted = new List<string>();
        var seenSelections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenFileIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in rawPaths)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                skipped.Add(new SkippedFile("<empty>", "Path argument was empty."));
                continue;
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(raw.Trim());
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                skipped.Add(new SkippedFile(raw, $"Invalid path format: {ex.Message}"));
                continue;
            }

            if (!seenSelections.Add(fullPath))
            {
                skipped.Add(new SkippedFile(fullPath, "Duplicate selection ignored."));
                continue;
            }

            if (Directory.Exists(fullPath))
            {
                AcceptDirectoryFiles(fullPath, accepted, seenFiles, seenFileIds, skipped, maxFiles);
                continue;
            }

            AcceptFile(fullPath, accepted, seenFiles, seenFileIds, skipped, maxFiles);
        }

        return accepted;
    }

    private static void AcceptDirectoryFiles(
        string directoryPath,
        ICollection<string> accepted,
        ISet<string> seenFiles,
        ISet<string> seenFileIds,
        ICollection<SkippedFile> skipped,
        int maxFiles)
    {
        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(directoryPath);
        }
        catch (Exception ex)
        {
            skipped.Add(new SkippedFile(directoryPath, $"Could not read directory attributes: {ex.Message}"));
            return;
        }

        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            skipped.Add(new SkippedFile(directoryPath, "Reparse points/symlinks are not supported."));
            return;
        }

        var foundAny = false;
        foreach (var filePath in EnumerateDirectoryFiles(directoryPath, skipped, depth: 1))
        {
            foundAny = true;
            AcceptFile(filePath, accepted, seenFiles, seenFileIds, skipped, maxFiles);
        }

        if (!foundAny)
        {
            skipped.Add(new SkippedFile(directoryPath, "Directory did not contain any files."));
        }
    }

    private static IEnumerable<string> EnumerateDirectoryFiles(
        string directoryPath,
        ICollection<SkippedFile> skipped,
        int depth)
    {
        if (depth > 32)
        {
            skipped.Add(new SkippedFile(directoryPath, "Maximum directory depth reached (32 levels)."));
            yield break;
        }

        string[] files;
        try
        {
            files = Directory.EnumerateFiles(directoryPath)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            skipped.Add(new SkippedFile(directoryPath, $"Could not enumerate files: {ex.Message}"));
            yield break;
        }

        foreach (var file in files)
        {
            yield return file;
        }

        string[] directories;
        try
        {
            directories = Directory.EnumerateDirectories(directoryPath)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            skipped.Add(new SkippedFile(directoryPath, $"Could not enumerate subdirectories: {ex.Message}"));
            yield break;
        }

        foreach (var directory in directories)
        {
            var dirName = Path.GetFileName(directory);
            if (IgnoredDirectoryNames.Contains(dirName))
            {
                skipped.Add(new SkippedFile(directory, "Ignored build/dependency directory."));
                continue;
            }

            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(directory);
            }
            catch (Exception ex)
            {
                skipped.Add(new SkippedFile(directory, $"Could not read directory attributes: {ex.Message}"));
                continue;
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                skipped.Add(new SkippedFile(directory, "Reparse points/symlinks are not supported."));
                continue;
            }

            foreach (var file in EnumerateDirectoryFiles(directory, skipped, depth + 1))
            {
                yield return file;
            }
        }
    }

    private static void AcceptFile(
        string fullPath,
        ICollection<string> accepted,
        ISet<string> seenFiles,
        ISet<string> seenFileIds,
        ICollection<SkippedFile> skipped,
        int maxFiles)
    {
        if (!File.Exists(fullPath))
        {
            skipped.Add(new SkippedFile(fullPath, "File does not exist."));
            return;
        }

        if (FileClassification.IsSensitiveFile(fullPath))
        {
            skipped.Add(new SkippedFile(fullPath, "Excluded sensitive/secret file to protect credentials."));
            return;
        }

        if (!seenFiles.Add(fullPath))
        {
            skipped.Add(new SkippedFile(fullPath, "Duplicate file ignored."));
            return;
        }

        var uniqueKey = GetFileUniqueIdKey(fullPath);
        if (uniqueKey is not null && !seenFileIds.Add(uniqueKey))
        {
            skipped.Add(new SkippedFile(fullPath, "Duplicate hardlinked file ignored."));
            return;
        }

        if (accepted.Count >= maxFiles)
        {
            skipped.Add(new SkippedFile(fullPath, $"Selection limit reached ({maxFiles} files)."));
            return;
        }

        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(fullPath);
        }
        catch (Exception ex)
        {
            skipped.Add(new SkippedFile(fullPath, $"Could not read file attributes: {ex.Message}"));
            return;
        }

        if ((attributes & FileAttributes.Directory) != 0)
        {
            skipped.Add(new SkippedFile(fullPath, "Path is a directory, not a file."));
            return;
        }

        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            skipped.Add(new SkippedFile(fullPath, "Reparse points/symlinks are not supported."));
            return;
        }

        accepted.Add(fullPath);
    }

    private static string? GetFileUniqueIdKey(string fullPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (GetFileInformationByHandle(stream.SafeFileHandle.DangerousGetHandle(), out var info) && info.nNumberOfLinks > 1)
            {
                return $"{info.dwVolumeSerialNumber}:{info.nFileIndexHigh}:{info.nFileIndexLow}";
            }
        }
        catch
        {
        }

        return null;
    }

    private static bool TryLoadTextFile(
        string path,
        ProcessingOptions options,
        out IncludedFile? includedFile,
        out string? reason)
    {
        includedFile = null;
        reason = null;

        var extension = Path.GetExtension(path);
        if (FileClassification.IsObviousBinaryExtension(extension))
        {
            reason = "Obvious binary/media/executable format was rejected.";
            return false;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var length = stream.Length;
            var maxRead = Math.Min((long)options.MaxBytesPerFile + 1, length);

            var buffer = new byte[maxRead];
            var offset = 0;
            while (offset < maxRead)
            {
                var read = stream.Read(buffer, offset, (int)(maxRead - offset));
                if (read <= 0)
                {
                    break;
                }

                offset += read;
            }

            var truncated = length > options.MaxBytesPerFile || offset > options.MaxBytesPerFile;
            var bytesToDecode = Math.Min(offset, options.MaxBytesPerFile);
            var bytes = buffer[..bytesToDecode];

            var strictCheck = !FileClassification.IsKnownTextExtension(extension);
            if (!TextDecoding.TryDecode(bytes, strictCheck, out var decoded))
            {
                reason = "Could not decode file as UTF-8/UTF-16 text.";
                return false;
            }

            if (truncated)
            {
                decoded += TruncatedSuffix;
            }

            includedFile = new IncludedFile(
                path,
                extension,
                decoded,
                truncated,
                bytesToDecode,
                length);

            return true;
        }
        catch (UnauthorizedAccessException ex)
        {
            reason = $"Access denied: {ex.Message}";
            return false;
        }
        catch (IOException ex)
        {
            reason = $"I/O error: {ex.Message}";
            return false;
        }
    }

    private static string GetSafeFence(string content)
    {
        var maxRun = 0;
        var currentRun = 0;
        foreach (var ch in content)
        {
            if (ch == '`')
            {
                currentRun++;
                if (currentRun > maxRun)
                {
                    maxRun = currentRun;
                }
            }
            else
            {
                currentRun = 0;
            }
        }

        return new string('`', Math.Max(3, maxRun + 1));
    }

    private static int CalculateFileBlockByteCount(IncludedFile file)
    {
        var fence = GetSafeFence(file.Content);
        var lang = FenceLanguageMap.FromExtension(file.Extension);
        var header = $"=== FILE: {file.FullPath} ===\n{fence}{lang}\n";
        var footer = $"\n{fence}\n\n";

        return Encoding.UTF8.GetByteCount(header) +
               Encoding.UTF8.GetByteCount(file.Content) +
               Encoding.UTF8.GetByteCount(footer);
    }

    private static int CalculateHeaderByteCount(int includedCount, int skippedCount)
    {
        var header = $"Selected files for LLM context.\nIncluded: {includedCount} files\nSkipped: {skippedCount} files\nPaths are absolute Windows paths.\n\n";
        return Encoding.UTF8.GetByteCount(header);
    }

    private static List<IncludedFile> ApplyTotalPayloadLimit(
        IReadOnlyList<IncludedFile> candidates,
        ICollection<SkippedFile> skipped,
        ProcessingOptions options)
    {
        var accepted = new List<IncludedFile>();
        var totalSkippedCount = skipped.Count;
        var currentFileBlocksByteCount = 0;

        foreach (var candidate in candidates)
        {
            var blockBytes = CalculateFileBlockByteCount(candidate);
            var testHeaderBytes = CalculateHeaderByteCount(accepted.Count + 1, totalSkippedCount);

            if (testHeaderBytes + currentFileBlocksByteCount + blockBytes <= options.MaxPayloadBytes)
            {
                accepted.Add(candidate);
                currentFileBlocksByteCount += blockBytes;
            }
            else
            {
                totalSkippedCount++;
                skipped.Add(new SkippedFile(candidate.FullPath, "Skipped because adding this file would exceed the total payload limit."));
            }
        }

        return accepted;
    }

    private static string BuildPayload(IReadOnlyList<IncludedFile> included, ICollection<SkippedFile> skipped)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Selected files for LLM context.");
        builder.AppendLine($"Included: {included.Count} files");
        builder.AppendLine($"Skipped: {skipped.Count} files");
        builder.AppendLine("Paths are absolute Windows paths.");
        builder.AppendLine();

        foreach (var file in included)
        {
            var fence = GetSafeFence(file.Content);
            builder.Append("=== FILE: ");
            builder.Append(file.FullPath);
            builder.AppendLine(" ===");
            builder.Append(fence);
            builder.Append(FenceLanguageMap.FromExtension(file.Extension));
            builder.AppendLine();
            builder.AppendLine(file.Content);
            builder.AppendLine(fence);
            builder.AppendLine();
        }

        return builder.ToString();
    }
}
