using System.Text.RegularExpressions;

namespace SelectAndConcat.Core;

/// <summary>
/// Heuristic redaction of passwords, API keys and tokens. It favours over-redaction:
/// a false positive hides a harmless value, a false negative leaks a credential.
/// </summary>
internal static class SecretRedactor
{
    public const string Placeholder = "[REDACTED]";

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(2);

    private const string KeyName =
        // "token" must start a word, a camelCase hump or a known compound, so jsonwebtoken / tokenizer-style names don't match.
        @"(?:passw(?:or)?d|passphrase|pwd|secret|(?-i:(?<![a-z])|(?<=[a-z])(?=[A-Z]))token|(?:api|auth|access|refresh|bearer|session|oauth|csrf|xsrf|id|device|bot|personal)token|api[_-]?key|access[_-]?key|private[_-]?key|account[_-]?key|signing[_-]?key|encryption[_-]?key|auth[_-]?key|credential|authorization)[\w.-]*" + NotMetadataSuffix;

    // Keys such as access_token_url, userSecretsId or credentialsFile name a secret but don't hold one.
    private const string NotMetadataSuffix =
        @"(?<![_.-](?:url|uri|id|ids|file|path|name|type|endpoint|length|len|count|size|bits|bytes|ttl|expiry|expires)|(?-i:[a-z](?:Id|Url|Uri|File|Path|Name|Type|Length|Count|Size)))";

    // key = value, key: value, "key": "value", key := v, key ?= v, key .= v, key => v, "Type key = v".
    // Bare values skip obvious type/literal words and must end the line, a comment, a quoted string or a
    // ;/& separated pair, so code like `Foo = bar });` is left alone.
    private static readonly Regex Assignment = new(
        @"(?<pre>" + KeyName + @"[""']?(?:(?:[ \t]+|[ \t]*:[ \t]*)[A-Za-z_][\w<>\[\]?.]*)?[ \t]*(?:[:?.+|]*=>?|:)(?!=)[ \t]*)" +
        @"(?:(?<q>[""'`])(?<v>.*?)\k<q>|(?!(?:string|number|boolean|any|null|nil|none|true|false|undefined|\d{1,2})\b)(?<b>(?:(?:Bearer|Basic)[ \t]+)?[^\s,;""'`}\])(]+)(?=[ \t]*(?:#.*|//.*)?\r?$|[;&]\w+=|\\[nr]|[""'`]))",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant, MatchTimeout);

    // setProperty("API_TOKEN", "value"), env.Set('SECRET', 'value')
    private static readonly Regex CallArgument = new(
        @"(?<pre>" + KeyName + @"[""'`][ \t]*,[ \t]*)(?<q>[""'`])(?<v>.*?)\k<q>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout);

    private static readonly Regex XmlElement = new(
        @"(<[\w.-]*(?:passw(?:or)?d|passphrase|secret|token|api[_-]?key)[\w.-]*" + NotMetadataSuffix + @">)(\s*)([^<\s][^<]{0,511}?)(\s*)(</)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout);

    private static readonly Regex PrivateKeyBlock = new(
        @"-----BEGIN [A-Z ]*PRIVATE KEY(?: BLOCK)?-----[\s\S]*?-----END [A-Z ]*PRIVATE KEY(?: BLOCK)?-----",
        RegexOptions.CultureInvariant, MatchTimeout);

    // curl -u user:pass, curl --user user:pass
    private static readonly Regex CurlUser = new(
        @"(?<=(?:^|\s)(?:-u|--user)(?:\s+|=)[""']?)[^\s:""'$<{]+:[^\s""'$<{\[]+",
        RegexOptions.Multiline | RegexOptions.CultureInvariant, MatchTimeout);

    private static readonly Regex UrlCredentials = new(
        @"(?<=://[^/\s:@]+:)[^@\s/]+(?=@)",
        RegexOptions.CultureInvariant, MatchTimeout);

    private static readonly Regex Bearer = new(
        @"(?<=\bBearer\s+)[A-Za-z0-9._~+/=-]{16,}",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout);

    // Well-known token shapes, redacted wherever they appear.
    private static readonly Regex KnownTokens = new(
        // Cloud and source hosting
        @"\b(?:A3T[A-Z0-9]|AKIA|AGPA|AIDA|AIPA|ANPA|ANVA|AROA|ASIA)[A-Z0-9]{16}\b|\bda2-[a-z0-9]{26}\b|\bamzn\.mws\.[0-9a-f-]{36}\b" +
        @"|\bgh[pousr]_[A-Za-z0-9]{36,}\b|\bgithub_pat_[A-Za-z0-9_]{22,}\b|\bglpat-[A-Za-z0-9_-]{20,}|\bglsa_[A-Za-z0-9_]{32,}" +
        @"|\bAIza[0-9A-Za-z_-]{35}\b|\bya29\.[A-Za-z0-9_-]{20,}|\bdop_v1_[a-f0-9]{64}\b|\bdp\.(?:pt|st|sa|ct)\.[A-Za-z0-9]{30,}" +
        // Package registries
        @"|\bnpm_[A-Za-z0-9]{36}\b|\bpypi-AgE[A-Za-z0-9_-]{20,}|\brubygems_[a-f0-9]{48}\b" +
        // AI and payments
        @"|\bsk-[A-Za-z0-9_-]{20,}|\bhf_[A-Za-z]{34}\b|\b[sr]k_(?:live|test|prod)_[A-Za-z0-9_-]{16,}|\bwhsec_[A-Za-z0-9]{24,}" +
        @"|\bsq0(?:atp|csp|idp)-[A-Za-z0-9_-]{22,}|\bshp(?:at|ca|pa|ss)_[a-fA-F0-9]{32}\b|\bFLWSECK(?:_TEST)?-[A-Za-z0-9-]{20,}" +
        // Messaging, email, SaaS
        @"|\bxox[baprse]-[A-Za-z0-9-]{10,}|\bSG\.[A-Za-z0-9_-]{22}\.[A-Za-z0-9_-]{43}\b|\bxkeysib-[A-Za-z0-9-]{40,}" +
        @"|\bkey-[0-9a-zA-Z]{32}\b|\bPMAK-[A-Za-z0-9-]{20,}|\bNR(?:AA|II|RA|SP)-[A-Za-z0-9_-]{20,}|\blin_api_[A-Za-z0-9]{30,}" +
        @"|\bapify_api_[A-Za-z0-9_-]{30,}|\baio_[A-Za-z0-9]{28}\b|\bphc_[A-Za-z0-9_]{30,}|\bBBFF-[A-Za-z0-9]{30,}|\bsl\.[A-Za-z0-9_-]{40,}" +
        // More vendors with distinctive prefixes
        @"|\bpscale_(?:tkn|pw|oauth)_[A-Za-z0-9=._-]{20,}|\bsntrys_[A-Za-z0-9+/=_-]{40,}|\bshippo_(?:live|test)_[a-f0-9]{40}\b" +
        @"|\b(?:pat|sat)\.[A-Za-z0-9]{20,}\.[A-Za-z0-9]{20,}\.[A-Za-z0-9]{20,}|\bsq[pua]_[a-f0-9]{40}\b|\bapi_org_[A-Za-z]{34}\b" +
        @"|\bEZ[AT]K[A-Za-z0-9]{54}\b|\bpplx-[A-Za-z0-9]{40,}|\bv1\.0-[a-f0-9]{24}-[a-f0-9]{100,}|\bfo1_[A-Za-z0-9_-]{30,}" +
        @"|\bhv[sbr]\.[A-Za-z0-9_-]{24,}|\bHRKU-AA[A-Za-z0-9_-]{50,}|\bglc_[A-Za-z0-9+/=]{32,}|\bsha256~[A-Za-z0-9_-]{43}\b" +
        @"|\bpul-[a-f0-9]{40}\b|\bpnu_[A-Za-z0-9]{36}\b|\bNRAK-[A-Z0-9]{27}\b|\bNRJS-[a-f0-9]{19}\b|\brdme_[a-z0-9]{70}" +
        @"|\btk-us-[A-Za-z0-9_-]{40,}|\bpubkey-[a-f0-9]{32}\b|\b[a-f0-9]{32}-us\d{1,2}\b|\bA3-[A-Z0-9]{6}(?:-[A-Z0-9]{5,11}){3,4}\b" +
        @"|\baccess-(?:sandbox|development|production)-[0-9a-f-]{36}\b|\b(?:ABIA|ACCA)[A-Z0-9]{16}\b|\b\d{8,10}:A[A-Za-z0-9_-]{34}\b" +
        @"|\bZXlK[A-Za-z0-9+/=]{30,}|\bops_eyJ[A-Za-z0-9+/=]{50,}|\bCLOJARS_[a-f0-9]{60}\b" +
        // Webhooks (the URL is the credential)
        @"|hooks\.slack\.com/(?:services|workflows|triggers)/[A-Za-z0-9/_-]{20,}|webhook\.office\.com/webhookb2/[A-Za-z0-9@-]{30,}" +
        @"|discord(?:app)?\.com/api/webhooks/\d+/[A-Za-z0-9_-]{40,}" +
        // GitLab, DigitalOcean, Databricks and friends
        @"|\bgl(?:rt|ptt|oas|agent|imt|ft|ffct|cbt|soat)-[A-Za-z0-9_.-]{20,}|\bGR1348941[A-Za-z0-9_-]{20}\b|\bdo[opr]_v1_[a-f0-9]{64}\b" +
        @"|\bdapi[a-f0-9]{32}\b|\bsm_(?:sat|pat|aat)_[a-z0-9]{16}\b|\bsntryu_[a-f0-9]{64}\b|\bsgp_[A-Za-z0-9_]{16,}|\bntn_[A-Za-z0-9]{40,}" +
        @"|\bxapp-\d-[A-Z0-9]+-\d+-[a-z0-9]{40,}|\bxoxe(?:\.xox[bp])?-\d-[A-Za-z0-9]{40,}|\bEAA[A-Za-z0-9]{60,}|\b\d{15,16}\|[A-Za-z0-9_-]{27}\b" +
        // AI platforms
        @"|\br8_[A-Za-z0-9]{37}\b|\bgsk_[A-Za-z0-9]{52}\b|\bxai-[A-Za-z0-9]{80}\b|\bfw_[A-Za-z0-9]{24}\b|\bpcsk_[A-Za-z0-9_]{50,}|\btvly-[A-Za-z0-9-]{30,}" +
        // Developer tooling
        @"|\bdckr_pat_[A-Za-z0-9_-]{27}\b|\bfigd_[A-Za-z0-9_-]{40}\b|\bsbp_[a-f0-9]{40}\b|\bico-[A-Za-z0-9]{32}\b|\bSK[0-9a-f]{32}\b" +
        @"|\bAGE-SECRET-KEY-1[A-Z0-9]{58}\b|\bABSK[A-Za-z0-9+/=]{100,}|\bbedrock-api-key-[A-Za-z0-9+/=]{20,}" +
        @"|\b[A-Za-z0-9]{14}\.atlasv1\.[A-Za-z0-9=_-]{60,}|\bduffel_(?:test|live)_[A-Za-z0-9_=-]{43}\b|\bpat[A-Za-z0-9]{14}\.[a-f0-9]{64}\b" +
        @"|\bdt0[a-z]\d{2}\.[A-Za-z0-9]{24}\.[A-Za-z0-9]{64}\b|\beyJrIjoi[A-Za-z0-9=]{50,}|\btfp_[A-Za-z0-9_=-]{40,}" +
        @"|\bdnkey-[A-Za-z0-9=_-]{26}-[A-Za-z0-9=_-]{52}\b|\bfio-u-[A-Za-z0-9_=-]{60,}|\bAQVN[A-Za-z0-9_-]{35,38}\b" +
        @"|\bAAAAAAAAAAAAAAAAAAAAA[A-Za-z0-9%]{30,}|\b[ps]k\.[A-Za-z0-9]{40,}\.[A-Za-z0-9_-]{20,}|\bs-s4t2(?:ud|af)-[a-f0-9]{64}\b" +
        @"|\bFLWPUBK(?:_TEST)?-[A-Za-z0-9-]{20,}|\b[A-Za-z0-9_~.-]{3}\dQ~[A-Za-z0-9_~.-]{31,34}\b|\bSSWS\s+[A-Za-z0-9_-]{40}\b" +
        // Discord bot tokens
        @"|\b[MN][A-Za-z\d]{23,25}\.[\w-]{6}\.[\w-]{27,38}\b" +
        // JWTs
        @"|\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]*",
        RegexOptions.CultureInvariant, MatchTimeout);

    /// <summary>
    /// Returns false when a pattern timed out; the caller must then withhold the text rather than risk leaking it.
    /// </summary>
    public static bool TryRedact(string text, out string redacted, out int count)
    {
        var n = 0;
        try
        {
            text = PrivateKeyBlock.Replace(text, _ => { n++; return Placeholder; });
            text = Assignment.Replace(text, m =>
            {
                var quote = m.Groups["q"];
                if (quote.Success)
                {
                    if (m.Groups["v"].Value.Length == 0 || m.Groups["v"].Value == Placeholder)
                    {
                        return m.Value;
                    }

                    n++;
                    return m.Groups["pre"].Value + quote.Value + Placeholder + quote.Value;
                }

                if (m.Groups["b"].Value == Placeholder)
                {
                    return m.Value;
                }

                n++;
                return m.Groups["pre"].Value + Placeholder;
            });
            text = CallArgument.Replace(text, m =>
            {
                if (m.Groups["v"].Value.Length == 0 || m.Groups["v"].Value == Placeholder)
                {
                    return m.Value;
                }

                n++;
                return m.Groups["pre"].Value + m.Groups["q"].Value + Placeholder + m.Groups["q"].Value;
            });
            text = XmlElement.Replace(text, m =>
            {
                if (m.Groups[3].Value == Placeholder)
                {
                    return m.Value;
                }

                n++;
                return m.Groups[1].Value + m.Groups[2].Value + Placeholder + m.Groups[4].Value + m.Groups[5].Value;
            });
            text = CurlUser.Replace(text, _ => { n++; return Placeholder; });
            text = UrlCredentials.Replace(text, _ => { n++; return Placeholder; });
            text = Bearer.Replace(text, _ => { n++; return Placeholder; });
            text = KnownTokens.Replace(text, _ => { n++; return Placeholder; });
        }
        catch (RegexMatchTimeoutException)
        {
            redacted = string.Empty;
            count = 0;
            return false;
        }

        redacted = text;
        count = n;
        return true;
    }
}
