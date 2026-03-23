using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common;
using NzbDrone.Common.Crypto;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.MediaFiles.BookImport.Manual
{
    public interface IManualImportService
    {
        List<ManualImportItem> GetMediaFiles(string path, string downloadId, Author author, FilterFilesType filter, bool replaceExistingFiles);
        List<ManualImportItem> UpdateItems(List<ManualImportItem> item);
    }

    public class ManualImportService : IExecute<ManualImportCommand>, IManualImportService
    {
        private static readonly Regex LeadingPartRegex = new Regex(@"^\s*(?:(?:book|part|pt|chapter|disc|cd|track)\s*)?(?<number>\d+(?:\.\d+)?|[ivxlcdm]+)\s*[-._:)]*\s+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RemoveSeriesPartRegex = new Regex(@"\b(?:book|part|pt)\s*(?:\d+(?:\.\d+)?|[ivxlcdm]+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex CollapseWhitespaceRegex = new Regex(@"\s{2,}", RegexOptions.Compiled);

        private readonly IDiskProvider _diskProvider;
        private readonly IParsingService _parsingService;
        private readonly IRootFolderService _rootFolderService;
        private readonly IDiskScanService _diskScanService;
        private readonly IMakeImportDecision _importDecisionMaker;
        private readonly IAuthorService _authorService;
        private readonly IBookService _bookService;
        private readonly IEditionService _editionService;
        private readonly IProvideBookInfo _bookInfo;
        private readonly IMetadataTagService _metadataTagService;
        private readonly IImportApprovedBooks _importApprovedBooks;
        private readonly ICustomFormatCalculationService _formatCalculator;
        private readonly ITrackedDownloadService _trackedDownloadService;
        private readonly IDownloadedBooksImportService _downloadedTracksImportService;
        private readonly IProvideImportItemService _provideImportItemService;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        public ManualImportService(IDiskProvider diskProvider,
                                   IParsingService parsingService,
                                   IRootFolderService rootFolderService,
                                   IDiskScanService diskScanService,
                                   IMakeImportDecision importDecisionMaker,
                                   IAuthorService authorService,
                                   IBookService bookService,
                                   IEditionService editionService,
                                   IProvideBookInfo bookInfo,
                                   IMetadataTagService metadataTagService,
                                   IImportApprovedBooks importApprovedBooks,
                                   ICustomFormatCalculationService formatCalculator,
                                   ITrackedDownloadService trackedDownloadService,
                                   IDownloadedBooksImportService downloadedTracksImportService,
                                   IProvideImportItemService provideImportItemService,
                                   IEventAggregator eventAggregator,
                                   Logger logger)
        {
            _diskProvider = diskProvider;
            _parsingService = parsingService;
            _rootFolderService = rootFolderService;
            _diskScanService = diskScanService;
            _importDecisionMaker = importDecisionMaker;
            _authorService = authorService;
            _bookService = bookService;
            _editionService = editionService;
            _bookInfo = bookInfo;
            _metadataTagService = metadataTagService;
            _importApprovedBooks = importApprovedBooks;
            _formatCalculator = formatCalculator;
            _trackedDownloadService = trackedDownloadService;
            _downloadedTracksImportService = downloadedTracksImportService;
            _provideImportItemService = provideImportItemService;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        public List<ManualImportItem> GetMediaFiles(string path, string downloadId, Author author, FilterFilesType filter, bool replaceExistingFiles)
        {
            if (downloadId.IsNotNullOrWhiteSpace())
            {
                var trackedDownload = _trackedDownloadService.Find(downloadId);

                if (trackedDownload == null)
                {
                    return new List<ManualImportItem>();
                }

                if (trackedDownload.ImportItem == null)
                {
                    trackedDownload.ImportItem = _provideImportItemService.ProvideImportItem(trackedDownload.DownloadItem, trackedDownload.ImportItem);
                }

                path = trackedDownload.ImportItem.OutputPath.FullPath;
            }

            if (!_diskProvider.FolderExists(path))
            {
                if (!_diskProvider.FileExists(path))
                {
                    return new List<ManualImportItem>();
                }

                var files = new List<IFileInfo> { _diskProvider.GetFileInfo(path) };

                var config = new ImportDecisionMakerConfig
                {
                    Filter = FilterFilesType.None,
                    NewDownload = true,
                    SingleRelease = false,
                    IncludeExisting = !replaceExistingFiles,
                    AllowSameFileMatch = replaceExistingFiles,
                    AddNewAuthors = false,
                    KeepAllEditions = true
                };

                var decision = _importDecisionMaker.GetImportDecisions(files, null, null, config);
                var result = MapItem(decision.First(), downloadId, replaceExistingFiles, false);

                return new List<ManualImportItem> { result };
            }

            return ProcessFolder(path, downloadId, author, filter, replaceExistingFiles);
        }

        private List<ManualImportItem> ProcessFolder(string folder, string downloadId, Author author, FilterFilesType filter, bool replaceExistingFiles)
        {
            DownloadClientItem downloadClientItem = null;
            var directoryInfo = new DirectoryInfo(folder);
            author = author ?? _parsingService.GetAuthor(directoryInfo.Name);

            if (downloadId.IsNotNullOrWhiteSpace())
            {
                var trackedDownload = _trackedDownloadService.Find(downloadId);
                downloadClientItem = trackedDownload?.DownloadItem;

                if (author == null)
                {
                    author = trackedDownload?.RemoteBook?.Author;
                }
            }

            var authorFiles = _diskScanService.GetBookFiles(folder).ToList();
            var itemInfo = new ImportDecisionMakerInfo
            {
                DownloadClientItem = downloadClientItem,
                ParsedBookInfo = Parser.Parser.ParseBookTitle(directoryInfo.Name)
            };
            var idOverrides = ResolveIdentificationOverrides(author, authorFiles, itemInfo);
            var config = new ImportDecisionMakerConfig
            {
                Filter = filter,
                NewDownload = true,
                SingleRelease = false,
                IncludeExisting = !replaceExistingFiles,
                AllowSameFileMatch = replaceExistingFiles,
                AddNewAuthors = false,
                KeepAllEditions = true
            };

            var decisions = _importDecisionMaker.GetImportDecisions(authorFiles, idOverrides, itemInfo, config);

            // paths will be different for new and old files which is why we need to map separately
            var newFiles = authorFiles.Join(decisions,
                                            f => f.FullName,
                                            d => d.Item.Path,
                                            (f, d) => new { File = f, Decision = d },
                                            PathEqualityComparer.Instance);

            var newItems = newFiles.Select(x => MapItem(x.Decision, downloadId, replaceExistingFiles, false));
            var existingDecisions = decisions.Except(newFiles.Select(x => x.Decision));
            var existingItems = existingDecisions.Select(x => MapItem(x, null, replaceExistingFiles, false));

            return newItems.Concat(existingItems).ToList();
        }

        private IdentificationOverrides ResolveIdentificationOverrides(Author author, List<IFileInfo> authorFiles, ImportDecisionMakerInfo itemInfo)
        {
            var idOverrides = new IdentificationOverrides
            {
                Author = author
            };

            if (author?.AuthorMetadataId <= 0 || !authorFiles.Any())
            {
                return idOverrides;
            }

            var embeddedTracks = authorFiles
                .Select(file => _metadataTagService.ReadTags(file))
                .Where(track => track != null)
                .ToList();

            var distinctEmbeddedTitles = embeddedTracks
                .SelectMany(track => new[] { track.BookTitle, track.Title })
                .Where(title => title.IsNotNullOrWhiteSpace())
                .Select(CanonicalizeTitle)
                .Where(title => title.IsNotNullOrWhiteSpace())
                .Distinct(StringComparer.InvariantCultureIgnoreCase)
                .ToList();

            // Only apply a direct override when the embedded tags consistently describe one book.
            if (distinctEmbeddedTitles.Count > 2)
            {
                return idOverrides;
            }

            var titleCandidates = GetTitleCandidates(embeddedTracks, itemInfo?.ParsedBookInfo);
            var book = ResolveBookOverride(author, titleCandidates);
            if (book == null)
            {
                return idOverrides;
            }

            idOverrides.Book = book;

            var edition = ResolveEditionOverride(author, book, titleCandidates);
            if (edition != null)
            {
                idOverrides.Edition = edition;
            }

            _logger.Debug("Manual import resolved book override to '{0}'", book.Title);
            return idOverrides;
        }

        private Book ResolveBookOverride(Author author, List<string> titleCandidates)
        {
            if (author?.AuthorMetadataId <= 0 || !titleCandidates.Any())
            {
                return null;
            }

            var authorBooks = _bookService.GetBooksByAuthorMetadataId(author.AuthorMetadataId) ?? new List<Book>();
            var directMatches = authorBooks
                .Where(book => titleCandidates.Any(candidate => LooksLikeSameBook(book.Title, candidate)))
                .DistinctBy(x => x.Id)
                .ToList();

            if (directMatches.Count == 1)
            {
                return directMatches[0];
            }

            var candidateMatches = titleCandidates
                .SelectMany(candidate => _bookService.GetCandidates(author.AuthorMetadataId, candidate))
                .GroupBy(book => book.Id)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key)
                .ToList();

            if (candidateMatches.Count == 1)
            {
                return candidateMatches[0].First();
            }

            if (candidateMatches.Count > 1 && candidateMatches[0].Count() > candidateMatches[1].Count())
            {
                return candidateMatches[0].First();
            }

            foreach (var candidate in titleCandidates)
            {
                var book = _bookService.FindByTitle(author.AuthorMetadataId, candidate) ??
                           _bookService.FindByTitleInexact(author.AuthorMetadataId, candidate);

                if (book != null)
                {
                    return book;
                }
            }

            return null;
        }

        private Edition ResolveEditionOverride(Author author, Book book, List<string> titleCandidates)
        {
            if (author?.AuthorMetadataId <= 0 || book == null)
            {
                return null;
            }

            var editions = _editionService.GetEditionsByBook(book.Id) ?? new List<Edition>();
            var directMatches = editions
                .Where(edition => titleCandidates.Any(candidate => LooksLikeSameBook(edition.Title, candidate)))
                .DistinctBy(x => x.Id)
                .ToList();

            if (directMatches.Count == 1)
            {
                return directMatches[0];
            }

            if (editions.Count == 1)
            {
                return editions[0];
            }

            foreach (var candidate in titleCandidates)
            {
                var edition = _editionService.FindByTitle(author.AuthorMetadataId, candidate) ??
                              _editionService.FindByTitleInexact(author.AuthorMetadataId, candidate);

                if (edition?.BookId == book.Id)
                {
                    return edition;
                }

                var candidateEditions = _editionService.GetCandidates(author.AuthorMetadataId, candidate)
                    .Where(x => x.BookId == book.Id)
                    .DistinctBy(x => x.Id)
                    .ToList();

                if (candidateEditions.Count == 1)
                {
                    return candidateEditions[0];
                }
            }

            return null;
        }

        private static List<string> GetTitleCandidates(List<ParsedTrackInfo> embeddedTracks, ParsedBookInfo parsedBookInfo)
        {
            var candidates = new List<string>();

            void Add(string value)
            {
                if (value.IsNullOrWhiteSpace())
                {
                    return;
                }

                foreach (var variant in ExpandTitleCandidates(value))
                {
                    if (!candidates.Any(x => x.Equals(variant, StringComparison.InvariantCultureIgnoreCase)))
                    {
                        candidates.Add(variant);
                    }
                }
            }

            Add(embeddedTracks.MostCommon(x => x.BookTitle));
            Add(embeddedTracks.MostCommon(x => x.Title));
            Add(parsedBookInfo?.BookTitle);

            return candidates;
        }

        private static IEnumerable<string> ExpandTitleCandidates(string title)
        {
            if (title.IsNullOrWhiteSpace())
            {
                yield break;
            }

            foreach (var candidate in new[]
                     {
                         title,
                         Parser.Parser.ParseBookTitle(title)?.BookTitle,
                         title.CleanBookTitle(),
                         title.RemoveBracketsAndContents(),
                         title.RemoveAfterDash(),
                         LeadingPartRegex.Replace(title, string.Empty),
                         RemoveSeriesPartRegex.Replace(title, " ")
                     }
                     .Where(x => x.IsNotNullOrWhiteSpace())
                     .Select(CanonicalizeTitle)
                     .Where(x => x.IsNotNullOrWhiteSpace()))
            {
                yield return candidate;

                if (candidate.Contains(" - ", StringComparison.Ordinal))
                {
                    var splitIndex = candidate.LastIndexOf(" - ", StringComparison.Ordinal);
                    yield return CanonicalizeTitle(candidate.Substring(0, splitIndex));
                    yield return CanonicalizeTitle(candidate.Substring(splitIndex + 3));
                }

                if (candidate.Contains(':'))
                {
                    var split = candidate.Split(':', 2);
                    yield return CanonicalizeTitle(split[0]);
                    yield return CanonicalizeTitle(split[1]);
                }
            }
        }

        private static string CanonicalizeTitle(string title)
        {
            if (title == null)
            {
                return null;
            }

            var normalized = title
                .Trim(' ', '-', '_', '.', ',', ':', ';')
                .Replace('_', ' ');

            return CollapseWhitespaceRegex.Replace(normalized, " ");
        }

        private static bool LooksLikeSameBook(string expectedTitle, string candidateTitle)
        {
            if (expectedTitle.IsNullOrWhiteSpace() || candidateTitle.IsNullOrWhiteSpace())
            {
                return false;
            }

            var expectedNormalizedTitle = Parser.Parser.NormalizeTitle(expectedTitle);
            var normalizedCandidate = Parser.Parser.NormalizeTitle(candidateTitle);
            if (expectedNormalizedTitle.IsNullOrWhiteSpace() || normalizedCandidate.IsNullOrWhiteSpace())
            {
                return false;
            }

            var expectedTokens = expectedNormalizedTitle
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .ToList();

            var candidateTokens = normalizedCandidate
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .ToList();

            if (expectedTokens.Any() && expectedTokens.All(token => candidateTokens.Contains(token)))
            {
                return true;
            }

            if (candidateTokens.Any() && candidateTokens.All(token => expectedTokens.Contains(token)))
            {
                return true;
            }

            return normalizedCandidate.LevenshteinCoefficient(expectedNormalizedTitle) >= 0.8;
        }

        public List<ManualImportItem> UpdateItems(List<ManualImportItem> items)
        {
            var replaceExistingFiles = items.All(x => x.ReplaceExistingFiles);
            var groupedItems = items.Where(x => !x.AdditionalFile).GroupBy(x => x.Book?.Id);
            _logger.Debug($"UpdateItems, {groupedItems.Count()} groups, replaceExisting {replaceExistingFiles}");

            var result = new List<ManualImportItem>();

            foreach (var group in groupedItems)
            {
                _logger.Debug("UpdateItems, group key: {0}", group.Key);

                var disableReleaseSwitching = group.First().DisableReleaseSwitching;

                var files = group.Select(x => _diskProvider.GetFileInfo(x.Path)).ToList();
                var idOverride = new IdentificationOverrides
                {
                    Author = group.First().Author,
                    Book = group.First().Book,
                    Edition = group.First().Edition
                };
                var config = new ImportDecisionMakerConfig
                {
                    Filter = FilterFilesType.None,
                    NewDownload = true,
                    SingleRelease = true,
                    IncludeExisting = !replaceExistingFiles,
                    AllowSameFileMatch = replaceExistingFiles,
                    AddNewAuthors = false
                };
                var decisions = _importDecisionMaker.GetImportDecisions(files, idOverride, null, config);

                var existingItems = group.Join(decisions,
                                               i => i.Path,
                                               d => d.Item.Path,
                                               (i, d) => new { Item = i, Decision = d },
                                               PathEqualityComparer.Instance);

                foreach (var pair in existingItems)
                {
                    var item = pair.Item;
                    var decision = pair.Decision;

                    if (decision.Item.Author != null)
                    {
                        item.Author = decision.Item.Author;
                    }

                    if (decision.Item.Book != null)
                    {
                        item.Book = decision.Item.Book;
                        item.Edition = decision.Item.Edition;
                    }

                    if (item.Quality?.Quality == Quality.Unknown)
                    {
                        item.Quality = decision.Item.Quality;
                    }

                    if (item.ReleaseGroup.IsNullOrWhiteSpace())
                    {
                        item.ReleaseGroup = decision.Item.ReleaseGroup;
                    }

                    item.Rejections = decision.Rejections;
                    item.Size = decision.Item.Size;

                    result.Add(item);
                }

                var newDecisions = decisions.Except(existingItems.Select(x => x.Decision));
                result.AddRange(newDecisions.Select(x => MapItem(x, null, replaceExistingFiles, disableReleaseSwitching)));
            }

            return result;
        }

        private ManualImportItem MapItem(ImportDecision<LocalBook> decision, string downloadId, bool replaceExistingFiles, bool disableReleaseSwitching)
        {
            var item = new ManualImportItem();

            item.Id = HashConverter.GetHashInt31(decision.Item.Path);
            item.Path = decision.Item.Path;
            item.Name = Path.GetFileNameWithoutExtension(decision.Item.Path);
            item.DownloadId = downloadId;

            if (decision.Item.Author != null)
            {
                item.Author = decision.Item.Author;

                item.CustomFormats = _formatCalculator.ParseCustomFormat(decision.Item);
            }

            if (decision.Item.Book != null)
            {
                item.Book = decision.Item.Book;
                item.Edition = decision.Item.Edition;
            }

            item.Quality = decision.Item.Quality;
            item.IndexerFlags = (int)decision.Item.IndexerFlags;
            item.Size = _diskProvider.GetFileSize(decision.Item.Path);
            item.Rejections = decision.Rejections;
            item.Tags = decision.Item.FileTrackInfo;
            item.AdditionalFile = decision.Item.AdditionalFile;
            item.ReplaceExistingFiles = replaceExistingFiles;
            item.DisableReleaseSwitching = disableReleaseSwitching;

            return item;
        }

        public void Execute(ManualImportCommand message)
        {
            _logger.ProgressTrace("Manually importing {0} files using mode {1}", message.Files.Count, message.ImportMode);

            var imported = new List<ImportResult>();
            var importedTrackedDownload = new List<ManuallyImportedFile>();
            var bookIds = message.Files.GroupBy(e => e.BookId).ToList();
            var fileCount = 0;

            foreach (var importBookId in bookIds)
            {
                var bookImportDecisions = new List<ImportDecision<LocalBook>>();

                // turn off anyReleaseOk if specified
                if (importBookId.First().DisableReleaseSwitching)
                {
                    var book = _bookService.GetBook(importBookId.First().BookId);
                    book.AnyEditionOk = false;
                    _bookService.UpdateBook(book);
                }

                foreach (var file in importBookId)
                {
                    _logger.ProgressTrace("Processing file {0} of {1}", fileCount + 1, message.Files.Count);

                    var author = _authorService.GetAuthor(file.AuthorId);
                    var book = _bookService.GetBook(file.BookId);

                    var edition = _editionService.GetEditionByForeignEditionId(file.ForeignEditionId);
                    if (edition == null)
                    {
                        var tuple = _bookInfo.GetBookInfo(book.ForeignBookId);
                        edition = tuple.Item2.Editions.Value.SingleOrDefault(x => x.ForeignEditionId == file.ForeignEditionId);
                    }

                    var fileRootFolder = _rootFolderService.GetBestRootFolder(file.Path);
                    var fileInfo = _diskProvider.GetFileInfo(file.Path);
                    var fileTrackInfo = _metadataTagService.ReadTags(fileInfo) ?? new ParsedTrackInfo();

                    var localTrack = new LocalBook
                    {
                        ExistingFile = fileRootFolder != null,
                        FileTrackInfo = fileTrackInfo,
                        Path = file.Path,
                        Part = fileTrackInfo.TrackNumbers.Any() ? fileTrackInfo.TrackNumbers.First() : 1,
                        PartCount = importBookId.Count(),
                        Size = fileInfo.Length,
                        Modified = fileInfo.LastWriteTimeUtc,
                        Quality = file.Quality,
                        IndexerFlags = (IndexerFlags)file.IndexerFlags,
                        Author = author,
                        Book = book,
                        Edition = edition
                    };

                    var importDecision = new ImportDecision<LocalBook>(localTrack);
                    if (_rootFolderService.GetBestRootFolder(author.Path) == null)
                    {
                        _logger.Warn($"Destination author folder {author.Path} not in a Root Folder, skipping import");
                        importDecision.Reject(new Rejection($"Destination author folder {author.Path} is not in a Root Folder"));
                    }

                    bookImportDecisions.Add(importDecision);
                    fileCount += 1;
                }

                var downloadId = importBookId.Select(x => x.DownloadId).FirstOrDefault(x => x.IsNotNullOrWhiteSpace());
                if (downloadId.IsNullOrWhiteSpace())
                {
                    imported.AddRange(_importApprovedBooks.Import(bookImportDecisions, message.ReplaceExistingFiles, null, message.ImportMode));
                }
                else
                {
                    var trackedDownload = _trackedDownloadService.Find(downloadId);
                    var downloadClientItem = trackedDownload?.DownloadItem;

                    if (downloadClientItem == null)
                    {
                        _logger.Warn("Tracked download {0} was not available during manual import, importing without tracked download context", downloadId);
                    }

                    var importResults = _importApprovedBooks.Import(bookImportDecisions, message.ReplaceExistingFiles, downloadClientItem, message.ImportMode);

                    imported.AddRange(importResults);

                    if (downloadClientItem == null)
                    {
                        continue;
                    }

                    foreach (var importResult in importResults)
                    {
                        importedTrackedDownload.Add(new ManuallyImportedFile
                        {
                            TrackedDownload = trackedDownload,
                            ImportResult = importResult
                        });
                    }
                }
            }

            _logger.ProgressTrace("Manually imported {0} files", imported.Count);

            foreach (var groupedTrackedDownload in importedTrackedDownload.GroupBy(i => i.TrackedDownload.DownloadItem.DownloadId).ToList())
            {
                var trackedDownload = groupedTrackedDownload.First().TrackedDownload;

                if (trackedDownload.ImportItem != null && !trackedDownload.ImportItem.OutputPath.IsEmpty)
                {
                    var outputPath = trackedDownload.ImportItem.OutputPath.FullPath;

                    if (_diskProvider.FolderExists(outputPath))
                    {
                        if (_downloadedTracksImportService.ShouldDeleteFolder(_diskProvider.GetDirectoryInfo(outputPath)) &&
                            trackedDownload.DownloadItem.CanMoveFiles)
                        {
                            _diskProvider.DeleteFolder(outputPath, true);
                        }
                    }
                }

                var importedCount = groupedTrackedDownload.Select(c => c.ImportResult)
                    .Count(c => c.Result == ImportResultType.Imported);
                var downloadItemCount = Math.Max(1, trackedDownload.RemoteBook?.Books.Count ?? 1);
                var allItemsImported = importedCount >= downloadItemCount;

                if (allItemsImported)
                {
                    trackedDownload.State = TrackedDownloadState.Imported;
                    var authorId = groupedTrackedDownload
                        .Select(c => c.ImportResult.ImportDecision.Item.Author?.Id ?? 0)
                        .FirstOrDefault(id => id > 0);

                    if (authorId > 0)
                    {
                        _eventAggregator.PublishEvent(new DownloadCompletedEvent(trackedDownload, authorId));
                    }
                }
            }
        }
    }
}
