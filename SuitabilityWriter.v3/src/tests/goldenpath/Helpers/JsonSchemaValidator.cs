using System.Text.Json;
using System.Text.RegularExpressions;

public static class JsonSchemaValidator
{
    public static void Validate(string json, string schemaPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaPath);

        var resolvedSchemaPath = ResolveSchemaPath(schemaPath);
        if (!File.Exists(resolvedSchemaPath))
        {
            throw new FileNotFoundException($"Schema file was not found: {resolvedSchemaPath}", resolvedSchemaPath);
        }

        using var jsonDoc = JsonDocument.Parse(json);
        using var schemaDoc = JsonDocument.Parse(File.ReadAllText(resolvedSchemaPath));

        var errors = new List<string>();
        var schemaRoot = schemaDoc.RootElement;
        var jsonRoot = jsonDoc.RootElement;

        if (jsonRoot.ValueKind != JsonValueKind.Object)
        {
            errors.Add("JSON payload must be an object.");
        }

        if (schemaRoot.TryGetProperty("required", out var required))
        {
            foreach (var property in required.EnumerateArray())
            {
                var name = property.GetString();
                if (!string.IsNullOrWhiteSpace(name) && !jsonRoot.TryGetProperty(name, out _))
                {
                    errors.Add($"Missing required property '{name}'.");
                }
            }
        }

        if (schemaRoot.TryGetProperty("properties", out var properties))
        {
            var allowedProperties = properties.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

            if (schemaRoot.TryGetProperty("additionalProperties", out var additionalProperties)
                && additionalProperties.ValueKind == JsonValueKind.False)
            {
                foreach (var jsonProperty in jsonRoot.EnumerateObject())
                {
                    if (!allowedProperties.Contains(jsonProperty.Name))
                    {
                        errors.Add($"Unexpected property '{jsonProperty.Name}'.");
                    }
                }
            }

            foreach (var schemaProperty in properties.EnumerateObject())
            {
                if (!jsonRoot.TryGetProperty(schemaProperty.Name, out var jsonValue))
                {
                    continue;
                }

                ValidateProperty(schemaProperty.Name, jsonValue, schemaProperty.Value, errors);
            }
        }

        if (errors.Count > 0)
        {
            throw new AssertFailedException(
                "JSON schema validation failed:" + Environment.NewLine + string.Join(Environment.NewLine, errors));
        }
    }

    private static void ValidateProperty(string propertyName, JsonElement jsonValue, JsonElement schema, List<string> errors)
    {
        if (schema.TryGetProperty("const", out var constValue)
            && !JsonElementEqualityComparer.Instance.Equals(jsonValue, constValue))
        {
            errors.Add($"Property '{propertyName}' must equal '{constValue}'.");
        }

        if (schema.TryGetProperty("enum", out var enumValues))
        {
            var matches = enumValues.EnumerateArray()
                .Any(v => JsonElementEqualityComparer.Instance.Equals(jsonValue, v));

            if (!matches)
            {
                errors.Add($"Property '{propertyName}' is not in the allowed enum set.");
            }
        }

        if (schema.TryGetProperty("pattern", out var patternValue)
            && jsonValue.ValueKind == JsonValueKind.String)
        {
            var text = jsonValue.GetString() ?? string.Empty;
            var pattern = patternValue.GetString() ?? string.Empty;

            if (!Regex.IsMatch(text, pattern))
            {
                errors.Add($"Property '{propertyName}' does not match pattern '{pattern}'.");
            }
        }

        if (schema.TryGetProperty("format", out var formatValue)
            && jsonValue.ValueKind == JsonValueKind.String)
        {
            var format = formatValue.GetString();
            var text = jsonValue.GetString();

            if (string.Equals(format, "date-time", StringComparison.OrdinalIgnoreCase)
                && !DateTimeOffset.TryParse(text, out _))
            {
                errors.Add($"Property '{propertyName}' is not a valid date-time value.");
            }
        }

        if (schema.TryGetProperty("contentEncoding", out var contentEncoding)
            && jsonValue.ValueKind == JsonValueKind.String)
        {
            var encoding = contentEncoding.GetString();
            var text = jsonValue.GetString() ?? string.Empty;

            if (string.Equals(encoding, "base64", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    _ = Convert.FromBase64String(text);
                }
                catch (FormatException)
                {
                    errors.Add($"Property '{propertyName}' is not valid base64.");
                }
            }
        }
    }

    private static string ResolveSchemaPath(string schemaPath)
    {
        if (Path.IsPathRooted(schemaPath))
        {
            return schemaPath;
        }

        var baseDirCandidate = Path.Combine(AppContext.BaseDirectory, schemaPath);
        if (File.Exists(baseDirCandidate))
        {
            return baseDirCandidate;
        }

        return Path.GetFullPath(schemaPath, Directory.GetCurrentDirectory());
    }

    private sealed class JsonElementEqualityComparer : IEqualityComparer<JsonElement>
    {
        public static JsonElementEqualityComparer Instance { get; } = new();

        public bool Equals(JsonElement x, JsonElement y) => x.ToString() == y.ToString();

        public int GetHashCode(JsonElement obj) => obj.ToString().GetHashCode(StringComparison.Ordinal);
    }
}
