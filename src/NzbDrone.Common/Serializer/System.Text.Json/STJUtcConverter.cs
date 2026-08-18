using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NzbDrone.Common.Serializer
{
    public class STJUtcConverter : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert)
        {
            return typeToConvert == typeof(DateTime) || typeToConvert == typeof(DateTime?);
        }

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        {
            return typeToConvert == typeof(DateTime?) ? new NullableDateTimeConverter() : new DateTimeConverter();
        }

        private static DateTime ReadDate(ref Utf8JsonReader reader)
        {
            var value = reader.GetString();

            if (string.IsNullOrWhiteSpace(value))
            {
                return default;
            }

            if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedOffset))
            {
                return parsedOffset.UtcDateTime;
            }

            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedDateTime))
            {
                return parsedDateTime.ToUniversalTime();
            }

            return default;
        }

        private static void WriteDate(Utf8JsonWriter writer, DateTime value)
        {
            writer.WriteStringValue(value.ToUniversalTime().ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ssZ"));
        }

        private sealed class DateTimeConverter : JsonConverter<DateTime>
        {
            public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => ReadDate(ref reader);
            public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) => WriteDate(writer, value);
        }

        private sealed class NullableDateTimeConverter : JsonConverter<DateTime?>
        {
            public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                return reader.TokenType == JsonTokenType.Null ? null : ReadDate(ref reader);
            }

            public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
            {
                if (value.HasValue)
                {
                    WriteDate(writer, value.Value);
                }
                else
                {
                    writer.WriteNullValue();
                }
            }
        }
    }
}
