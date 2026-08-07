using System.Text.Json;
using System.Text.Json.Nodes;

namespace ChurnWarn.Sdk.Internal;

/// <summary>Recursive payload redaction for telemetry events.</summary>
internal static class TelemetryPrivacy
{
    private const string SafeDefault = "***";
    private const int MaxDepth = 8;
    private const int MaxKeys = 100;
    private const int MaxArrayItems = 100;

    private static readonly string[] SensitiveKeyFragments =
    [
        "password", "secret", "token", "api_key", "apikey", "authorization", "cookie", "ssn", "credit_card"
    ];

    public static RecordEventInput RedactRecord(RecordEventInput input)
    {
        try
        {
            if (!string.IsNullOrEmpty(input.PayloadJson))
            {
                return input with { PayloadJson = RedactPayloadJson(input.PayloadJson), Payload = null };
            }

            if (input.Payload is null)
            {
                return input;
            }

            var redacted = RedactValue(input.Payload, 0);
            return input with { Payload = redacted, PayloadJson = null };
        }
        catch
        {
            return input;
        }
    }

    public static string RedactPayloadJson(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return "{}";
        }

        try
        {
            var node = JsonNode.Parse(json);
            if (node is null)
            {
                return "{}";
            }

            var redacted = RedactJsonNode(node, 0);
            return redacted?.ToJsonString() ?? "{}";
        }
        catch
        {
            return SensitiveDataMasker.AutoMask(json);
        }
    }

    private static object? RedactValue(object? value, int depth)
    {
        if (value is null || depth > MaxDepth)
        {
            return value;
        }

        return value switch
        {
            string s => SensitiveDataMasker.AutoMask(s),
            bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal =>
                value,
            DateTime or DateTimeOffset or Guid => value,
            JsonElement el => RedactJsonElement(el, depth),
            JsonNode node => RedactJsonNode(node, depth),
            System.Collections.IDictionary dict => RedactDictionary(dict, depth),
            System.Collections.IEnumerable enumerable when value is not string => RedactEnumerable(enumerable, depth),
            _ => value
        };
    }

    private static JsonNode? RedactJsonNode(JsonNode? node, int depth)
    {
        if (node is null || depth > MaxDepth)
        {
            return node;
        }

        return node switch
        {
            JsonObject obj => RedactJsonObject(obj, depth),
            JsonArray arr => RedactJsonArray(arr, depth),
            JsonValue val when val.TryGetValue<string>(out var s) => SensitiveDataMasker.AutoMask(s),
            _ => node
        };
    }

    private static JsonObject RedactJsonObject(JsonObject obj, int depth)
    {
        var result = new JsonObject();
        var count = 0;
        foreach (var (key, value) in obj)
        {
            if (count >= MaxKeys)
            {
                break;
            }

            if (IsSensitiveKey(key))
            {
                result[key] = SafeDefault;
            }
            else if (IsUrlKey(key) && value is JsonValue urlVal && urlVal.TryGetValue<string>(out var urlStr))
            {
                result[key] = SensitiveDataMasker.SafeUrlString(urlStr);
            }
            else
            {
                result[key] = RedactJsonNode(value, depth + 1);
            }

            count++;
        }

        return result;
    }

    private static JsonArray RedactJsonArray(JsonArray arr, int depth)
    {
        var result = new JsonArray();
        var count = 0;
        foreach (var item in arr)
        {
            if (count >= MaxArrayItems)
            {
                break;
            }

            result.Add(RedactJsonNode(item, depth + 1));
            count++;
        }

        return result;
    }

    private static object? RedactJsonElement(JsonElement el, int depth)
    {
        return el.ValueKind switch
        {
            JsonValueKind.String => SensitiveDataMasker.AutoMask(el.GetString()),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null => el,
            JsonValueKind.Object => RedactJsonObject(JsonNode.Parse(el.GetRawText())!.AsObject(), depth),
            JsonValueKind.Array => RedactJsonArray(JsonNode.Parse(el.GetRawText())!.AsArray(), depth),
            _ => el
        };
    }

    private static Dictionary<string, object?> RedactDictionary(System.Collections.IDictionary dict, int depth)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        var count = 0;
        foreach (System.Collections.DictionaryEntry entry in dict)
        {
            if (count >= MaxKeys)
            {
                break;
            }

            var key = Convert.ToString(entry.Key) ?? string.Empty;
            if (IsSensitiveKey(key))
            {
                result[key] = SafeDefault;
            }
            else if (IsUrlKey(key))
            {
                result[key] = SensitiveDataMasker.SafeUrlString(Convert.ToString(entry.Value));
            }
            else
            {
                result[key] = RedactValue(entry.Value, depth + 1);
            }

            count++;
        }

        return result;
    }

    private static List<object?> RedactEnumerable(System.Collections.IEnumerable enumerable, int depth)
    {
        var result = new List<object?>();
        var count = 0;
        foreach (var item in enumerable)
        {
            if (count >= MaxArrayItems)
            {
                break;
            }

            result.Add(RedactValue(item, depth + 1));
            count++;
        }

        return result;
    }

    private static bool IsUrlKey(string key)
    {
        var lower = key.ToLowerInvariant();
        return lower is "url" or "referrer" || lower.EndsWith("url", StringComparison.Ordinal);
    }

    private static bool IsSensitiveKey(string key)
    {
        var lower = key.ToLowerInvariant();
        foreach (var fragment in SensitiveKeyFragments)
        {
            if (lower.Contains(fragment, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
