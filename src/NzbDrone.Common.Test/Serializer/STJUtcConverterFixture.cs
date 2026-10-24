using System;
using System.Text.Json;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Serializer;

namespace NzbDrone.Common.Test.Serializer
{
    [TestFixture]
    public class STJUtcConverterFixture
    {
        private readonly JsonSerializerOptions _options = new JsonSerializerOptions
        {
            Converters = { new STJUtcConverter() }
        };

        private class DateHolder
        {
            public DateTime? Value { get; set; }
        }

        [Test]
        public void should_parse_valid_utc_dates()
        {
            var result = JsonSerializer.Deserialize<DateHolder>(
                "{\"Value\":\"2024-01-02T03:04:05Z\"}",
                _options);

            result.Value.Should().Be(new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        }

        // Shared serialization stays strict. Tolerant parsing lives in the BookInfo serializer.
        [Test]
        public void should_reject_invalid_date_strings()
        {
            Action act = () => JsonSerializer.Deserialize<DateHolder>(
                "{\"Value\":\"+020101-01-01T00:00:00.000Z\"}",
                _options);

            act.Should().Throw<FormatException>();
        }
    }
}
