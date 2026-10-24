using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.MediaFiles.BookImport.Identification
{
    // Reads series positions such as "Book 3", "Part IV" or a leading "2 - " from release and file titles.
    // Shared by candidate search and distance scoring so both read positions the same way.
    public static class SeriesPartParser
    {
        private static readonly Regex SeriesPartRegex = new Regex(@"\b(?:book|part|pt)\s*(?<number>\d+(?:\.\d+)?|[ivxlcdm]+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex LeadingPartRegex = new Regex(@"^\s*(?:(?:book|part|pt|chapter|disc|cd|track)\s*)?(?<number>\d+(?:\.\d+)?|[ivxlcdm]+)\s*[-._:)]*\s+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RemoveSeriesPartRegex = new Regex(@"\b(?:book|part|pt)\s*(?:\d+(?:\.\d+)?|[ivxlcdm]+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static string StripLeadingPart(string title)
        {
            return LeadingPartRegex.Replace(title, string.Empty);
        }

        public static string RemoveSeriesPart(string title)
        {
            return RemoveSeriesPartRegex.Replace(title, " ");
        }

        public static bool TryParseSeriesNumber(string value, out double number)
        {
            number = 0;

            if (value.IsNullOrWhiteSpace())
            {
                return false;
            }

            if (double.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out number))
            {
                return true;
            }

            var roman = RomanToInt(value);
            if (roman > 0)
            {
                number = roman;
                return true;
            }

            return false;
        }

        public static List<double> ParsePartNumbers(IEnumerable<string> titles)
        {
            var partNumbers = new List<double>();

            foreach (var title in (titles ?? Enumerable.Empty<string>()).Where(x => x.IsNotNullOrWhiteSpace()))
            {
                var leadingMatch = LeadingPartRegex.Match(title);
                if (leadingMatch.Success && TryParseSeriesNumber(leadingMatch.Groups["number"].Value, out var leadingPart))
                {
                    AddPartIfUnique(partNumbers, leadingPart);
                }

                foreach (Match match in SeriesPartRegex.Matches(title))
                {
                    if (TryParseSeriesNumber(match.Groups["number"].Value, out var partNumber))
                    {
                        AddPartIfUnique(partNumbers, partNumber);
                    }
                }
            }

            return partNumbers;
        }

        public static void AddPartIfUnique(List<double> partNumbers, double value)
        {
            if (partNumbers.All(x => Math.Abs(x - value) > 0.01))
            {
                partNumbers.Add(value);
            }
        }

        private static int RomanToInt(string value)
        {
            if (value.IsNullOrWhiteSpace())
            {
                return 0;
            }

            var roman = value.ToUpperInvariant();
            var total = 0;
            var last = 0;

            for (var i = roman.Length - 1; i >= 0; i--)
            {
                var current = roman[i] switch
                {
                    'I' => 1,
                    'V' => 5,
                    'X' => 10,
                    'L' => 50,
                    'C' => 100,
                    'D' => 500,
                    'M' => 1000,
                    _ => 0
                };

                if (current == 0)
                {
                    return 0;
                }

                if (current < last)
                {
                    total -= current;
                }
                else
                {
                    total += current;
                    last = current;
                }
            }

            return total;
        }
    }
}
