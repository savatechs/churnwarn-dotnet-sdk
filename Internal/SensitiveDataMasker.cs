using System.Text.RegularExpressions;

namespace ChurnWarn.Sdk.Internal;

/// <summary>
/// Bounded sensitive-data masking for telemetry payloads.
/// All methods are safe and never throw; on error a safe default is returned.
/// </summary>
internal static class SensitiveDataMasker
{
    private const string SafeDefault = "***";
    private const string SafePasswordMask = "********";
    private const string SafeCardMask = "****";
    private const int DefaultMaxScan = 2048;

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    private static readonly Regex EmailPattern = new(
        @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        RegexTimeout);

    private static readonly Regex CreditCardPattern = new(
        @"\b(?:\d{4}[-\s]?){3}\d{4}\b|\b\d{16}\b",
        RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex PhonePattern = new(
        @"\b(?:\+?1[-.\s]?)?\(?[0-9]{3}\)?[-.\s]?[0-9]{3}[-.\s]?[0-9]{4}\b",
        RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex SsnPattern = new(
        @"\b\d{3}[-\s]?\d{2}[-\s]?\d{4}\b",
        RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex JwtPattern = new(
        @"\beyJ[A-Za-z0-9_-]*\.eyJ[A-Za-z0-9_-]*\.[A-Za-z0-9_-]*\b",
        RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex ApiKeyPattern = new(
        @"\b(?:api[_-]?key|apikey|access[_-]?token|auth[_-]?token|bearer)\s*[:=]\s*[""']?([A-Za-z0-9_-]{20,})[""']?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        RegexTimeout);

    private static readonly Regex PasswordInUrlPattern = new(
        @"(?<=[:@])([^:@/\s]+)(?=@)",
        RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex IbanPattern = new(
        @"\b[A-Z]{2}\d{2}[A-Z0-9]{4}\d{7}[A-Z0-9]{0,16}\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        RegexTimeout);

    private static readonly Regex ControlCharsPattern = new(
        @"[\x00-\x1F\x7F]",
        RegexOptions.Compiled,
        RegexTimeout);

    public static string SanitizeForTelemetry(string? value, int maxLength = DefaultMaxScan)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        try
        {
            var sanitized = ControlCharsPattern.Replace(value, string.Empty);
            return sanitized.Length > maxLength ? sanitized[..maxLength] : sanitized;
        }
        catch
        {
            return SafeDefault;
        }
    }

    public static string MaskEmail(string? email)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                return SafeDefault;
            }

            var atIndex = email.IndexOf('@');
            if (atIndex <= 0 || atIndex >= email.Length - 1)
            {
                return SafeDefault;
            }

            var username = email[..atIndex];
            var domain = email[(atIndex + 1)..];

            if (username.Length <= 2)
            {
                return $"{SafeDefault}@{domain}";
            }

            return $"{username[0]}{SafeDefault}{username[^1]}@{domain}";
        }
        catch
        {
            return SafeDefault;
        }
    }

    public static string AutoMask(string? input, int maxLength = DefaultMaxScan)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return input ?? string.Empty;
        }

        try
        {
            var result = SanitizeForTelemetry(input, maxLength);
            if (result.Length == 0)
            {
                return result;
            }

            result = MaskJwtTokens(result);
            result = MaskApiKeys(result);
            result = MaskPasswordsInUrls(result);
            result = MaskCreditCards(result);
            result = MaskIbans(result);
            result = MaskSsns(result);
            result = MaskPhoneNumbers(result);
            result = MaskEmails(result);
            return result;
        }
        catch
        {
            return SanitizeForTelemetry(input, maxLength);
        }
    }

    public static string SafeUrlString(string? value)
    {
        var raw = SanitizeForTelemetry(value);
        if (raw.Length == 0)
        {
            return string.Empty;
        }

        try
        {
            if (Uri.TryCreate(raw, UriKind.Absolute, out var absolute))
            {
                return absolute.GetLeftPart(UriPartial.Path);
            }

            var cut = raw.Split('#')[0].Split('?')[0];
            return AutoMask(cut, 512);
        }
        catch
        {
            return SafeDefault;
        }
    }

    private static string ExtractDigits(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        Span<char> digits = stackalloc char[input.Length];
        var count = 0;
        foreach (var c in input)
        {
            if (char.IsDigit(c))
            {
                digits[count++] = c;
            }
        }

        return count == 0 ? string.Empty : new string(digits[..count]);
    }

    private static string MaskEmails(string input) =>
        EmailPattern.Replace(input, m => MaskEmail(m.Value));

    private static string MaskCreditCards(string input) =>
        CreditCardPattern.Replace(input, m =>
        {
            var card = ExtractDigits(m.Value);
            return card.Length < 4 ? SafeCardMask : $"****-****-****-{card[^4..]}";
        });

    private static string MaskPhoneNumbers(string input) =>
        PhonePattern.Replace(input, m =>
        {
            var phone = ExtractDigits(m.Value);
            if (phone.Length < 7)
            {
                return SafeDefault;
            }

            var first = phone[..Math.Min(3, phone.Length)];
            var last = phone[^4..];
            return $"{first}****{last}";
        });

    private static string MaskSsns(string input) =>
        SsnPattern.Replace(input, "***-**-****");

    private static string MaskJwtTokens(string input) =>
        JwtPattern.Replace(input, m =>
        {
            var token = m.Value;
            return token.Length <= 20 ? SafeDefault : $"{token[..10]}...{SafeDefault}";
        });

    private static string MaskApiKeys(string input) =>
        ApiKeyPattern.Replace(input, m =>
        {
            var fullMatch = m.Value;
            var keyValue = m.Groups[1].Value;
            if (string.IsNullOrEmpty(keyValue))
            {
                return fullMatch;
            }

            var masked = keyValue.Length > 8
                ? $"{keyValue[..4]}{SafeDefault}"
                : SafeDefault;
            return fullMatch.Replace(keyValue, masked);
        });

    private static string MaskPasswordsInUrls(string input) =>
        PasswordInUrlPattern.Replace(input, SafePasswordMask);

    private static string MaskIbans(string input) =>
        IbanPattern.Replace(input, m =>
        {
            var iban = m.Value;
            return iban.Length < 8 ? SafeDefault : $"{iban[..4]}{SafeDefault}{iban[^4..]}";
        });
}
