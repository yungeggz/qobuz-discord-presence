using System.Text.Json;

namespace QobuzPresence.Helpers;

public static class JsonElementHelper
{
    public static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement property)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out property))
        {
            return true;
        }

        property = default;
        return false;
    }

    public static bool TryGetNestedProperty(JsonElement root, out JsonElement value, params string[] propertyNames)
    {
        value = root;

        foreach (string propertyName in propertyNames)
        {
            if (!TryGetProperty(value, propertyName, out value))
            {
                value = default;
                return false;
            }
        }

        return true;
    }

    public static string? GetString(JsonElement element, string propertyName)
    {
        return TryGetProperty(element, propertyName, out JsonElement property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    public static string? GetNestedString(JsonElement element, params string[] path)
    {
        JsonElement current = element;

        foreach (string segment in path)
        {
            if (!TryGetProperty(current, segment, out current))
            {
                return null;
            }
        }

        return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
    }

    public static int? GetInt32(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out JsonElement property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out int value)
            ? value
            : null;
    }

    public static long? GetInt64(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out JsonElement property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out long value)
            ? value
            : null;
    }

    public static double? GetDouble(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out JsonElement property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out double value)
            ? value
            : null;
    }

    public static bool? GetBool(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out JsonElement property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }
}
