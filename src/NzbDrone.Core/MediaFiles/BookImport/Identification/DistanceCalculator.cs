using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Calibre;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.BookImport.Identification
{
    public static class DistanceCalculator
    {
        private static readonly Logger Logger = NzbDroneLogger.GetLogger(typeof(DistanceCalculator));

        public static readonly List<string> VariousAuthorIds = new List<string> { "89ad4ac3-39f7-470e-963a-56509c546377" };

        private static readonly RegexReplace StripSeriesRegex = new RegexReplace(@"\([^\)].+?\)$", string.Empty, RegexOptions.Compiled);

        private static readonly RegexReplace CleanTitleCruft = new RegexReplace(@"\((?:unabridged)\)|,?\s*(?:\([^)]*edition[^)]*\)|(?:first|second|third|fourth|fifth|sixth|seventh|eighth|ninth|tenth|\d+(?:st|nd|rd|th)?)\s+edition)$", string.Empty, RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly RegexReplace LeadingPartNumber = new RegexReplace(@"^\s*(?:(?:book|part|pt|chapter|disc|cd|track)\s*)?(?:\d+|[ivxlcdm]+)\s*[-._:)]*\s+", string.Empty, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex SeriesPartRegex = new Regex(@"\b(?:book|part|pt)\s*(?<number>\d+(?:\.\d+)?|[ivxlcdm]+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex LeadingPartRegex = new Regex(@"^\s*(?:(?:book|part|pt|chapter|disc|cd|track)\s*)?(?<number>\d+(?:\.\d+)?|[ivxlcdm]+)\s*[-._:)]*\s+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly List<string> EbookFormats = new List<string> { "Kindle Edition", "Nook", "ebook" };

        private static readonly List<string> AudiobookFormats = new List<string> { "Audiobook", "Audio CD", "Audio Cassette", "Audible Audio", "CD-ROM", "MP3 CD" };

        public static Distance BookDistance(List<LocalBook> localTracks, Edition edition)
        {
            var dist = new Distance();

            var authors = GetAuthorCandidates(localTracks);

            dist.AddString("author", authors, edition.Book.Value.AuthorMetadata.Value.Name);
            Logger.Trace("author: '{0}' vs '{1}'; {2}", authors.ConcatToString("' or '"), edition.Book.Value.AuthorMetadata.Value.Name, dist.NormalizedDistance());

            var titleOptions = new List<string> { edition.Title };
            if (titleOptions[0].Contains('#', StringComparison.OrdinalIgnoreCase))
            {
                titleOptions.Add(StripSeriesRegex.Replace(titleOptions[0]));
            }

            var seriesNames = new List<string>();
            if (edition.Book.Value.SeriesLinks?.Value?.Any() ?? false)
            {
                foreach (var l in edition.Book.Value.SeriesLinks.Value)
                {
                    if (l.Series?.Value?.Title?.IsNotNullOrWhiteSpace() ?? false)
                    {
                        seriesNames.Add(l.Series.Value.Title);
                        titleOptions.Add($"{l.Series.Value.Title} {l.Position} {edition.Title}");
                        titleOptions.Add($"{l.Series.Value.Title} Book {l.Position} {edition.Title}");
                        titleOptions.Add($"{edition.Title} {l.Series.Value.Title} {l.Position}");
                        titleOptions.Add($"{edition.Title} {l.Series.Value.Title} Book {l.Position}");
                        AddSeriesPartTitleOptions(titleOptions, l.Series.Value.Title, l.Position);
                    }
                }
            }

            // Use series-aware title splitting
            var (maintitle, _) = SeriesAwareSplitBookTitle(edition.Title, edition.Book.Value.AuthorMetadata.Value.Name, seriesNames);
            if (!titleOptions.Contains(maintitle))
            {
                titleOptions.Add(maintitle);
            }

            var fileTitles = GetTitleCandidates(localTracks);

            dist.AddString("book", fileTitles, titleOptions);
            Logger.Trace("book: '{0}' vs '{1}'; {2}", fileTitles.ConcatToString("' or '"), titleOptions.ConcatToString("' or '"), dist.NormalizedDistance());

            var localPartNumbers = ParsePartNumbers(fileTitles);
            var editionPartNumbers = GetEditionPartNumbers(edition);
            if (localPartNumbers.Any() && editionPartNumbers.Any())
            {
                dist.AddBool("series_part", !HasMatchingPartNumber(localPartNumbers, editionPartNumbers));
                Logger.Trace("series_part: '{0}' vs '{1}'; {2}",
                    localPartNumbers.Select(x => x.ToString("0.###", CultureInfo.InvariantCulture)).ConcatToString("' or '"),
                    editionPartNumbers.Select(x => x.ToString("0.###", CultureInfo.InvariantCulture)).ConcatToString("' or '"),
                    dist.NormalizedDistance());
            }

            var isbn = localTracks.MostCommon(x => x.FileTrackInfo.Isbn);
            if (isbn.IsNotNullOrWhiteSpace() && edition.Isbn13.IsNotNullOrWhiteSpace())
            {
                dist.AddBool("isbn", isbn != edition.Isbn13);
                Logger.Trace("isbn: '{0}' vs '{1}'; {2}", isbn, edition.Isbn13, dist.NormalizedDistance());
            }
            else if (isbn.IsNullOrWhiteSpace() != edition.Isbn13.IsNullOrWhiteSpace())
            {
                dist.AddBool("isbn_missing", true);
                if (edition.Isbn13.IsNullOrWhiteSpace())
                {
                    dist.AddBool("edition_isbn_missing", true);
                }

                Logger.Trace("isbn: '{0}' vs '{1}'; {2}", isbn, edition.Isbn13, dist.NormalizedDistance());
            }

            var asin = localTracks.MostCommon(x => x.FileTrackInfo.Asin);
            if (asin.IsNotNullOrWhiteSpace() && edition.Asin.IsNotNullOrWhiteSpace())
            {
                dist.AddBool("asin", asin != edition.Asin);
                Logger.Trace("asin: '{0}' vs '{1}'; {2}", asin, edition.Asin, dist.NormalizedDistance());
            }
            else if (asin.IsNullOrWhiteSpace() != edition.Asin.IsNullOrWhiteSpace())
            {
                dist.AddBool("asin_missing", true);
                if (edition.Asin.IsNullOrWhiteSpace())
                {
                    dist.AddBool("edition_asin_missing", true);
                }

                Logger.Trace("asin: '{0}' vs '{1}'; {2}", asin, edition.Asin, dist.NormalizedDistance());
            }

            // Year
            var localYear = localTracks.MostCommon(x => x.FileTrackInfo.Year);
            if (localYear > 0 && edition.ReleaseDate.HasValue)
            {
                var bookYear = edition.ReleaseDate?.Year ?? 0;
                if (localYear == bookYear)
                {
                    dist.Add("year", 0.0);
                }
                else
                {
                    var remoteYear = bookYear;
                    var diff = Math.Abs(localYear - remoteYear);
                    var diff_max = Math.Abs(DateTime.Now.Year - remoteYear);
                    dist.AddRatio("year", diff, diff_max);
                }

                Logger.Trace($"year: {localYear} vs {edition.ReleaseDate?.Year}; {dist.NormalizedDistance()}");
            }

            // Language - only if set for both the local book and remote edition
            var localLanguage = localTracks.MostCommon(x => x.FileTrackInfo.Language).CanonicalizeLanguage();
            var editionLanguage = edition.Language.CanonicalizeLanguage();
            if (localLanguage.IsNotNullOrWhiteSpace() && editionLanguage.IsNotNullOrWhiteSpace())
            {
                dist.AddBool("language", localLanguage != editionLanguage);
                Logger.Trace($"language: {localLanguage} vs {editionLanguage}; {dist.NormalizedDistance()}");
            }

            // Publisher - only if set for both the local book and remote edition
            var localPublisher = localTracks.MostCommon(x => x.FileTrackInfo.Publisher);
            var editionPublisher = edition.Publisher;
            if (localPublisher.IsNotNullOrWhiteSpace() && editionPublisher.IsNotNullOrWhiteSpace())
            {
                dist.AddString("publisher", localPublisher, editionPublisher);
                Logger.Trace($"publisher: {localPublisher} vs {editionPublisher}; {dist.NormalizedDistance()}");
            }

            // try to tilt it towards the correct "type" of release
            var isAudio = MediaFileExtensions.AudioExtensions.Contains(localTracks.First().Path.GetPathExtension());

            if (edition.Format.IsNotNullOrWhiteSpace())
            {
                if (!isAudio)
                {
                    // text books should prefer ebook formats
                    dist.AddBool("ebook_format", !EbookFormats.Contains(edition.Format));
                    Logger.Trace($"ebook_format: {edition.Format} - {!EbookFormats.Contains(edition.Format)}; {dist.NormalizedDistance()}");

                    // text books should not match audio entries
                    dist.AddBool("wrong_format", AudiobookFormats.Contains(edition.Format));
                    Logger.Trace($"wrong_format: {edition.Format} - {AudiobookFormats.Contains(edition.Format)}; {dist.NormalizedDistance()}");
                }
                else
                {
                    // audio books should prefer audio formats
                    dist.AddBool("audio_format", !AudiobookFormats.Contains(edition.Format));
                    Logger.Trace($"audio_format: {edition.Format} - {!AudiobookFormats.Contains(edition.Format)}; {dist.NormalizedDistance()}");

                    // audio books should not match ebook entries
                    dist.AddBool("wrong_format", EbookFormats.Contains(edition.Format));
                    Logger.Trace($"wrong_format: {edition.Format} - {EbookFormats.Contains(edition.Format)}; {dist.NormalizedDistance()}");
                }
            }

            return dist;
        }

        private static List<string> GetAuthorCandidates(List<LocalBook> localTracks)
        {
            var authors = new List<string>();

            // the most common list of authors reported by a file
            var fileAuthors = localTracks.Select(x => x.FileTrackInfo.Authors?.Where(a => a.IsNotNullOrWhiteSpace()).ToList() ?? new List<string>())
                .GroupBy(x => x.ConcatToString())
                .OrderByDescending(x => x.Count())
                .First()
                .First();

            authors.AddRange(fileAuthors.Where(a => a.IsNotNullOrWhiteSpace()));
            authors.AddRange(localTracks.Select(x => x.FolderTrackInfo?.AuthorName).Where(x => x.IsNotNullOrWhiteSpace()));
            authors.AddRange(localTracks.Select(x => x.DownloadClientBookInfo?.AuthorName).Where(x => x.IsNotNullOrWhiteSpace()));
            authors.AddRange(localTracks
                .Select(x => Parser.Parser.ParseBookTitle(Path.GetFileNameWithoutExtension(x.Path))?.AuthorName)
                .Where(x => x.IsNotNullOrWhiteSpace()));
            authors.AddRange(localTracks
                .Select(x => Parser.Parser.ParseBookTitle(Path.GetFileName(Path.GetDirectoryName(x.Path) ?? string.Empty))?.AuthorName)
                .Where(x => x.IsNotNullOrWhiteSpace()));

            return GetAuthorVariants(authors
                    .Distinct(StringComparer.InvariantCultureIgnoreCase)
                    .ToList())
                .Where(x => x.IsNotNullOrWhiteSpace())
                .Distinct(StringComparer.InvariantCultureIgnoreCase)
                .ToList();
        }

        private static List<string> GetTitleCandidates(List<LocalBook> localTracks)
        {
            var rawTitles = new List<string>
            {
                localTracks.MostCommon(x => x.FileTrackInfo.Title),
                localTracks.MostCommon(x => x.FileTrackInfo.CleanTitle),
                localTracks.MostCommon(x => x.FileTrackInfo.BookTitle),
                localTracks.MostCommon(x => x.FolderTrackInfo?.BookTitle),
                localTracks.MostCommon(x => x.DownloadClientBookInfo?.BookTitle),
                localTracks.MostCommon(x => Path.GetFileNameWithoutExtension(x.Path)),
                localTracks.MostCommon(x => Path.GetFileName(Path.GetDirectoryName(x.Path) ?? string.Empty))
            };

            return rawTitles
                .Where(x => x.IsNotNullOrWhiteSpace())
                .SelectMany(ExpandTitleCandidates)
                .Where(x => x.IsNotNullOrWhiteSpace())
                .Distinct(StringComparer.InvariantCultureIgnoreCase)
                .ToList();
        }

        private static IEnumerable<string> ExpandTitleCandidates(string title)
        {
            if (title.IsNullOrWhiteSpace())
            {
                return Array.Empty<string>();
            }

            var candidates = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);

            void Add(string value)
            {
                if (value.IsNotNullOrWhiteSpace())
                {
                    var trimmed = value.Trim(' ', '-', '_', '.', ',', ':', ';');
                    if (trimmed.IsNotNullOrWhiteSpace())
                    {
                        candidates.Add(trimmed);
                    }
                }
            }

            Add(title);

            var parsed = Parser.Parser.ParseBookTitle(title);
            if (parsed?.BookTitle.IsNotNullOrWhiteSpace() ?? false)
            {
                Add(parsed.BookTitle);
            }

            var cleanedTitle = CleanTitleCruft.Replace(title);
            Add(cleanedTitle);
            Add(LeadingPartNumber.Replace(title));
            Add(LeadingPartNumber.Replace(cleanedTitle));
            Add(title.RemoveBracketsAndContents());
            Add(title.RemoveAfterDash());

            if (title.Contains(" - ", StringComparison.Ordinal))
            {
                var splitIndex = title.LastIndexOf(" - ", StringComparison.Ordinal);
                Add(title.Substring(0, splitIndex));
                Add(title.Substring(splitIndex + 3));
            }

            return candidates;
        }

        private static void AddSeriesPartTitleOptions(List<string> titleOptions, string seriesName, string position)
        {
            if (seriesName.IsNullOrWhiteSpace() || position.IsNullOrWhiteSpace())
            {
                return;
            }

            AddTitleOption(titleOptions, $"{seriesName} Part {position}");

            if (TryParseSeriesNumber(position, out var numericPosition))
            {
                AddTitleOption(titleOptions, $"{seriesName} Part {numericPosition.ToString("0.###", CultureInfo.InvariantCulture)}");

                if (Math.Abs(numericPosition - Math.Round(numericPosition)) < 0.001)
                {
                    var rounded = (int)Math.Round(numericPosition);
                    AddTitleOption(titleOptions, $"{seriesName} Part {ToRoman(rounded)}");
                }
            }
        }

        private static void AddTitleOption(List<string> titleOptions, string option)
        {
            if (option.IsNullOrWhiteSpace())
            {
                return;
            }

            if (!titleOptions.Any(x => x.Equals(option, StringComparison.InvariantCultureIgnoreCase)))
            {
                titleOptions.Add(option);
            }
        }

        private static bool TryParseSeriesNumber(string value, out double number)
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

        private static List<double> ParsePartNumbers(IEnumerable<string> titles)
        {
            var partNumbers = new List<double>();

            foreach (var title in titles.Where(x => x.IsNotNullOrWhiteSpace()))
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

        private static List<double> GetEditionPartNumbers(Edition edition)
        {
            var partNumbers = new List<double>();

            if (!(edition.Book?.Value?.SeriesLinks?.Value?.Any() ?? false))
            {
                return partNumbers;
            }

            foreach (var seriesLink in edition.Book.Value.SeriesLinks.Value)
            {
                if (seriesLink.SeriesPosition > 0)
                {
                    AddPartIfUnique(partNumbers, seriesLink.SeriesPosition);
                }

                if (TryParseSeriesNumber(seriesLink.Position, out var parsedPosition))
                {
                    AddPartIfUnique(partNumbers, parsedPosition);
                }
            }

            return partNumbers;
        }

        private static bool HasMatchingPartNumber(List<double> localPartNumbers, List<double> editionPartNumbers)
        {
            foreach (var localPartNumber in localPartNumbers)
            {
                if (editionPartNumbers.Any(x => Math.Abs(x - localPartNumber) < 0.01))
                {
                    return true;
                }
            }

            return false;
        }

        private static void AddPartIfUnique(List<double> partNumbers, double value)
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

        private static string ToRoman(int value)
        {
            if (value <= 0)
            {
                return string.Empty;
            }

            var numerals = new (int Value, string Symbol)[]
            {
                (1000, "M"),
                (900, "CM"),
                (500, "D"),
                (400, "CD"),
                (100, "C"),
                (90, "XC"),
                (50, "L"),
                (40, "XL"),
                (10, "X"),
                (9, "IX"),
                (5, "V"),
                (4, "IV"),
                (1, "I")
            };

            var output = new StringBuilder();
            var remaining = value;

            foreach (var numeral in numerals)
            {
                while (remaining >= numeral.Value)
                {
                    output.Append(numeral.Symbol);
                    remaining -= numeral.Value;
                }
            }

            return output.ToString();
        }

        public static List<string> GetAuthorVariants(List<string> fileAuthors)
        {
            var authors = new List<string>(fileAuthors);

            foreach (var author in fileAuthors)
            {
                authors.AddRange(SplitAuthor(author));
            }

            foreach (var author in fileAuthors)
            {
                if (author.Contains(','))
                {
                    var split = author.Split(',', 2).Select(x => x.Trim());
                    if (!split.First().Contains(' '))
                    {
                        authors.Add(split.Reverse().ConcatToString(" "));
                    }
                }
            }

            return authors;
        }

        private static List<string> SplitAuthor(string input)
        {
            var seps = new[] { ';', '/' };
            foreach (var sep in seps)
            {
                if (input.Contains(sep))
                {
                    return input.Split(sep).Select(x => x.Trim()).ToList();
                }
            }

            var andSeps = new List<string> { " and ", " & " };
            foreach (var sep in andSeps)
            {
                if (input.Contains(sep))
                {
                    var result = new List<string>();
                    foreach (var s in input.Split(sep).Select(x => x.Trim()))
                    {
                        var s2 = SplitAuthor(s);
                        if (s2.Any())
                        {
                            result.AddRange(s2);
                        }
                        else
                        {
                            result.Add(s);
                        }
                    }

                    return result;
                }
            }

            if (input.Contains(','))
            {
                var split = input.Split(',').Select(x => x.Trim()).ToList();
                if (split[0].Contains(' '))
                {
                    return split;
                }
            }

            return new List<string>();
        }

        /// <summary>
        /// Series-aware version of SplitBookTitle that can identify series names and better split titles
        /// </summary>
        private static (string, string) SeriesAwareSplitBookTitle(string book, string author, List<string> seriesNames)
        {
            // First try the standard split
            var (standardMain, standardSub) = book.SplitBookTitle(author);

            // If we don't have series names, return the standard split
            if (!seriesNames.Any())
            {
                return (standardMain, standardSub);
            }

            // Check if the title starts with any known series name
            foreach (var seriesName in seriesNames)
            {
                // Check for "Series Name: Book Title" pattern
                if (book.StartsWith($"{seriesName}:", StringComparison.OrdinalIgnoreCase))
                {
                    var bookTitle = book.Substring(seriesName.Length + 1).Trim();
                    return (bookTitle, seriesName);
                }

                // Check for "Series Name Book Title" pattern (no colon)
                if (book.StartsWith($"{seriesName} ", StringComparison.OrdinalIgnoreCase))
                {
                    var bookTitle = book.Substring(seriesName.Length).Trim();

                    // Make sure there's actually content after the series name
                    if (bookTitle.Length > 0)
                    {
                        return (bookTitle, seriesName);
                    }
                }
            }

            // If no series match found, return the standard split
            return (standardMain, standardSub);
        }
    }
}
