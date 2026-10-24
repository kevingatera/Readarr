using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.Books
{
    public static class EquivalentBookMergeHelper
    {
        public static Book FindMatchingRemoteBook(Book localBook, IEnumerable<Book> remoteBooks)
        {
            if (localBook == null || remoteBooks == null)
            {
                return null;
            }

            var remoteList = remoteBooks.ToList();
            var exactMatch = remoteList.SingleOrDefault(x => x.ForeignBookId == localBook.ForeignBookId);

            if (exactMatch != null)
            {
                return exactMatch;
            }

            // A related-work link only identifies the same book when the local row is audiobook-backed.
            if (!HasAudioEdition(localBook))
            {
                return null;
            }

            if (!TryParseForeignBookId(localBook.ForeignBookId, out var localForeignBookId))
            {
                return null;
            }

            var candidates = remoteList.Where(x => (x.RelatedBooks ?? new List<int>()).Contains(localForeignBookId) && HasCompatibleIdentity(x, localBook) && HasAudioEdition(x)).ToList();

            if (!candidates.Any())
            {
                return null;
            }

            return candidates.OrderByDescending(HasAudioEdition)
                             .ThenByDescending(x => !GetSeriesPosition(x).IsNullOrWhiteSpace())
                             .ThenByDescending(x => x.Editions?.Value?.Count ?? 0)
                             .ThenByDescending(x => x.Ratings?.Popularity ?? 0)
                             .ThenBy(x => x.ForeignBookId, StringComparer.Ordinal)
                             .First();
        }

        public static Tuple<Book, List<Book>> FindMatchingLocalBook(List<Book> localBooks, Book remoteBook, IReadOnlyDictionary<int, int> localFileCounts)
        {
            // Related-work links only count when a file-backed local book with the same foreign id anchors the match.
            var anchorHasFiles = localBooks.Any(x => x.ForeignBookId == remoteBook.ForeignBookId && localFileCounts.TryGetValue(x.Id, out var count) && count > 0);
            var candidates = localBooks.Where(x => IsEquivalentLocalCandidate(x, remoteBook, anchorHasFiles)).ToList();

            if (!candidates.Any())
            {
                return Tuple.Create(default(Book), new List<Book>());
            }

            if (candidates.Count == 1)
            {
                return Tuple.Create(candidates[0], new List<Book>());
            }

            var exactMatches = candidates.Where(x => x.ForeignBookId == remoteBook.ForeignBookId).ToList();

            if (!ShouldMergeLocalCandidates(remoteBook, candidates, localFileCounts))
            {
                var retained = exactMatches.FirstOrDefault() ?? ChoosePreferredLocalBook(candidates, remoteBook, localFileCounts, false);
                return Tuple.Create(retained, new List<Book>());
            }

            var existing = ChoosePreferredLocalBook(candidates, remoteBook, localFileCounts, true);
            var merged = candidates.Where(x => x.Id != existing.Id).ToList();

            return Tuple.Create(existing, merged);
        }

        private static bool ShouldMergeLocalCandidates(Book remoteBook, List<Book> candidates, IReadOnlyDictionary<int, int> localFileCounts)
        {
            if (!HasAudioEdition(remoteBook))
            {
                return false;
            }

            var withRemote = candidates.Append(remoteBook).ToList();

            if (GetDistinctSeriesPositions(withRemote).Count > 1)
            {
                return false;
            }

            return candidates.Count(local => GetFileCount(localFileCounts, local) > 0) <= 1;
        }

        private static Book ChoosePreferredLocalBook(List<Book> candidates, Book remoteBook, IReadOnlyDictionary<int, int> localFileCounts, bool preferFiles)
        {
            return candidates.OrderByDescending(local => preferFiles ? GetFileCount(localFileCounts, local) : 0)
                             .ThenByDescending(local => local.ForeignBookId == remoteBook.ForeignBookId)
                             .ThenByDescending(local => !GetSeriesPosition(local).IsNullOrWhiteSpace())
                             .ThenByDescending(local => local.Added)
                             .ThenBy(local => local.Id)
                             .First();
        }

        private static bool IsEquivalentLocalCandidate(Book localBook, Book remoteBook, bool anchorHasFiles)
        {
            if (localBook.ForeignBookId == remoteBook.ForeignBookId)
            {
                return true;
            }

            if (!anchorHasFiles || !HasCompatibleIdentity(localBook, remoteBook))
            {
                return false;
            }

            if (!TryParseForeignBookId(localBook.ForeignBookId, out var localForeignBookId))
            {
                return false;
            }

            if ((remoteBook.RelatedBooks ?? new List<int>()).Contains(localForeignBookId))
            {
                return true;
            }

            if (TryParseForeignBookId(remoteBook.ForeignBookId, out var remoteForeignBookId) &&
                (localBook.RelatedBooks ?? new List<int>()).Contains(remoteForeignBookId))
            {
                return true;
            }

            return false;
        }

        private static bool HasCompatibleIdentity(Book left, Book right)
        {
            return GetDuplicateTitle(left).Equals(GetDuplicateTitle(right), StringComparison.OrdinalIgnoreCase) &&
                HasCompatibleReleaseDate(left, right) &&
                HasCompatibleSeriesPosition(left, right);
        }

        private static bool HasCompatibleReleaseDate(Book left, Book right)
        {
            var leftDate = GetReleaseDate(left);
            var rightDate = GetReleaseDate(right);

            if (leftDate.HasValue && rightDate.HasValue)
            {
                return leftDate.Value == rightDate.Value;
            }

            return leftDate.HasValue || rightDate.HasValue;
        }

        private static bool HasCompatibleSeriesPosition(Book left, Book right)
        {
            return GetDistinctSeriesPositions(new[] { left, right }).Count <= 1;
        }

        private static List<string> GetDistinctSeriesPositions(IEnumerable<Book> books)
        {
            return books.Select(GetSeriesPosition)
                        .Where(x => !x.IsNullOrWhiteSpace())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
        }

        private static string GetSeriesPosition(Book book)
        {
            var primaryLink = book.SeriesLinks?.Value?.FirstOrDefault(x => x.IsPrimary) ?? book.SeriesLinks?.Value?.FirstOrDefault();

            if (primaryLink == null)
            {
                return string.Empty;
            }

            if (!primaryLink.Position.IsNullOrWhiteSpace())
            {
                return primaryLink.Position.Trim();
            }

            return primaryLink.SeriesPosition > 0 ? primaryLink.SeriesPosition.ToString() : string.Empty;
        }

        private static string GetDuplicateTitle(Book book)
        {
            return (book.CleanTitle ?? Parser.Parser.CleanAuthorName(book.Title ?? string.Empty)).NullSafe().Trim();
        }

        private static DateTime? GetReleaseDate(Book book)
        {
            return book.ReleaseDate?.Date;
        }

        private static bool HasAudioEdition(Book book)
        {
            return (book.Editions?.Value ?? new List<Edition>()).Any(x => IsAudioFormat(x.Format));
        }

        private static bool IsAudioFormat(string format)
        {
            if (format.IsNullOrWhiteSpace())
            {
                return false;
            }

            var normalized = format.Trim().ToLowerInvariant();

            return normalized.Contains("audio") ||
                   normalized.Contains("audible") ||
                   normalized.Contains("m4b") ||
                   normalized.Contains("m4a") ||
                   normalized.Contains("mp3") ||
                   normalized.Contains("cassette");
        }

        private static int GetFileCount(IReadOnlyDictionary<int, int> localFileCounts, Book book)
        {
            if (localFileCounts != null && localFileCounts.TryGetValue(book.Id, out var count))
            {
                return count;
            }

            return 0;
        }

        private static bool TryParseForeignBookId(string foreignBookId, out int parsed)
        {
            return int.TryParse(foreignBookId, out parsed);
        }
    }
}
