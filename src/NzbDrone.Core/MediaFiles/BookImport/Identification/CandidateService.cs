using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.Goodreads;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.BookImport.Identification
{
    public interface ICandidateService
    {
        List<CandidateEdition> GetDbCandidatesFromTags(LocalEdition localEdition, IdentificationOverrides idOverrides, bool includeExisting);
        IEnumerable<CandidateEdition> GetRemoteCandidates(LocalEdition localEdition, IdentificationOverrides idOverrides);
    }

    public class CandidateService : ICandidateService
    {
        private static readonly Regex SeriesPartRegex = new Regex(@"\b(?:book|part|pt)\s*(?<number>\d+(?:\.\d+)?|[ivxlcdm]+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex LeadingPartRegex = new Regex(@"^\s*(?:(?:book|part|pt|chapter|disc|cd|track)\s*)?(?<number>\d+(?:\.\d+)?|[ivxlcdm]+)\s*[-._:)]*\s+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RemoveSeriesPartRegex = new Regex(@"\b(?:book|part|pt)\s*(?:\d+(?:\.\d+)?|[ivxlcdm]+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex CollapseWhitespaceRegex = new Regex(@"\s{2,}", RegexOptions.Compiled);

        private readonly ISearchForNewBook _bookSearchService;
        private readonly IAuthorService _authorService;
        private readonly IBookService _bookService;
        private readonly IEditionService _editionService;
        private readonly IMediaFileService _mediaFileService;
        private readonly Logger _logger;

        public CandidateService(ISearchForNewBook bookSearchService,
                                IAuthorService authorService,
                                IBookService bookService,
                                IEditionService editionService,
                                IMediaFileService mediaFileService,
                                Logger logger)
        {
            _bookSearchService = bookSearchService;
            _authorService = authorService;
            _bookService = bookService;
            _editionService = editionService;
            _mediaFileService = mediaFileService;
            _logger = logger;
        }

        public List<CandidateEdition> GetDbCandidatesFromTags(LocalEdition localEdition, IdentificationOverrides idOverrides, bool includeExisting)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();

            // Generally author, book and release are null.  But if they're not then limit candidates appropriately.
            // We've tried to make sure that tracks are all for a single release.
            List<CandidateEdition> candidateReleases;

            // if we have a Book ID, use that
            Book tagMbidRelease = null;
            List<CandidateEdition> tagCandidate = null;

            // TODO: select by ISBN?
            // var releaseIds = localEdition.LocalTracks.Select(x => x.FileTrackInfo.ReleaseMBId).Distinct().ToList();
            // if (releaseIds.Count == 1 && releaseIds[0].IsNotNullOrWhiteSpace())
            // {
            //     _logger.Debug("Selecting release from consensus ForeignReleaseId [{0}]", releaseIds[0]);
            //     tagMbidRelease = _releaseService.GetReleaseByForeignReleaseId(releaseIds[0], true);

            //     if (tagMbidRelease != null)
            //     {
            //         tagCandidate = GetDbCandidatesByRelease(new List<BookRelease> { tagMbidRelease }, includeExisting);
            //     }
            // }
            if (idOverrides?.Edition != null)
            {
                var release = idOverrides.Edition;
                _logger.Debug("Edition {0} was forced", release);
                candidateReleases = GetDbCandidatesByEdition(new List<Edition> { release }, includeExisting);
            }
            else if (idOverrides?.Book != null)
            {
                // use the release from file tags if it exists and agrees with the specified book
                if (tagMbidRelease?.Id == idOverrides.Book.Id)
                {
                    candidateReleases = tagCandidate;
                }
                else
                {
                    candidateReleases = GetDbCandidatesByBook(idOverrides.Book, includeExisting);
                }
            }
            else if (idOverrides?.Author != null)
            {
                // use the release from file tags if it exists and agrees with the specified book
                if (tagMbidRelease?.AuthorMetadataId == idOverrides.Author.AuthorMetadataId)
                {
                    candidateReleases = tagCandidate;
                }
                else
                {
                    candidateReleases = GetDbCandidatesByAuthor(localEdition, idOverrides.Author, includeExisting);
                }
            }
            else
            {
                if (tagMbidRelease != null)
                {
                    candidateReleases = tagCandidate;
                }
                else
                {
                    candidateReleases = GetDbCandidates(localEdition, includeExisting);
                }
            }

            watch.Stop();
            _logger.Debug($"Getting {candidateReleases.Count} candidates from tags for {localEdition.LocalBooks.Count} tracks took {watch.ElapsedMilliseconds}ms");

            return candidateReleases;
        }

        private List<CandidateEdition> GetDbCandidatesByEdition(List<Edition> editions, bool includeExisting)
        {
            // get the local tracks on disk for each book
            var bookFiles = editions.Select(x => x.BookId)
                .Distinct()
                .ToDictionary(id => id, id => includeExisting ? _mediaFileService.GetFilesByBook(id) : new List<BookFile>());

            return editions.Select(x => new CandidateEdition
            {
                Edition = x,
                ExistingFiles = bookFiles[x.BookId]
            }).ToList();
        }

        private List<CandidateEdition> GetDbCandidatesByBook(Book book, bool includeExisting)
        {
            // Sort by most voted so less likely to swap to a random release
            return GetDbCandidatesByEdition(_editionService.GetEditionsByBook(book.Id)
                                            .OrderByDescending(x => x.Ratings.Popularity)
                                            .ToList(), includeExisting);
        }

        private List<CandidateEdition> GetDbCandidatesByAuthor(LocalEdition localEdition, Author author, bool includeExisting)
        {
            _logger.Trace("Getting candidates for {0}", author);
            var candidateReleases = new List<CandidateEdition>();

            var bookTags = GetBookTags(localEdition);
            foreach (var bookTag in bookTags)
            {
                var possibleBooks = _bookService.GetCandidates(author.AuthorMetadataId, bookTag);
                foreach (var book in possibleBooks)
                {
                    candidateReleases.AddRange(GetDbCandidatesByBook(book, includeExisting));
                }

                var possibleEditions = _editionService.GetCandidates(author.AuthorMetadataId, bookTag);
                candidateReleases.AddRange(GetDbCandidatesByEdition(possibleEditions, includeExisting));
            }

            candidateReleases.AddRange(GetSeriesPartCandidates(author, bookTags, includeExisting));

            return candidateReleases.DistinctBy(x => x.Edition.Id).ToList();
        }

        private List<CandidateEdition> GetDbCandidates(LocalEdition localEdition, bool includeExisting)
        {
            // most general version, nothing has been specified.
            // get all plausible authors, then all plausible books, then get releases for each of these.
            var candidateReleases = new List<CandidateEdition>();

            // check if it looks like VA.
            if (TrackGroupingService.IsVariousAuthors(localEdition.LocalBooks))
            {
                var va = _authorService.FindById(DistanceCalculator.VariousAuthorIds[0]);
                if (va != null)
                {
                    candidateReleases.AddRange(GetDbCandidatesByAuthor(localEdition, va, includeExisting));
                }
            }

            var authorTags = GetAuthorTags(localEdition);
            if (authorTags.Any())
            {
                var variants = authorTags
                    .SelectMany(x => DistanceCalculator.GetAuthorVariants(new List<string> { x }))
                    .Concat(authorTags)
                    .Where(x => x.IsNotNullOrWhiteSpace())
                    .Distinct(StringComparer.InvariantCultureIgnoreCase)
                    .ToList();

                foreach (var authorTag in variants)
                {
                    if (authorTag.IsNotNullOrWhiteSpace())
                    {
                        var possibleAuthors = _authorService.GetCandidates(authorTag);
                        foreach (var author in possibleAuthors)
                        {
                            candidateReleases.AddRange(GetDbCandidatesByAuthor(localEdition, author, includeExisting));
                        }
                    }
                }
            }

            return candidateReleases;
        }

        public IEnumerable<CandidateEdition> GetRemoteCandidates(LocalEdition localEdition, IdentificationOverrides idOverrides)
        {
            // TODO handle edition override

            // Gets candidate book releases from the metadata server.
            // Will eventually need adding locally if we find a match
            List<Book> remoteBooks;
            var seenCandidates = new HashSet<string>();

            var isbns = localEdition.LocalBooks.Select(x => x.FileTrackInfo.Isbn).Distinct().ToList();
            var asins = localEdition.LocalBooks.Select(x => x.FileTrackInfo.Asin).Distinct().ToList();
            var goodreads = localEdition.LocalBooks.Select(x => x.FileTrackInfo.GoodreadsId).Distinct().ToList();

            // grab possibilities for all the IDs present
            if (isbns.Count == 1 && isbns[0].IsNotNullOrWhiteSpace())
            {
                _logger.Trace($"Searching by isbn {isbns[0]}");

                try
                {
                    remoteBooks = _bookSearchService.SearchByIsbn(isbns[0]);
                }
                catch (GoodreadsException e)
                {
                    _logger.Info(e, "Skipping ISBN search due to Goodreads Error");
                    remoteBooks = new List<Book>();
                }

                foreach (var candidate in ToCandidates(remoteBooks, seenCandidates, idOverrides))
                {
                    yield return candidate;
                }
            }

            if (asins.Count == 1 &&
                asins[0].IsNotNullOrWhiteSpace() &&
                asins[0].Length == 10)
            {
                _logger.Trace($"Searching by asin {asins[0]}");

                try
                {
                    remoteBooks = _bookSearchService.SearchByAsin(asins[0]);
                }
                catch (GoodreadsException e)
                {
                    _logger.Info(e, "Skipping ASIN search due to Goodreads Error");
                    remoteBooks = new List<Book>();
                }

                foreach (var candidate in ToCandidates(remoteBooks, seenCandidates, idOverrides))
                {
                    yield return candidate;
                }
            }

            if (goodreads.Count == 1 &&
                goodreads[0].IsNotNullOrWhiteSpace())
            {
                if (int.TryParse(goodreads[0], out var id))
                {
                    _logger.Trace($"Searching by goodreads id {id}");

                    try
                    {
                        remoteBooks = _bookSearchService.SearchByGoodreadsBookId(id, true);
                    }
                    catch (GoodreadsException e)
                    {
                        _logger.Info(e, "Skipping Goodreads ID search due to Goodreads Error");
                        remoteBooks = new List<Book>();
                    }

                    foreach (var candidate in ToCandidates(remoteBooks, seenCandidates, idOverrides))
                    {
                        yield return candidate;
                    }
                }
            }

            // If we got an id result, or any overrides are set, stop
            if (seenCandidates.Any() ||
                idOverrides?.Edition != null ||
                idOverrides?.Book != null ||
                idOverrides?.Author != null)
            {
                yield break;
            }

            // fall back to author / book name search
            var authorTags = new List<string>();

            if (TrackGroupingService.IsVariousAuthors(localEdition.LocalBooks))
            {
                authorTags.Add("Various Authors");
            }
            else
            {
                authorTags.AddRange(GetAuthorTags(localEdition));
            }

            var bookTags = GetBookTags(localEdition);

            // If no valid author or book tags, stop
            if (!authorTags.Any() || !bookTags.Any())
            {
                yield break;
            }

            // Search by author+book
            foreach (var authorTag in authorTags.Take(5))
            {
                foreach (var bookTag in bookTags.Take(5))
                {
                    try
                    {
                        remoteBooks = _bookSearchService.SearchForNewBook(bookTag, authorTag);
                    }
                    catch (GoodreadsException e)
                    {
                        _logger.Info(e, "Skipping author/title search due to Goodreads Error");
                        remoteBooks = new List<Book>();
                    }

                    foreach (var candidate in ToCandidates(remoteBooks, seenCandidates, idOverrides))
                    {
                        yield return candidate;
                    }
                }
            }

            // If we got an author/book search result, stop
            if (seenCandidates.Any())
            {
                yield break;
            }

            // Search by just book title
            foreach (var bookTag in bookTags.Take(5))
            {
                try
                {
                    remoteBooks = _bookSearchService.SearchForNewBook(bookTag, null);
                }
                catch (GoodreadsException e)
                {
                    _logger.Info(e, "Skipping book title search due to Goodreads Error");
                    remoteBooks = new List<Book>();
                }

                foreach (var candidate in ToCandidates(remoteBooks, seenCandidates, idOverrides))
                {
                    yield return candidate;
                }
            }

            // Search by just author
            foreach (var a in authorTags.Take(5))
            {
                try
                {
                    remoteBooks = _bookSearchService.SearchForNewBook(a, null);
                }
                catch (GoodreadsException e)
                {
                    _logger.Info(e, "Skipping author search due to Goodreads Error");
                    remoteBooks = new List<Book>();
                }

                foreach (var candidate in ToCandidates(remoteBooks, seenCandidates, idOverrides))
                {
                    yield return candidate;
                }
            }
        }

        private static List<string> GetAuthorTags(LocalEdition localEdition)
        {
            var authorTags = new List<string>();

            var fileAuthors = localEdition.LocalBooks.MostCommon(x => x.FileTrackInfo.Authors) ?? new List<string>();
            authorTags.AddRange(fileAuthors.Where(x => x.IsNotNullOrWhiteSpace()));

            authorTags.AddRange(localEdition.LocalBooks
                .Select(x => x.FolderTrackInfo?.AuthorName)
                .Where(x => x.IsNotNullOrWhiteSpace()));

            authorTags.AddRange(localEdition.LocalBooks
                .Select(x => x.DownloadClientBookInfo?.AuthorName)
                .Where(x => x.IsNotNullOrWhiteSpace()));

            return authorTags
                .Distinct(StringComparer.InvariantCultureIgnoreCase)
                .ToList();
        }

        private static List<string> GetBookTags(LocalEdition localEdition)
        {
            var bookTags = new List<string>
            {
                localEdition.LocalBooks.MostCommon(x => x.FileTrackInfo.BookTitle),
                localEdition.LocalBooks.MostCommon(x => x.FolderTrackInfo?.BookTitle),
                localEdition.LocalBooks.MostCommon(x => x.DownloadClientBookInfo?.BookTitle)
            };

            return bookTags
                .SelectMany(ExpandBookTagVariants)
                .Where(x => x.IsNotNullOrWhiteSpace())
                .Distinct(StringComparer.InvariantCultureIgnoreCase)
                .ToList();
        }

        private static IEnumerable<string> ExpandBookTagVariants(string bookTag)
        {
            if (bookTag.IsNullOrWhiteSpace())
            {
                return Array.Empty<string>();
            }

            var variants = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);

            void Add(string value)
            {
                var cleaned = CleanTag(value);
                if (cleaned.IsNotNullOrWhiteSpace())
                {
                    variants.Add(cleaned);
                }
            }

            Add(bookTag);
            Add(bookTag.CleanBookTitle());
            Add(bookTag.RemoveBracketsAndContents());
            Add(bookTag.RemoveAfterDash());
            Add(LeadingPartRegex.Replace(bookTag, string.Empty));
            Add(RemoveSeriesPartRegex.Replace(bookTag, " "));

            if (bookTag.Contains(" - ", StringComparison.Ordinal))
            {
                var splitIndex = bookTag.LastIndexOf(" - ", StringComparison.Ordinal);
                Add(bookTag.Substring(0, splitIndex));
                Add(bookTag.Substring(splitIndex + 3));
            }

            if (bookTag.Contains(':'))
            {
                var split = bookTag.Split(':', 2);
                Add(split[0]);
                Add(split[1]);
            }

            return variants;
        }

        private static string CleanTag(string value)
        {
            if (value.IsNullOrWhiteSpace())
            {
                return string.Empty;
            }

            var cleaned = value.Replace('_', ' ').Trim(' ', '-', '_', '.', ',', ':', ';');
            cleaned = CollapseWhitespaceRegex.Replace(cleaned, " ");

            return cleaned.Trim();
        }

        private List<CandidateEdition> GetSeriesPartCandidates(Author author, IEnumerable<string> bookTags, bool includeExisting)
        {
            if (author == null)
            {
                return new List<CandidateEdition>();
            }

            var partNumbers = ParsePartNumbers(bookTags);
            if (!partNumbers.Any())
            {
                return new List<CandidateEdition>();
            }

            var matchingBooks = _bookService.GetBooksByAuthorMetadataId(author.AuthorMetadataId)
                .Where(book => MatchesSeriesPart(book, partNumbers))
                .DistinctBy(x => x.Id)
                .ToList();

            var candidates = new List<CandidateEdition>();
            foreach (var book in matchingBooks)
            {
                candidates.AddRange(GetDbCandidatesByBook(book, includeExisting));
            }

            return candidates;
        }

        private static List<double> ParsePartNumbers(IEnumerable<string> bookTags)
        {
            var partNumbers = new List<double>();

            foreach (var tag in bookTags.Where(x => x.IsNotNullOrWhiteSpace()))
            {
                var leadingMatch = LeadingPartRegex.Match(tag);
                if (leadingMatch.Success && TryParsePartNumber(leadingMatch.Groups["number"].Value, out var leadingPart))
                {
                    AddPartIfUnique(partNumbers, leadingPart);
                }

                foreach (Match match in SeriesPartRegex.Matches(tag))
                {
                    if (TryParsePartNumber(match.Groups["number"].Value, out var partNumber))
                    {
                        AddPartIfUnique(partNumbers, partNumber);
                    }
                }
            }

            return partNumbers;
        }

        private static void AddPartIfUnique(List<double> partNumbers, double value)
        {
            if (partNumbers.All(x => Math.Abs(x - value) > 0.01))
            {
                partNumbers.Add(value);
            }
        }

        private static bool TryParsePartNumber(string value, out double partNumber)
        {
            partNumber = 0;

            if (value.IsNullOrWhiteSpace())
            {
                return false;
            }

            var cleaned = value.Trim();
            if (double.TryParse(cleaned, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out partNumber))
            {
                return true;
            }

            var roman = RomanToInt(cleaned);
            if (roman > 0)
            {
                partNumber = roman;
                return true;
            }

            return false;
        }

        private static int RomanToInt(string value)
        {
            if (value.IsNullOrWhiteSpace())
            {
                return 0;
            }

            var roman = value.ToUpperInvariant();
            var total = 0;
            var lastValue = 0;

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

                if (current < lastValue)
                {
                    total -= current;
                }
                else
                {
                    total += current;
                    lastValue = current;
                }
            }

            return total;
        }

        private static bool MatchesSeriesPart(Book book, List<double> partNumbers)
        {
            if (book == null || !(book.SeriesLinks?.Value?.Any() ?? false))
            {
                return false;
            }

            foreach (var seriesLink in book.SeriesLinks.Value)
            {
                foreach (var partNumber in partNumbers)
                {
                    if (Math.Abs(seriesLink.SeriesPosition - partNumber) < 0.01)
                    {
                        return true;
                    }

                    if (TryParsePartNumber(seriesLink.Position, out var seriesPosition) && Math.Abs(seriesPosition - partNumber) < 0.01)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private List<CandidateEdition> ToCandidates(IEnumerable<Book> books, HashSet<string> seenCandidates, IdentificationOverrides idOverrides)
        {
            var candidates = new List<CandidateEdition>();

            foreach (var book in books)
            {
                // We have to make sure various bits and pieces are populated that are normally handled
                // by a database lazy load
                foreach (var edition in book.Editions.Value)
                {
                    edition.Book = book;

                    if (!seenCandidates.Contains(edition.ForeignEditionId) && SatisfiesOverride(edition, idOverrides))
                    {
                        seenCandidates.Add(edition.ForeignEditionId);
                        candidates.Add(new CandidateEdition
                        {
                            Edition = edition,
                            ExistingFiles = new List<BookFile>()
                        });
                    }
                }
            }

            return candidates;
        }

        private bool SatisfiesOverride(Edition edition, IdentificationOverrides idOverride)
        {
            if (idOverride?.Edition != null)
            {
                return edition.ForeignEditionId == idOverride.Edition.ForeignEditionId;
            }

            if (idOverride?.Book != null)
            {
                return edition.Book.Value.ForeignBookId == idOverride.Book.ForeignBookId;
            }

            if (idOverride?.Author != null)
            {
                return edition.Book.Value.Author.Value.ForeignAuthorId == idOverride.Author.ForeignAuthorId;
            }

            return true;
        }
    }
}
