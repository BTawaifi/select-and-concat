using SelectAndConcat.Core;

namespace SelectAndConcat.Core.Tests;

public sealed class SecretRedactorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SelectAndConcatTests", Guid.NewGuid().ToString("N"));

    public SecretRedactorTests() => Directory.CreateDirectory(_root);

    // Fake credentials are assembled at runtime so this file never contains a real-looking token.
    private static readonly string Fake = "Zx9" + "Qw7" + "Lm4" + "Kp2" + "Vb8";

    public static IEnumerable<object[]> Secrets() => new[]
    {
        new object[] { $"password = {Fake}" },
        new object[] { $"DB_PASSWORD={Fake}" },
        new object[] { $"\"apiKey\": \"{Fake}\"" },
        new object[] { $"api_key: '{Fake}'" },
        new object[] { $"client_secret = \"{Fake}\"" },
        new object[] { $"<Password>{Fake}</Password>" },
        new object[] { $"Authorization: Bearer {Fake}{Fake}" },
        new object[] { $"conn = postgres://admin:{Fake}@db.local/app" },
        new object[] { "key=" + "AKIA" + "ABCDEFGHIJKLMNOP" },
        new object[] { "t = " + "ghp_" + new string('a', 36) },
        new object[] { "t = " + "sk-" + new string('b', 30) },
        new object[] { $"var atlassianToken string = \"{Fake}\"" },
        new object[] { $"$mailToken .= \"{Fake}\";" },
        new object[] { $"API_TOKEN :::= \"{Fake}\"" },
        new object[] { $"System.setProperty(\"SERVICE_TOKEN\", \"{Fake}\");" },
        new object[] { $"{{\"config.ini\": \"VAULT_TOKEN={Fake}\nBACKUP=true\"}}" },
        new object[] { $"<ServiceToken> {Fake} </ServiceToken>" },
        new object[] { $"\"Authorization\" => \"{Fake}\"" },
        new object[] { $"Server=db;Password={Fake};User=bob" },
        new object[] { $"password={Fake}   # prod" },
        new object[] { "t = " + "glpat" + "-" + new string('c', 20) },
        new object[] { "t = " + "shp" + "at_" + new string('a', 32) },
        new object[] { "t = " + "sq0" + "atp-" + new string('d', 22) },
        new object[] { "t = " + "pypi-" + "AgE" + new string('e', 30) },
        new object[] { "t = " + "NRAA" + "-" + new string('f', 27) },
        new object[] { "t = " + "sk_" + "live_" + new string('g', 10) + "-" + new string('g', 10) },
        new object[] { "-----BEGIN RSA PRIVATE KEY-----\nMIIEabc\n-----END RSA PRIVATE KEY-----" },
    };

    [Theory]
    [MemberData(nameof(Secrets))]
    public void Redacts_SecretLikeValues(string input)
    {
        var ok = SecretRedactor.TryRedact(input, out var output, out var count);

        Assert.True(ok);
        Assert.True(count >= 1);
        Assert.Contains(SecretRedactor.Placeholder, output);
        Assert.DoesNotContain(Fake, output);
        Assert.DoesNotContain("MIIEabc", output);
        Assert.DoesNotContain("ABCDEFGHIJKLMNOP", output);
        Assert.DoesNotContain(new string('a', 36), output);
        Assert.DoesNotContain(new string('b', 30), output);
        foreach (var c in "cdefga")
        {
            Assert.DoesNotContain(new string(c, 20), output);
        }

    }

    [Theory]
    [InlineData("var author = \"Jane\";")]
    [InlineData("bool passed = true;")]
    [InlineData("password: string;")]
    [InlineData("access_token_url='https://example.com/oauth/access_token',")]
    [InlineData("<UserSecretsId>79a3edd0-2092-40a2-a04d-dcb46d5ca9ed</UserSecretsId>")]
    [InlineData("credentialsId: 'build-agent'")]
    [InlineData("if (token == expected) return;")]
    [InlineData("new ProcessingOptions { RedactSecrets = redact });")]
    [InlineData("this.token = tokenProvider.Next();")]
    [InlineData("var password = \"\";")]
    [InlineData("Console.WriteLine(\"hello\");")]
    public void LeavesOrdinaryCodeAlone(string input)
    {
        SecretRedactor.TryRedact(input, out var output, out var count);

        Assert.Equal(input, output);
        Assert.Equal(0, count);
    }

    // (prefix, filler char, filler length): fake tokens are assembled at runtime from public vendor formats.
    public static IEnumerable<object[]> VendorFormats() => new[]
    {
        new object[] { "hooks.slack.com/services/", 'T', 30 },
        new object[] { "discord.com/api/webhooks/123456789/", 'a', 50 },
        new object[] { "glrt-", 'a', 24 },
        new object[] { "dop_v1_", 'a', 64 },
        new object[] { "dapi", 'a', 32 },
        new object[] { "sntryu_", 'a', 64 },
        new object[] { "sgp_", 'a', 20 },
        new object[] { "xoxe-1-", 'A', 50 },
        new object[] { "r8_", 'A', 37 },
        new object[] { "gsk_", 'A', 52 },
        new object[] { "xai-", 'A', 80 },
        new object[] { "dckr_pat_", 'A', 27 },
        new object[] { "figd_", 'A', 40 },
        new object[] { "sbp_", 'a', 40 },
        new object[] { "AGE-SECRET-KEY-1", 'Q', 58 },
        new object[] { "sk_prod_", 'a', 40 },
        new object[] { "AQVN", 'A', 36 },
        new object[] { "ntn_", 'a', 45 },
        new object[] { "pcsk_", 'a', 55 },
    };

    [Theory]
    [MemberData(nameof(VendorFormats))]
    public void Redacts_VendorFormats_WithoutKeywordContext(string prefix, char filler, int length)
    {
        var secret = prefix + new string(filler, length);
        var input = $"value = \"{secret}\"";

        SecretRedactor.TryRedact(input, out var output, out var count);

        Assert.True(count >= 1);
        Assert.DoesNotContain(new string(filler, 20), output);
    }

    [Fact]
    public void Redacts_ValueOnItsOwnLineInsideXml()
    {
        var xml = $"<ETSYToken>\n    {Fake}\n</ETSYToken>";

        SecretRedactor.TryRedact(xml, out var output, out var count);

        Assert.Equal(1, count);
        Assert.DoesNotContain(Fake, output);
        Assert.Equal("<ETSYToken>\n    [REDACTED]\n</ETSYToken>", output);
    }

    [Fact]
    public void Redacts_CurlUserCredentials_ButNotShellVariables()
    {
        SecretRedactor.TryRedact($"curl -u admin:{Fake} https://x.test", out var literal, out _);
        SecretRedactor.TryRedact("curl --user \"$USER:$PASS\" https://x.test", out var variables, out var count);

        Assert.DoesNotContain(Fake, literal);
        Assert.Equal("curl --user \"$USER:$PASS\" https://x.test", variables);
        Assert.Equal(0, count);
    }

    [Fact]
    public void Redacts_PgpPrivateKeyBlock()
    {
        var pgp = "-----BEGIN PGP PRIVATE KEY BLOCK-----\nlQWGBGSVV4YB\n-----END PGP PRIVATE KEY BLOCK-----";

        SecretRedactor.TryRedact(pgp, out var output, out _);

        Assert.DoesNotContain("lQWGBGSVV4YB", output);
    }

    [Theory]
    [InlineData("<dependency>io.jsonwebtoken:jjwt-jackson-0.11.2</dependency>")]
    [InlineData("notes = \"Maven - io.jsonwebtoken:jjwt-jackson-0.11.2\"")]
    [InlineData("secret_length = X25519.secret_length")]
    [InlineData("PuttyPassword=0")]
    [InlineData("curl -u \"$1:$2\" \"$3\"")]
    public void LeavesMoreOrdinaryTextAlone(string input)
    {
        SecretRedactor.TryRedact(input, out var output, out var count);

        Assert.Equal(input, output);
        Assert.Equal(0, count);
    }

    [Fact]
    public void Redaction_KeepsKeyAndQuotes_AndIsIdempotent()
    {
        SecretRedactor.TryRedact($"\"password\": \"{Fake}\"", out var once, out _);
        SecretRedactor.TryRedact(once, out var twice, out var count);

        Assert.Equal("\"password\": \"[REDACTED]\"", once);
        Assert.Equal(once, twice);
        Assert.Equal(0, count);
    }

    [Fact]
    public void Process_RedactsByDefault_AndCanBeDisabled()
    {
        var path = Path.Combine(_root, "settings.txt");
        File.WriteAllText(path, $"user=bob\npassword={Fake}\n");

        var on = new SelectionProcessor().Process(new[] { path });
        var off = new SelectionProcessor().Process(new[] { path }, new ProcessingOptions { RedactSecrets = false });

        Assert.DoesNotContain(Fake, on.ClipboardPayload);
        Assert.Contains("user=bob", on.ClipboardPayload);
        Assert.Equal(1, on.RedactedCount);
        Assert.Contains(Fake, off.ClipboardPayload);
        Assert.Equal(0, off.RedactedCount);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
