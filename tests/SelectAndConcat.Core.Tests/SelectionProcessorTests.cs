using System.Text;
using SelectAndConcat.Core;

namespace SelectAndConcat.Core.Tests;

public sealed class SelectionProcessorTests : IDisposable
{
    private readonly string _tempRoot;

    public SelectionProcessorTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "SelectAndConcatTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    [Fact]
    public void Dedupe_IsCaseInsensitive_AndSkipsDuplicate()
    {
        var file = CreateFile("a.txt", "hello");

        var result = new SelectionProcessor().Process(new[] { file, file.ToUpperInvariant() });

        Assert.Single(result.Included);
        Assert.Contains(result.Skipped, x => x.Reason.Contains("Duplicate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DirectorySelection_IncludesFilesRecursively()
    {
        var rootFile = CreateFile("docs\\root.txt", "root file");
        var nestedFile = CreateFile("docs\\nested\\child.cs", "class Child {}");

        var result = new SelectionProcessor().Process(new[] { Path.Combine(_tempRoot, "docs") });

        Assert.Equal(2, result.Included.Count);
        Assert.Contains(result.Included, x => x.FullPath == rootFile);
        Assert.Contains(result.Included, x => x.FullPath == nestedFile);
        Assert.Contains("root file", result.ClipboardPayload);
        Assert.Contains("class Child {}", result.ClipboardPayload);
    }

    [Fact]
    public void MixedFileAndDirectorySelection_DedupesExpandedFiles()
    {
        var file = CreateFile("src\\one.txt", "one");
        var directory = Path.Combine(_tempRoot, "src");

        var result = new SelectionProcessor().Process(new[] { file, directory });

        Assert.Single(result.Included);
        Assert.Contains(result.Skipped, x => x.Reason.Contains("Duplicate file", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Rejects_BinaryByExtension()
    {
        var file = CreateBytes("tool.exe", new byte[] { 0x4D, 0x5A, 0x90, 0x00 });

        var result = new SelectionProcessor().Process(new[] { file });

        Assert.Empty(result.Included);
        Assert.Contains(result.Skipped, x => x.Reason.Contains("binary", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(ProcessExitCode.Failure, result.ExitCode);
    }

    [Fact]
    public void Decodes_Utf16UnknownExtension()
    {
        var file = Path.Combine(_tempRoot, "notes.unknown");
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("hello utf16")).ToArray();
        File.WriteAllBytes(file, bytes);

        var result = new SelectionProcessor().Process(new[] { file });

        Assert.Single(result.Included);
        Assert.Contains("hello utf16", result.Included[0].Content);
    }

    [Fact]
    public void Truncates_PerFileLimit()
    {
        var file = CreateFile("big.txt", new string('a', 100));

        var result = new SelectionProcessor().Process(
            new[] { file },
            new ProcessingOptions { MaxBytesPerFile = 20, MaxPayloadBytes = 1024 * 1024, MaxFiles = 50 });

        Assert.Single(result.Included);
        Assert.True(result.Included[0].WasTruncated);
        Assert.Contains("TRUNCATED", result.Included[0].Content);
    }

    [Fact]
    public void Truncates_PerFileLimit_WithMidCodepointUtf8()
    {
        // 100 multi-byte Japanese characters (3 bytes each in UTF-8 = 300 bytes)
        var japaneseText = new string('あ', 100);
        var file = CreateFileNoBom("japanese.txt", japaneseText);

        // Cap at 10 bytes (which lands in the middle of 4th character: byte 10 is 1st byte of 4th 'あ')
        var result = new SelectionProcessor().Process(
            new[] { file },
            new ProcessingOptions { MaxBytesPerFile = 10, MaxPayloadBytes = 1024 * 1024, MaxFiles = 50 });

        Assert.Single(result.Included);
        Assert.True(result.Included[0].WasTruncated);
        Assert.Contains("TRUNCATED", result.Included[0].Content);
        // Ensure 3 complete 'あ' characters were preserved before truncation suffix
        Assert.StartsWith("あああ", result.Included[0].Content);
    }

    [Fact]
    public void FenceEscaping_AdaptsToContentWithBackticks()
    {
        var contentWithFences = "Here is markdown:\n```js\nconsole.log(1);\n```\nend";
        var file = CreateFile("doc.md", contentWithFences);

        var result = new SelectionProcessor().Process(new[] { file });

        Assert.Single(result.Included);
        // Since content contains 3 backticks, fence should use at least 4 backticks
        Assert.Contains("````", result.ClipboardPayload);
    }

    [Fact]
    public void Handles_InvalidPathCharacters_WithoutCrashing()
    {
        var invalidPath = "C:\\test\0file.txt";

        var result = new SelectionProcessor().Process(new[] { invalidPath });

        Assert.Empty(result.Included);
        Assert.Single(result.Skipped);
        Assert.Contains("Invalid path format", result.Skipped[0].Reason);
    }

    [Fact]
    public void Excludes_SensitiveSecretFiles()
    {
        var envFile = CreateFile(".env", "SECRET_KEY=12345");
        var pemFile = CreateFile("server.pem", "-----BEGIN PRIVATE KEY-----");
        var regularFile = CreateFile("main.cs", "class App {}");

        var result = new SelectionProcessor().Process(new[] { envFile, pemFile, regularFile });

        Assert.Single(result.Included);
        Assert.Equal(regularFile, result.Included[0].FullPath);
        Assert.Equal(2, result.Skipped.Count);
        Assert.All(result.Skipped, x => Assert.Contains("sensitive/secret", x.Reason, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Ignores_BuildAndDependencyDirectories()
    {
        var sourceFile = CreateFile("app\\index.js", "console.log(1);");
        var nodeModulesFile = CreateFile("app\\node_modules\\dep\\index.js", "module.exports = {};");

        var result = new SelectionProcessor().Process(new[] { Path.Combine(_tempRoot, "app") });

        Assert.Single(result.Included);
        Assert.Equal(sourceFile, result.Included[0].FullPath);
        Assert.Contains(result.Skipped, x => x.Reason.Contains("Ignored build/dependency", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Stops_WhenTotalPayloadWouldExceedLimit()
    {
        var one = CreateFile("one.txt", new string('a', 200));
        var two = CreateFile("two.txt", new string('b', 200));

        var result = new SelectionProcessor().Process(
            new[] { one, two },
            new ProcessingOptions { MaxBytesPerFile = 1024, MaxPayloadBytes = 500, MaxFiles = 50 });

        Assert.Single(result.Included);
        Assert.Contains(result.Skipped, x => x.Reason.Contains("payload limit", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(ProcessExitCode.PartialSuccess, result.ExitCode);
    }

    [Fact]
    public void Formatter_UsesFenceLanguageByExtension()
    {
        var file = CreateFile("code.ts", "const x = 1;");

        var result = new SelectionProcessor().Process(new[] { file });

        Assert.Contains("=== FILE:", result.ClipboardPayload);
        Assert.Contains("```ts", result.ClipboardPayload);
        Assert.Contains(file, result.ClipboardPayload);
    }

    [Fact]
    public void TruncationNotice_ReportsConfiguredLimit()
    {
        var file = CreateFile("big2.txt", new string('a', 8192));

        var result = new SelectionProcessor().Process(
            new[] { file },
            new ProcessingOptions { MaxBytesPerFile = 4096 });

        Assert.Contains("4 KiB limit", result.Included[0].Content);
    }

    [Fact]
    public void Bom_IsNotPartOfContent()
    {
        var file = CreateFile("bom.txt", "hello");

        var result = new SelectionProcessor().Process(new[] { file });

        Assert.Equal("hello", result.Included[0].Content);
    }

    [Theory]
    [InlineData(".npmrc")]
    [InlineData("credentials")]
    [InlineData("deploy.ppk")]
    [InlineData("prod.tfvars")]
    public void Excludes_AdditionalSecretFiles(string name)
    {
        var file = CreateFile(name, "secret");

        var result = new SelectionProcessor().Process(new[] { file });

        Assert.Empty(result.Included);
        Assert.Contains(result.Skipped, x => x.Reason.Contains("sensitive", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DirectoryExpansion_StopsScanningAtFileLimit()
    {
        for (var i = 0; i < 20; i++)
        {
            CreateFile($"many\\f{i:D2}.txt", "x");
        }

        var result = new SelectionProcessor().Process(
            new[] { Path.Combine(_tempRoot, "many") },
            new ProcessingOptions { MaxFiles = 3 });

        Assert.Equal(3, result.Included.Count);
        Assert.Single(result.Skipped);
    }

    [Fact]
    public void IgnoredDirectoriesAlone_DoNotCauseWarningExitCode()
    {
        CreateFile("proj/a.cs", "class A {}");
        CreateFile("proj/bin/x.txt", "x");

        var result = new SelectionProcessor().Process(new[] { Path.Combine(_tempRoot, "proj") });

        Assert.Equal(ProcessExitCode.Success, result.ExitCode);
    }

    private string CreateFile(string name, string content)
    {
        var path = Path.Combine(_tempRoot, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, Encoding.UTF8);
        return path;
    }

    private string CreateFileNoBom(string name, string content)
    {
        var path = Path.Combine(_tempRoot, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    private string CreateBytes(string name, byte[] content)
    {
        var path = Path.Combine(_tempRoot, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }
}
