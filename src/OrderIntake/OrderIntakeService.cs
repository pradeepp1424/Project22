using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OrderIntake;

/// <summary>
/// Parses and validates a single laboratory order supplied as a JSON string.
/// Stateless and side-effect free: no console, file, network, or database
/// access, and it never throws for bad input - every failure path returns
/// a Rejected OrderResult instead.
/// </summary>
public sealed class OrderIntakeService
{
    private const int MaxIdLength = 20;

    private static readonly IReadOnlyDictionary<string, string> AllowedSpecimenTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Blood"] = "Blood",
            ["Urine"] = "Urine",
            ["Tissue"] = "Tissue",
            ["Saliva"] = "Saliva",
        };

    private static readonly IReadOnlyDictionary<string, string> AllowedPriorities =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Routine"] = "Routine",
            ["Urgent"] = "Urgent",
        };

    private static readonly Regex StrictDatePattern = new(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.Compiled);

    /// <summary>
    /// Validates a single laboratory order JSON string and returns the result.
    /// Never throws for malformed or invalid input.
    /// </summary>
    public OrderResult Process(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return OrderResult.Malformed("Input was null, empty, or whitespace.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return OrderResult.Malformed("Input is not valid JSON.");
        }

        using (document)
        {
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return OrderResult.Malformed("Top-level JSON value must be an object.");
            }

            // Read every recognized field, checking JSON-type compatibility as we go.
            // A type mismatch (e.g. a number where a string is expected) is treated
            // as malformed input for the whole message, per spec - it is not a
            // per-field validation error.
            if (!TryGetString(root, "orderId", out string? orderId))
                return OrderResult.Malformed("Field 'orderId' has an incompatible JSON type.");

            if (!TryGetString(root, "patientId", out string? patientId))
                return OrderResult.Malformed("Field 'patientId' has an incompatible JSON type.");

            if (!TryGetString(root, "specimenId", out string? specimenId))
                return OrderResult.Malformed("Field 'specimenId' has an incompatible JSON type.");

            if (!TryGetString(root, "specimenType", out string? specimenType))
                return OrderResult.Malformed("Field 'specimenType' has an incompatible JSON type.");

            if (!TryGetString(root, "priority", out string? priority))
                return OrderResult.Malformed("Field 'priority' has an incompatible JSON type.");

            if (!TryGetString(root, "collectionDate", out string? collectionDateRaw))
                return OrderResult.Malformed("Field 'collectionDate' has an incompatible JSON type.");

            if (!TryGetStringArray(root, "requestedTests", out List<string>? requestedTestsRaw))
                return OrderResult.Malformed("Field 'requestedTests' has an incompatible JSON type.");

            // Unknown fields (e.g. senderNote) are simply never read above, so
            // they are ignored automatically.

            var errors = new List<ValidationError>();

            ValidateRequiredMaxLength(orderId, "orderId", errors);
            ValidateRequiredMaxLength(patientId, "patientId", errors);
            ValidateRequiredMaxLength(specimenId, "specimenId", errors);

            string? normalizedSpecimenType = ValidateEnumField(specimenType, "specimenType", AllowedSpecimenTypes, errors);
            string? normalizedPriority = ValidateEnumField(priority, "priority", AllowedPriorities, errors);
            DateOnly? collectionDate = ValidateCollectionDate(collectionDateRaw, errors);
            List<string>? requestedTests = ValidateRequestedTests(requestedTestsRaw, errors);

            if (errors.Count > 0)
            {
                return OrderResult.Rejected(errors);
            }

            var order = new LabOrder
            {
                OrderId = orderId!,
                PatientId = patientId!,
                SpecimenId = specimenId!,
                SpecimenType = normalizedSpecimenType!,
                Priority = normalizedPriority!,
                CollectionDate = collectionDate!.Value,
                RequestedTests = requestedTests!,
            };

            return OrderResult.Accepted(order);
        }
    }

    // ---- JSON extraction helpers -------------------------------------------------

    /// <summary>
    /// Reads a recognized string field. Missing property or explicit JSON null
    /// both yield value = null (a REQUIRED error is raised later at field-validation
    /// time). Returns false only when the property is present with an incompatible
    /// JSON type (number, bool, object, array).
    /// </summary>
    private static bool TryGetString(JsonElement root, string propertyName, out string? value)
    {
        if (root.TryGetProperty(propertyName, out JsonElement prop))
        {
            switch (prop.ValueKind)
            {
                case JsonValueKind.Null:
                    value = null;
                    return true;
                case JsonValueKind.String:
                    value = prop.GetString();
                    return true;
                default:
                    value = null;
                    return false;
            }
        }

        value = null;
        return true;
    }

    /// <summary>
    /// Reads the requestedTests field. Missing/null yields values = null.
    /// Returns false if the property is present but is not an array, or
    /// contains any element that is not a JSON string.
    /// </summary>
    private static bool TryGetStringArray(JsonElement root, string propertyName, out List<string>? values)
    {
        if (root.TryGetProperty(propertyName, out JsonElement prop))
        {
            if (prop.ValueKind == JsonValueKind.Null)
            {
                values = null;
                return true;
            }

            if (prop.ValueKind == JsonValueKind.Array)
            {
                var list = new List<string>();
                foreach (JsonElement item in prop.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String)
                    {
                        values = null;
                        return false;
                    }

                    list.Add(item.GetString() ?? string.Empty);
                }

                values = list;
                return true;
            }

            values = null;
            return false;
        }

        values = null;
        return true;
    }

    // ---- Field validators ----------------------------------------------------

    private static void ValidateRequiredMaxLength(string? value, string field, List<ValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(new ValidationError(field, "REQUIRED", $"'{field}' is required."));
            return;
        }

        if (value.Length > MaxIdLength)
        {
            errors.Add(new ValidationError(field, "MAX_LENGTH", $"'{field}' must be at most {MaxIdLength} characters."));
        }
    }

    private static string? ValidateEnumField(
        string? value,
        string field,
        IReadOnlyDictionary<string, string> allowedValues,
        List<ValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(new ValidationError(field, "REQUIRED", $"'{field}' is required."));
            return null;
        }

        if (allowedValues.TryGetValue(value, out string? normalized))
        {
            return normalized;
        }

        errors.Add(new ValidationError(field, "INVALID_VALUE", $"'{field}' must be one of: {string.Join(", ", Distinct(allowedValues.Values))}."));
        return null;
    }

    private static DateOnly? ValidateCollectionDate(string? value, List<ValidationError> errors)
    {
        const string field = "collectionDate";

        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(new ValidationError(field, "REQUIRED", $"'{field}' is required."));
            return null;
        }

        // Regex first so "2026-9-2" (missing leading zeros) is rejected even though
        // DateOnly.TryParseExact can be lenient about digit counts on some platforms.
        if (!StrictDatePattern.IsMatch(value) ||
            !DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly parsed))
        {
            errors.Add(new ValidationError(field, "INVALID_FORMAT", $"'{field}' must be a real calendar date in yyyy-MM-dd format."));
            return null;
        }

        if (parsed > DateOnly.FromDateTime(DateTime.Today))
        {
            errors.Add(new ValidationError(field, "FUTURE_DATE", $"'{field}' cannot be after today."));
            return null;
        }

        return parsed;
    }

    private static List<string>? ValidateRequestedTests(List<string>? values, List<ValidationError> errors)
    {
        const string field = "requestedTests";

        if (values is null || values.Count == 0)
        {
            errors.Add(new ValidationError(field, "REQUIRED", $"'{field}' must contain at least one test."));
            return null;
        }

        bool hasEmptyItem = values.Any(string.IsNullOrWhiteSpace);
        if (hasEmptyItem)
        {
            errors.Add(new ValidationError(field, "INVALID_VALUE", $"'{field}' must not contain empty items."));
        }

        bool hasDuplicate = false;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string item in values)
        {
            if (string.IsNullOrWhiteSpace(item))
            {
                continue;
            }

            if (!seen.Add(item))
            {
                hasDuplicate = true;
            }
        }

        if (hasDuplicate)
        {
            errors.Add(new ValidationError(field, "DUPLICATE", $"'{field}' must not contain duplicate test names (case-insensitive)."));
        }

        return hasEmptyItem || hasDuplicate ? null : values;
    }

    private static IEnumerable<string> Distinct(IEnumerable<string> values) => new HashSet<string>(values);
}
