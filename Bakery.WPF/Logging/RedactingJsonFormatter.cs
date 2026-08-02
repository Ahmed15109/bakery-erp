using System.IO;
using Bakery.Shared.Security;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Json;
using Serilog.Parsing;

namespace Bakery.WPF.Logging;


public sealed class RedactingJsonFormatter : ITextFormatter
{
    private readonly JsonFormatter _inner = new(renderMessage: true);
    private readonly MessageTemplateParser _messageTemplateParser = new();

    public void Format(LogEvent logEvent, TextWriter output)
    {
        var properties = logEvent.Properties.Select(property => new LogEventProperty(
            property.Key,
            SanitizeValue(property.Value, SensitiveDataRedactor.IsSensitiveName(property.Key))));
        var messageTemplate = _messageTemplateParser.Parse(
            SensitiveDataRedactor.Redact(logEvent.MessageTemplate.Text));
        var exception = logEvent.Exception is null
            ? null
            : new RedactedException(SensitiveDataRedactor.Redact(logEvent.Exception.ToString()));
        var sanitized = new LogEvent(
            logEvent.Timestamp,
            logEvent.Level,
            exception,
            messageTemplate,
            properties);
        _inner.Format(sanitized, output);
    }

    private static LogEventPropertyValue SanitizeValue(LogEventPropertyValue value, bool redactWholeValue)
    {
        if (redactWholeValue) return new ScalarValue(SensitiveDataRedactor.Replacement);
        return value switch
        {
            ScalarValue { Value: string text } => new ScalarValue(SensitiveDataRedactor.Redact(text)),
            SequenceValue sequence => new SequenceValue(sequence.Elements.Select(item => SanitizeValue(item, false))),
            StructureValue structure => new StructureValue(
                structure.Properties.Select(property => new LogEventProperty(
                    property.Name,
                    SanitizeValue(property.Value, SensitiveDataRedactor.IsSensitiveName(property.Name)))),
                structure.TypeTag),
            DictionaryValue dictionary => new DictionaryValue(dictionary.Elements.Select(pair =>
                new KeyValuePair<ScalarValue, LogEventPropertyValue>(
                    pair.Key,
                    SanitizeValue(
                        pair.Value,
                        pair.Key.Value is string key && SensitiveDataRedactor.IsSensitiveName(key))))),
            _ => value
        };
    }

    private sealed class RedactedException : Exception
    {
        private readonly string _redactedText;

        public RedactedException(string redactedText) => _redactedText = redactedText;

        public override string ToString() => _redactedText;
    }
}
