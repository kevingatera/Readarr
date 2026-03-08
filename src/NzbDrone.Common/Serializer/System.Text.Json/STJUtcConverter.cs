using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NzbDrone.Common.Serializer
{
    public class STJUtcConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
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

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToUniversalTime().ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ssZ"));
        }
    }
}
