using Serilog.Core;
using Serilog.Events;

namespace Harness.DataAccess.Observability;

internal sealed class SensitiveDataRedactionEnricher : ILogEventEnricher
{
    private const string RedactedValue = "[REDACTED]";

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        foreach (LogEventProperty property in logEvent.Properties
                     .Select(pair => new LogEventProperty(pair.Key, pair.Value))
                     .ToArray())
        {
            LogEventPropertyValue redacted = Redact(property.Name, property.Value);
            if (!ReferenceEquals(redacted, property.Value))
            {
                logEvent.AddOrUpdateProperty(new LogEventProperty(property.Name, redacted));
            }
        }
    }

    private static LogEventPropertyValue Redact(string propertyName, LogEventPropertyValue value)
    {
        if (IsSensitive(propertyName))
        {
            return new ScalarValue(RedactedValue);
        }

        return value switch
        {
            StructureValue structure => RedactStructure(structure),
            SequenceValue sequence => RedactSequence(sequence),
            DictionaryValue dictionary => RedactDictionary(dictionary),
            _ => value,
        };
    }

    private static StructureValue RedactStructure(StructureValue structure) => new(
        structure.Properties.Select(property => new LogEventProperty(
            property.Name,
            Redact(property.Name, property.Value))),
        structure.TypeTag);

    private static SequenceValue RedactSequence(SequenceValue sequence) => new(
        sequence.Elements.Select(value => Redact(string.Empty, value)));

    private static DictionaryValue RedactDictionary(DictionaryValue dictionary) => new(
        dictionary.Elements.Select(pair => new KeyValuePair<ScalarValue, LogEventPropertyValue>(
            pair.Key,
            Redact(pair.Key.Value?.ToString() ?? string.Empty, pair.Value))));

    private static bool IsSensitive(string propertyName)
    {
        string normalized = new(
            propertyName
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());

        return normalized.Contains("password", StringComparison.Ordinal) ||
               normalized.Contains("secret", StringComparison.Ordinal) ||
               normalized.Contains("token", StringComparison.Ordinal) ||
               normalized.Contains("apikey", StringComparison.Ordinal) ||
               normalized.Contains("authorization", StringComparison.Ordinal) ||
               normalized.Contains("credential", StringComparison.Ordinal);
    }
}
