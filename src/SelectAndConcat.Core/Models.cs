namespace SelectAndConcat.Core;

public sealed class ProcessingOptions
{
    public int MaxFiles { get; init; } = 50;

    public int MaxBytesPerFile { get; init; } = 512 * 1024;

    public int MaxPayloadBytes { get; init; } = 2 * 1024 * 1024;
}

public enum ProcessExitCode
{
    Success = 0,
    PartialSuccess = 1,
    Failure = 2,
}

public sealed record IncludedFile(
    string FullPath,
    string Extension,
    string Content,
    bool WasTruncated,
    int BytesRead,
    long OriginalLengthBytes);

public sealed record SkippedFile(string Path, string Reason);

public sealed record ProcessResult(
    ProcessExitCode ExitCode,
    string ClipboardPayload,
    IReadOnlyList<IncludedFile> Included,
    IReadOnlyList<SkippedFile> Skipped)
{
    public int TruncatedCount => Included.Count(x => x.WasTruncated);
}
