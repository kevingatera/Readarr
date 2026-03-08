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
                "{\"value\":\"2024-01-02T03:04:05Z\"}",
                _options);

            result.Value.Should().Be(new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        }

        [Test]
        public void should_return_default_date_for_invalid_strings()
        {
            var result = JsonSerializer.Deserialize<DateHolder>(
                "{\"value\":\"+020101-01-01T00:00:00.000Z\"}",
                _options);

            result.Value.Should().Be(DateTime.MinValue);
        }
    }
}
