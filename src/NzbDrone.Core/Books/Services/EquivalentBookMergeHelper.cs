using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.Books
{
    public static class EquivalentBookMergeHelper
    {
        public static void CollapseEquivalentRemoteBooks(Author author, IEnumerable<Book> localBooks, IReadOnlyDictionary<int, int> localFileCounts, Logger logger)
        {
            var remoteBooks = author?.Books?.Value;

            if (remoteBooks == null || remoteBooks.Count < 2)
            {
                return;
            }

            var localBookList = localBooks?.ToList() ?? new List<Book>();
            var aliasMap = new Dictionary<string, string>(StringComparer.Ordinal);
            var collapsed = new List<Book>();

            foreach (var group in remoteBooks.GroupBy(GetDuplicateKey))
            {
                var candidates = group.ToList();

                if (!ShouldCollapse(candidates, localBookList, localFileCounts))
                {
                    collapsed.AddRange(candidates);
                    continue;
                }

                var canonical = ChoosePreferredRemoteBook(candidates, localBookList, localFileCounts);
                var duplicates = candidates.Where(book => !ReferenceEquals(book, canonical)).ToList();

                foreach (var duplicate in duplicates)
                {
                    if (!duplicate.ForeignBookId.IsNullOrWhiteSpace())
                    {
                        aliasMap[duplicate.ForeignBookId] = canonical.ForeignBookId;
                    }

                    MergeRemoteBook(canonical, duplicate);
                }

                if (duplicates.Any())
                {
                    logger.Debug("Collapsing equivalent remote books [{0}] into [{1}][{2}]",
                                 string.Join(", ", duplicates.Select(x => x.ForeignBookId)),
                                 canonical.ForeignBookId,
                                 canonical.Title);
                }

                collapsed.Add(canonical);
            }

            author.Books = collapsed.DistinctBy(x => x.ForeignBookId).ToList();

            if (aliasMap.Any())
            {
                RewriteSeriesLinks(author, aliasMap);
            }
        }

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

            if (!TryParseForeignBookId(localBook.ForeignBookId, out var localForeignBookId))
            {
                return null;
            }

            var candidates = remoteList.Where(x => (x.RelatedBooks ?? new List<int>()).Contains(localForeignBookId) && HasCompatibleIdentity(x, localBook)).ToList();

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
            var candidates = localBooks.Where(x => IsEquivalentLocalCandidate(x, remoteBook)).ToList();

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

        private static bool ShouldCollapse(List<Book> candidates, List<Book> localBooks, IReadOnlyDictionary<int, int> localFileCounts)
        {
            if (candidates.Count < 2)
            {
                return false;
            }

            if (GetReleaseDate(candidates[0]) == null || GetDuplicateTitle(candidates[0]).IsNullOrWhiteSpace())
            {
                return false;
            }

            if (!candidates.Any(HasAudioEdition))
            {
                return false;
            }

            if (GetDistinctSeriesPositions(candidates).Count > 1)
            {
                return false;
            }

            var localMatches = localBooks.Where(local => candidates.Any(remote => HasCompatibleIdentity(local, remote))).ToList();

            return localMatches.Count(local => GetFileCount(localFileCounts, local) > 0) <= 1;
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

        private static Book ChoosePreferredRemoteBook(List<Book> candidates, List<Book> localBooks, IReadOnlyDictionary<int, int> localFileCounts)
        {
            return candidates.OrderByDescending(remote => localBooks.Where(local => local.ForeignBookId == remote.ForeignBookId)
                                                               .Select(local => GetFileCount(localFileCounts, local))
                                                               .DefaultIfEmpty(0)
                                                               .Max())
                             .ThenByDescending(HasAudioEdition)
                             .ThenByDescending(remote => !GetSeriesPosition(remote).IsNullOrWhiteSpace())
                             .ThenByDescending(remote => remote.Editions?.Value?.Count ?? 0)
                             .ThenByDescending(remote => remote.Ratings?.Popularity ?? 0)
                             .ThenBy(remote => remote.ForeignBookId, StringComparer.Ordinal)
                             .First();
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

        private static void MergeRemoteBook(Book canonical, Book duplicate)
        {
            canonical.Editions = (canonical.Editions?.Value ?? new List<Edition>())
                .Concat(duplicate.Editions?.Value ?? new List<Edition>())
                .GroupBy(x => x.ForeignEditionId, StringComparer.Ordinal)
                .Select(x => x.OrderByDescending(e => e.Monitored)
                              .ThenByDescending(e => IsAudioFormat(e.Format))
                              .ThenByDescending(e => e.Ratings?.Popularity ?? 0)
                              .First())
                .ToList();

            canonical.Genres = (canonical.Genres ?? new List<string>()).Union(duplicate.Genres ?? new List<string>()).ToList();
            canonical.Links = (canonical.Links ?? new List<Links>()).Concat(duplicate.Links ?? new List<Links>())
                                   .Where(x => !x.Url.IsNullOrWhiteSpace())
                                   .DistinctBy(x => $"{x.Url}|{x.Name}")
                                   .ToList();
            canonical.RelatedBooks = (canonical.RelatedBooks ?? new List<int>())
                .Union(duplicate.RelatedBooks ?? new List<int>())
                .Union(GetForeignBookIds(duplicate))
                .Distinct()
                .ToList();

            if (canonical.ReleaseDate == null)
            {
                canonical.ReleaseDate = duplicate.ReleaseDate;
            }

            if (canonical.Title.IsNullOrWhiteSpace())
            {
                canonical.Title = duplicate.Title;
            }

            if (canonical.CleanTitle.IsNullOrWhiteSpace())
            {
                canonical.CleanTitle = duplicate.CleanTitle;
            }
        }

        private static void RewriteSeriesLinks(Author author, Dictionary<string, string> aliasMap)
        {
            var books = author?.Books?.Value;
            var series = author?.Series?.Value;

            if (books == null || series == null)
            {
                return;
            }

            var bookDict = books.ToDictionary(x => x.ForeignBookId, StringComparer.Ordinal);

            foreach (var book in books)
            {
                book.SeriesLinks = new List<SeriesBookLink>();
            }

            foreach (var item in series)
            {
                var rewritten = new List<SeriesBookLink>();

                foreach (var link in item.LinkItems?.Value ?? new List<SeriesBookLink>())
                {
                    var foreignBookId = link.Book?.Value?.ForeignBookId;

                    if (foreignBookId.IsNullOrWhiteSpace())
                    {
                        continue;
                    }

                    if (aliasMap.TryGetValue(foreignBookId, out var canonicalId))
                    {
                        foreignBookId = canonicalId;
                    }

                    if (!bookDict.TryGetValue(foreignBookId, out var canonicalBook))
                    {
                        continue;
                    }

                    link.Book = canonicalBook;
                    link.Series = item;
                    rewritten.Add(link);
                }

                item.LinkItems = rewritten.GroupBy(x => $"{x.Book.Value.ForeignBookId}|{x.Position}|{x.SeriesPosition}|{x.IsPrimary}")
                                          .Select(x => x.First())
                                          .ToList();

                foreach (var link in item.LinkItems.Value)
                {
                    link.Book.Value.SeriesLinks.Value.Add(link);
                }
            }
        }

        private static bool IsEquivalentLocalCandidate(Book localBook, Book remoteBook)
        {
            if (localBook.ForeignBookId == remoteBook.ForeignBookId)
            {
                return true;
            }

            if (!HasCompatibleIdentity(localBook, remoteBook))
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

        private static string GetDuplicateKey(Book book)
        {
            return $"{GetDuplicateTitle(book)}|{GetReleaseDate(book)?.ToString("yyyy-MM-dd") ?? string.Empty}";
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

        private static IEnumerable<int> GetForeignBookIds(Book book)
        {
            if (TryParseForeignBookId(book.ForeignBookId, out var foreignBookId))
            {
                yield return foreignBookId;
            }
        }

        private static bool TryParseForeignBookId(string foreignBookId, out int parsed)
        {
            return int.TryParse(foreignBookId, out parsed);
        }
    }
}
