using System.Text;

namespace SelectAndConcat.Core;

internal static class TextDecoding
{
    private static readonly Encoding Utf8Strict = Encoding.GetEncoding(
        Encoding.UTF8.CodePage,
        EncoderFallback.ExceptionFallback,
        DecoderFallback.ExceptionFallback);

    private static readonly Encoding Utf16LeStrict = Encoding.GetEncoding(
        Encoding.Unicode.CodePage,
        EncoderFallback.ExceptionFallback,
        DecoderFallback.ExceptionFallback);

    private static readonly Encoding Utf16BeStrict = Encoding.GetEncoding(
        Encoding.BigEndianUnicode.CodePage,
        EncoderFallback.ExceptionFallback,
        DecoderFallback.ExceptionFallback);

    public static bool TryDecode(byte[] bytes, bool applyBinaryHeuristic, out string text)
    {
        text = string.Empty;

        if (bytes.Length == 0)
        {
            return true;
        }

        foreach (var encoding in GetCandidateEncodings(bytes))
        {
            try
            {
                var candidate = encoding.GetString(bytes);
                if (applyBinaryHeuristic && LooksBinary(candidate))
                {
                    continue;
                }

                text = candidate;
                return true;
            }
            catch (DecoderFallbackException)
            {
                var trimmedBytes = TrimIncompleteTrailingBytes(bytes, encoding);
                if (trimmedBytes.Length > 0 && trimmedBytes.Length < bytes.Length)
                {
                    try
                    {
                        var candidate = encoding.GetString(trimmedBytes);
                        if (applyBinaryHeuristic && LooksBinary(candidate))
                        {
                            continue;
                        }

                        text = candidate;
                        return true;
                    }
                    catch (DecoderFallbackException)
                    {
                    }
                }
            }
            catch (ArgumentException)
            {
            }
        }

        return false;
    }

    private static byte[] TrimIncompleteTrailingBytes(byte[] bytes, Encoding encoding)
    {
        if (encoding.CodePage == Encoding.UTF8.CodePage)
        {
            var len = bytes.Length;
            for (var i = 1; i <= Math.Min(4, len); i++)
            {
                var b = bytes[len - i];
                if ((b & 0x80) == 0)
                {
                    break;
                }

                if ((b & 0xC0) == 0xC0)
                {
                    var expectedLen = (b & 0xE0) == 0xC0 ? 2 :
                                      (b & 0xF0) == 0xE0 ? 3 :
                                      (b & 0xF8) == 0xF0 ? 4 : 0;
                    var actualLen = i;
                    if (expectedLen > 0 && actualLen < expectedLen)
                    {
                        return bytes[..(len - actualLen)];
                    }
                    break;
                }
            }
        }
        else if (encoding.CodePage == Encoding.Unicode.CodePage || encoding.CodePage == Encoding.BigEndianUnicode.CodePage)
        {
            var len = bytes.Length;
            if (len % 2 != 0)
            {
                len--;
            }

            if (len >= 2)
            {
                var isLe = encoding.CodePage == Encoding.Unicode.CodePage;
                ushort codeUnit = isLe
                    ? (ushort)(bytes[len - 2] | (bytes[len - 1] << 8))
                    : (ushort)((bytes[len - 2] << 8) | bytes[len - 1]);

                if (codeUnit >= 0xD800 && codeUnit <= 0xDBFF)
                {
                    len -= 2;
                }
            }

            if (len < bytes.Length)
            {
                return bytes[..len];
            }
        }

        return bytes;
    }

    private static IEnumerable<Encoding> GetCandidateEncodings(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            yield return Utf8Strict;
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            yield return Utf16LeStrict;
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            yield return Utf16BeStrict;
        }

        yield return Utf8Strict;
        yield return Utf16LeStrict;
        yield return Utf16BeStrict;
    }

    private static bool LooksBinary(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var suspiciousControls = 0;
        foreach (var ch in text)
        {
            if (ch == '\uFFFD')
            {
                suspiciousControls += 4;
                continue;
            }

            if (char.IsControl(ch) && ch is not ('\r' or '\n' or '\t' or '\f'))
            {
                suspiciousControls++;
            }
        }

        return suspiciousControls > Math.Max(3, text.Length / 100);
    }
}

