using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NzbDrone.Core.MetadataSource.BookInfo
{
    // Malformed BookInfo dates become default(DateTime), which MapBook and MapEdition
    // normalize to null. Used only by the BookInfo serializer; the shared UTC converter stays strict.
    public class BookInfoDateConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                reader.Skip();
                return default;
            }

            if (DateTime.TryParse(reader.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            {
                return parsed.ToUniversalTime();
            }

            return default;
        }

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToUniversalTime());
        }
    }
}
