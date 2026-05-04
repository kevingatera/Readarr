using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Books;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.MediaFiles.BookImport.Manual;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles
{
    [TestFixture]
    public class ManualImportServiceFixture : CoreTest<ManualImportService>
    {
        private Author _author;
        private Book _book;
        private Edition _edition;
        private RootFolder _rootFolder;
        private IFileInfo _fileInfo;
        private string _filePath;

        [SetUp]
        public void Setup()
        {
            _filePath = @"C:\Downloads\Galaxy's Edge\Takeover.m4b".AsOsAgnostic();

            _author = Builder<Author>.CreateNew()
                .With(x => x.Id = 14)
                .With(x => x.AuthorMetadataId = 14)
                .With(x => x.Path = @"C:\Audiobooks\Jason Anspach".AsOsAgnostic())
                .Build();

            _book = Builder<Book>.CreateNew()
                .With(x => x.Id = 15402)
                .With(x => x.Title = "Takeover")
                .With(x => x.Author = _author)
                .With(x => x.ForeignBookId = "book-15402")
                .Build();

            _edition = Builder<Edition>.CreateNew()
                .With(x => x.Id = 91)
                .With(x => x.BookId = _book.Id)
                .With(x => x.Book = _book)
                .With(x => x.ForeignEditionId = "edition-15402")
                .Build();

            _rootFolder = Builder<RootFolder>.CreateNew().Build();

            var modified = DateTime.UtcNow;
            var fileMock = new Mock<IFileInfo>();
            fileMock.SetupGet(x => x.Length).Returns(1234);
            fileMock.SetupGet(x => x.LastWriteTimeUtc).Returns(modified);
            fileMock.SetupGet(x => x.FullName).Returns(_filePath);
            _fileInfo = fileMock.Object;

            Mocker.GetMock<IAuthorService>()
                .Setup(x => x.GetAuthor(_author.Id))
                .Returns(_author);

            Mocker.GetMock<IBookService>()
                .Setup(x => x.GetBook(_book.Id))
                .Returns(_book);

            Mocker.GetMock<IEditionService>()
                .Setup(x => x.GetEditionByForeignEditionId(_edition.ForeignEditionId))
                .Returns(_edition);

            Mocker.GetMock<IRootFolderService>()
                .Setup(x => x.GetBestRootFolder(_author.Path))
                .Returns(_rootFolder);

            Mocker.GetMock<IRootFolderService>()
                .Setup(x => x.GetBestRootFolder(_filePath))
                .Returns((RootFolder)null);

            Mocker.GetMock<IDiskProvider>()
                .Setup(x => x.GetFileInfo(_filePath))
                .Returns(_fileInfo);

            Mocker.GetMock<IDiskProvider>()
                .Setup(x => x.GetFileSize(_filePath))
                .Returns(1234);

            Mocker.GetMock<IMetadataTagService>()
                .Setup(x => x.ReadTags(_fileInfo))
                .Returns(new ParsedTrackInfo());

            Mocker.GetMock<IImportApprovedBooks>()
                .Setup(x => x.Import(It.IsAny<List<ImportDecision<LocalBook>>>(), It.IsAny<bool>(), It.IsAny<DownloadClientItem>(), It.IsAny<ImportMode>()))
                .Returns((List<ImportDecision<LocalBook>> decisions, bool replaceExisting, DownloadClientItem downloadClientItem, ImportMode importMode) =>
                    decisions.Select(d => new ImportResult(d)).ToList());
        }

        [Test]
        public void should_resolve_book_override_from_consistent_embedded_title_for_folder_import()
        {
            var folder = Path.GetDirectoryName(_filePath);
            NzbDrone.Core.MediaFiles.BookImport.IdentificationOverrides capturedOverride = null;

            Mocker.GetMock<IDiskProvider>()
                .Setup(x => x.FolderExists(folder))
                .Returns(true);

            Mocker.GetMock<IDiskScanService>()
                .Setup(x => x.GetBookFiles(folder, true))
                .Returns(new[] { _fileInfo });

            Mocker.GetMock<IMetadataTagService>()
                .Setup(x => x.ReadTags(_fileInfo))
                .Returns(new ParsedTrackInfo
                {
                    Title = "Resonant Son: Resonant Son, Book 1",
                    BookTitle = "Resonant Son: Resonant Son, Book 1"
                });

            Mocker.GetMock<IBookService>()
                .Setup(x => x.FindByTitle(_author.AuthorMetadataId, "Resonant Son: Resonant Son, Book 1"))
                .Returns((Book)null);

            Mocker.GetMock<IBookService>()
                .Setup(x => x.FindByTitleInexact(_author.AuthorMetadataId, "Resonant Son: Resonant Son, Book 1"))
                .Returns(_book);

            Mocker.GetMock<IEditionService>()
                .Setup(x => x.FindByTitle(_author.AuthorMetadataId, "Resonant Son: Resonant Son, Book 1"))
                .Returns((Edition)null);

            Mocker.GetMock<IEditionService>()
                .Setup(x => x.FindByTitleInexact(_author.AuthorMetadataId, "Resonant Son: Resonant Son, Book 1"))
                .Returns(_edition);

            Mocker.GetMock<IMakeImportDecision>()
                .Setup(x => x.GetImportDecisions(It.IsAny<List<IFileInfo>>(),
                                                It.IsAny<NzbDrone.Core.MediaFiles.BookImport.IdentificationOverrides>(),
                                                It.IsAny<ImportDecisionMakerInfo>(),
                                                It.IsAny<ImportDecisionMakerConfig>()))
                .Callback<List<IFileInfo>, NzbDrone.Core.MediaFiles.BookImport.IdentificationOverrides, ImportDecisionMakerInfo, ImportDecisionMakerConfig>((_, id, _, _) => capturedOverride = id)
                .Returns(new List<ImportDecision<LocalBook>>
                {
                    new ImportDecision<LocalBook>(new LocalBook
                    {
                        Path = _filePath,
                        Author = _author,
                        Book = _book,
                        Edition = _edition,
                        Quality = new QualityModel(Quality.M4B),
                        FileTrackInfo = new ParsedTrackInfo()
                    })
                });

            var result = Subject.GetMediaFiles(folder, null, _author, FilterFilesType.None, false);

            result.Should().HaveCount(1);
            capturedOverride.Should().NotBeNull();
            capturedOverride.Author.Should().Be(_author);
            capturedOverride.Book.Should().Be(_book);
            capturedOverride.Edition.Should().Be(_edition);
        }

        [Test]
        public void should_import_without_tracked_download_when_download_id_is_stale()
        {
            var command = new ManualImportCommand
            {
                Files = new List<ManualImportFile>
                {
                    new ManualImportFile
                    {
                        Path = _filePath,
                        AuthorId = _author.Id,
                        BookId = _book.Id,
                        ForeignEditionId = _edition.ForeignEditionId,
                        Quality = new QualityModel(Quality.M4B),
                        DownloadId = "stale-download-id"
                    }
                }
            };

            Mocker.GetMock<ITrackedDownloadService>()
                .Setup(x => x.Find("stale-download-id"))
                .Returns((TrackedDownload)null);

            Action act = () => Subject.Execute(command);

            act.Should().NotThrow();

            Mocker.GetMock<IImportApprovedBooks>()
                .Verify(x => x.Import(
                    It.Is<List<ImportDecision<LocalBook>>>(d => d.Count == 1 &&
                                                               d[0].Item.Author == _author &&
                                                               d[0].Item.Book == _book &&
                                                               d[0].Item.Edition == _edition),
                    false,
                    null,
                    ImportMode.Auto),
                    Times.Once());

            Mocker.GetMock<IEventAggregator>()
                .Verify(x => x.PublishEvent(It.IsAny<DownloadCompletedEvent>()), Times.Never());
        }

        [Test]
        public void should_fallback_to_path_scan_when_get_media_files_download_id_is_stale()
        {
            var folder = Path.GetDirectoryName(_filePath);

            Mocker.GetMock<ITrackedDownloadService>()
                .Setup(x => x.Find("stale-download-id"))
                .Returns((TrackedDownload)null);

            Mocker.GetMock<IDiskProvider>()
                .Setup(x => x.FolderExists(folder))
                .Returns(true);

            Mocker.GetMock<IDiskScanService>()
                .Setup(x => x.GetBookFiles(folder, true))
                .Returns(new[] { _fileInfo });

            Mocker.GetMock<IMakeImportDecision>()
                .Setup(x => x.GetImportDecisions(It.IsAny<List<IFileInfo>>(),
                                                It.IsAny<NzbDrone.Core.MediaFiles.BookImport.IdentificationOverrides>(),
                                                It.IsAny<ImportDecisionMakerInfo>(),
                                                It.IsAny<ImportDecisionMakerConfig>()))
                .Returns(new List<ImportDecision<LocalBook>>
                {
                    new ImportDecision<LocalBook>(new LocalBook
                    {
                        Path = _filePath,
                        Author = _author,
                        Book = _book,
                        Edition = _edition,
                        Quality = new QualityModel(Quality.M4B),
                        FileTrackInfo = new ParsedTrackInfo()
                    })
                });

            var result = Subject.GetMediaFiles(folder, "stale-download-id", _author, FilterFilesType.None, false);

            result.Should().HaveCount(1);
            result[0].Path.Should().Be(_filePath);
            result[0].DownloadId.Should().BeNullOrEmpty();
        }

        [Test]
        public void should_ignore_null_books_when_resolving_manual_import_overrides()
        {
            var folder = Path.GetDirectoryName(_filePath);
            NzbDrone.Core.MediaFiles.BookImport.IdentificationOverrides capturedOverride = null;

            Mocker.GetMock<IDiskProvider>()
                .Setup(x => x.FolderExists(folder))
                .Returns(true);

            Mocker.GetMock<IDiskScanService>()
                .Setup(x => x.GetBookFiles(folder, true))
                .Returns(new[] { _fileInfo });

            Mocker.GetMock<IMetadataTagService>()
                .Setup(x => x.ReadTags(_fileInfo))
                .Returns(new ParsedTrackInfo
                {
                    Title = "Takeover",
                    BookTitle = "Takeover"
                });

            Mocker.GetMock<IBookService>()
                .Setup(x => x.GetBooksByAuthorMetadataId(_author.AuthorMetadataId))
                .Returns(new List<Book> { null, _book });

            Mocker.GetMock<IEditionService>()
                .Setup(x => x.GetEditionsByBook(_book.Id))
                .Returns(new List<Edition> { null, _edition });

            Mocker.GetMock<IMakeImportDecision>()
                .Setup(x => x.GetImportDecisions(It.IsAny<List<IFileInfo>>(),
                                                It.IsAny<NzbDrone.Core.MediaFiles.BookImport.IdentificationOverrides>(),
                                                It.IsAny<ImportDecisionMakerInfo>(),
                                                It.IsAny<ImportDecisionMakerConfig>()))
                .Callback<List<IFileInfo>, NzbDrone.Core.MediaFiles.BookImport.IdentificationOverrides, ImportDecisionMakerInfo, ImportDecisionMakerConfig>((_, id, _, _) => capturedOverride = id)
                .Returns(new List<ImportDecision<LocalBook>>
                {
                    new ImportDecision<LocalBook>(new LocalBook
                    {
                        Path = _filePath,
                        Author = _author,
                        Book = _book,
                        Edition = _edition,
                        Quality = new QualityModel(Quality.M4B),
                        FileTrackInfo = new ParsedTrackInfo()
                    })
                });

            Action act = () => Subject.GetMediaFiles(folder, null, _author, FilterFilesType.None, false);

            act.Should().NotThrow();
            capturedOverride.Should().NotBeNull();
            capturedOverride.Book.Should().Be(_book);
            capturedOverride.Edition.Should().Be(_edition);
        }

        [Test]
        public void should_resolve_book_override_from_normalized_embedded_title_variants_for_folder_import()
        {
            var folder = Path.GetDirectoryName(_filePath);
            var matchedBook = Builder<Book>.CreateNew()
                .With(x => x.Id = 8393)
                .With(x => x.Title = "Mavericks")
                .With(x => x.Author = _author)
                .Build();
            var otherBook = Builder<Book>.CreateNew()
                .With(x => x.Id = 8384)
                .With(x => x.Title = "Zero Hour")
                .With(x => x.Author = _author)
                .Build();
            var matchedEdition = Builder<Edition>.CreateNew()
                .With(x => x.Id = 83930)
                .With(x => x.Title = "Mavericks")
                .With(x => x.BookId = matchedBook.Id)
                .With(x => x.Book = matchedBook)
                .Build();

            NzbDrone.Core.MediaFiles.BookImport.IdentificationOverrides capturedOverride = null;

            Mocker.GetMock<IDiskProvider>()
                .Setup(x => x.FolderExists(folder))
                .Returns(true);

            Mocker.GetMock<IDiskScanService>()
                .Setup(x => x.GetBookFiles(folder, true))
                .Returns(new[] { _fileInfo });

            Mocker.GetMock<IMetadataTagService>()
                .Setup(x => x.ReadTags(_fileInfo))
                .Returns(new ParsedTrackInfo
                {
                    Title = "Mavericks: Expeditionary Force, Book 6 (Unabridged)",
                    BookTitle = "Mavericks (Unabridged)"
                });

            Mocker.GetMock<IBookService>()
                .Setup(x => x.GetBooksByAuthorMetadataId(_author.AuthorMetadataId))
                .Returns(new List<Book> { matchedBook, otherBook });

            Mocker.GetMock<IEditionService>()
                .Setup(x => x.GetEditionsByBook(matchedBook.Id))
                .Returns(new List<Edition> { matchedEdition });

            Mocker.GetMock<IMakeImportDecision>()
                .Setup(x => x.GetImportDecisions(It.IsAny<List<IFileInfo>>(),
                                                It.IsAny<NzbDrone.Core.MediaFiles.BookImport.IdentificationOverrides>(),
                                                It.IsAny<ImportDecisionMakerInfo>(),
                                                It.IsAny<ImportDecisionMakerConfig>()))
                .Callback<List<IFileInfo>, NzbDrone.Core.MediaFiles.BookImport.IdentificationOverrides, ImportDecisionMakerInfo, ImportDecisionMakerConfig>((_, id, _, _) => capturedOverride = id)
                .Returns(new List<ImportDecision<LocalBook>>
                {
                    new ImportDecision<LocalBook>(new LocalBook
                    {
                        Path = _filePath,
                        Author = _author,
                        Book = matchedBook,
                        Edition = matchedEdition,
                        Quality = new QualityModel(Quality.M4B),
                        FileTrackInfo = new ParsedTrackInfo()
                    })
                });

            var result = Subject.GetMediaFiles(folder, null, _author, FilterFilesType.None, false);

            result.Should().HaveCount(1);
            capturedOverride.Should().NotBeNull();
            capturedOverride.Book.Should().Be(matchedBook);
            capturedOverride.Edition.Should().Be(matchedEdition);
        }

        [Test]
        public void should_use_identification_overrides_for_single_file_manual_import()
        {
            NzbDrone.Core.MediaFiles.BookImport.IdentificationOverrides capturedOverride = null;
            var matchedBook = Builder<Book>.CreateNew()
                .With(x => x.Id = 4617)
                .With(x => x.Title = "Callsign: Valkyrie")
                .With(x => x.Author = _author)
                .Build();
            var wrongBook = Builder<Book>.CreateNew()
                .With(x => x.Id = 113)
                .With(x => x.Title = "Kill Team")
                .With(x => x.Author = _author)
                .Build();
            var matchedEdition = Builder<Edition>.CreateNew()
                .With(x => x.Id = 46170)
                .With(x => x.Title = "Callsign: Valkyrie")
                .With(x => x.BookId = matchedBook.Id)
                .With(x => x.Book = matchedBook)
                .Build();

            Mocker.GetMock<IDiskProvider>()
                .Setup(x => x.FolderExists(_filePath))
                .Returns(false);

            Mocker.GetMock<IDiskProvider>()
                .Setup(x => x.FileExists(_filePath))
                .Returns(true);

            Mocker.GetMock<IMetadataTagService>()
                .Setup(x => x.ReadTags(_fileInfo))
                .Returns(new ParsedTrackInfo
                {
                    Title = "Callsign Valkyrie: Order of the Centurion, Book 6 (Unabridged)",
                    BookTitle = "Callsign Valkyrie: Order of the Centurion, Book 6 (Unabridged)"
                });

            Mocker.GetMock<IBookService>()
                .Setup(x => x.GetBooksByAuthorMetadataId(_author.AuthorMetadataId))
                .Returns(new List<Book> { matchedBook, wrongBook });

            Mocker.GetMock<IEditionService>()
                .Setup(x => x.GetEditionsByBook(matchedBook.Id))
                .Returns(new List<Edition> { matchedEdition });

            Mocker.GetMock<IMakeImportDecision>()
                .Setup(x => x.GetImportDecisions(It.IsAny<List<IFileInfo>>(),
                                                It.IsAny<NzbDrone.Core.MediaFiles.BookImport.IdentificationOverrides>(),
                                                It.IsAny<ImportDecisionMakerInfo>(),
                                                It.IsAny<ImportDecisionMakerConfig>()))
                .Callback<List<IFileInfo>, NzbDrone.Core.MediaFiles.BookImport.IdentificationOverrides, ImportDecisionMakerInfo, ImportDecisionMakerConfig>((_, id, _, _) => capturedOverride = id)
                .Returns(new List<ImportDecision<LocalBook>>
                {
                    new ImportDecision<LocalBook>(new LocalBook
                    {
                        Path = _filePath,
                        Author = _author,
                        Book = matchedBook,
                        Edition = matchedEdition,
                        Quality = new QualityModel(Quality.M4B),
                        FileTrackInfo = new ParsedTrackInfo()
                    })
                });

            var result = Subject.GetMediaFiles(_filePath, null, _author, FilterFilesType.None, false);

            result.Should().HaveCount(1);
            capturedOverride.Should().NotBeNull();
            capturedOverride.Book.Should().Be(matchedBook);
            capturedOverride.Edition.Should().Be(matchedEdition);
        }

        [Test]
        public void should_use_tracked_download_author_context_for_single_file_manual_import()
        {
            NzbDrone.Core.MediaFiles.BookImport.IdentificationOverrides capturedOverride = null;
            ImportDecisionMakerInfo capturedInfo = null;
            var downloadItem = new DownloadClientItem
            {
                DownloadId = "tracked-single-file-id",
                OutputPath = new OsPath(_filePath)
            };
            var trackedDownload = new TrackedDownload
            {
                DownloadItem = downloadItem,
                ImportItem = downloadItem,
                RemoteBook = new RemoteBook
                {
                    Author = _author,
                    Books = new List<Book> { _book }
                }
            };

            Mocker.GetMock<ITrackedDownloadService>()
                .Setup(x => x.Find(downloadItem.DownloadId))
                .Returns(trackedDownload);

            Mocker.GetMock<IDiskProvider>()
                .Setup(x => x.FolderExists(_filePath))
                .Returns(false);

            Mocker.GetMock<IDiskProvider>()
                .Setup(x => x.FileExists(_filePath))
                .Returns(true);

            Mocker.GetMock<IMakeImportDecision>()
                .Setup(x => x.GetImportDecisions(It.IsAny<List<IFileInfo>>(),
                                                It.IsAny<NzbDrone.Core.MediaFiles.BookImport.IdentificationOverrides>(),
                                                It.IsAny<ImportDecisionMakerInfo>(),
                                                It.IsAny<ImportDecisionMakerConfig>()))
                .Callback<List<IFileInfo>, NzbDrone.Core.MediaFiles.BookImport.IdentificationOverrides, ImportDecisionMakerInfo, ImportDecisionMakerConfig>((_, id, info, _) =>
                {
                    capturedOverride = id;
                    capturedInfo = info;
                })
                .Returns(new List<ImportDecision<LocalBook>>
                {
                    new ImportDecision<LocalBook>(new LocalBook
                    {
                        Path = _filePath,
                        Author = _author,
                        Book = _book,
                        Edition = _edition,
                        Quality = new QualityModel(Quality.M4B),
                        FileTrackInfo = new ParsedTrackInfo()
                    })
                });

            var result = Subject.GetMediaFiles(_filePath, downloadItem.DownloadId, null, FilterFilesType.None, false);

            result.Should().HaveCount(1);
            capturedOverride.Should().NotBeNull();
            capturedOverride.Author.Should().Be(_author);
            capturedInfo.Should().NotBeNull();
            capturedInfo.DownloadClientItem.Should().Be(downloadItem);
        }

        [Test]
        public void should_prefer_longer_exact_title_over_series_base_title_for_folder_override()
        {
            var folder = Path.GetDirectoryName(_filePath);
            NzbDrone.Core.MediaFiles.BookImport.IdentificationOverrides capturedOverride = null;

            var matchedBook = Builder<Book>.CreateNew()
                .With(x => x.Id = 15407)
                .With(x => x.Title = "Tournament of Champions")
                .With(x => x.Author = _author)
                .Build();
            var wrongBook = Builder<Book>.CreateNew()
                .With(x => x.Id = 515)
                .With(x => x.Title = "King's League")
                .With(x => x.Author = _author)
                .Build();
            var matchedEdition = Builder<Edition>.CreateNew()
                .With(x => x.Id = 154070)
                .With(x => x.Title = "Tournament of Champions")
                .With(x => x.BookId = matchedBook.Id)
                .With(x => x.Book = matchedBook)
                .Build();

            Mocker.GetMock<IDiskProvider>()
                .Setup(x => x.FolderExists(folder))
                .Returns(true);

            Mocker.GetMock<IDiskScanService>()
                .Setup(x => x.GetBookFiles(folder, true))
                .Returns(new[] { _fileInfo });

            Mocker.GetMock<IMetadataTagService>()
                .Setup(x => x.ReadTags(_fileInfo))
                .Returns(new ParsedTrackInfo
                {
                    BookTitle = "Tournament of Champions: An Epic Lit RPG Adventure (King's League, Book 6)"
                });

            Mocker.GetMock<IBookService>()
                .Setup(x => x.GetBooksByAuthorMetadataId(_author.AuthorMetadataId))
                .Returns(new List<Book> { matchedBook, wrongBook });

            Mocker.GetMock<IEditionService>()
                .Setup(x => x.GetEditionsByBook(matchedBook.Id))
                .Returns(new List<Edition> { matchedEdition });

            Mocker.GetMock<IMakeImportDecision>()
                .Setup(x => x.GetImportDecisions(It.IsAny<List<IFileInfo>>(),
                                                It.IsAny<NzbDrone.Core.MediaFiles.BookImport.IdentificationOverrides>(),
                                                It.IsAny<ImportDecisionMakerInfo>(),
                                                It.IsAny<ImportDecisionMakerConfig>()))
                .Callback<List<IFileInfo>, NzbDrone.Core.MediaFiles.BookImport.IdentificationOverrides, ImportDecisionMakerInfo, ImportDecisionMakerConfig>((_, id, _, _) => capturedOverride = id)
                .Returns(new List<ImportDecision<LocalBook>>
                {
                    new ImportDecision<LocalBook>(new LocalBook
                    {
                        Path = _filePath,
                        Author = _author,
                        Book = matchedBook,
                        Edition = matchedEdition,
                        Quality = new QualityModel(Quality.M4B),
                        FileTrackInfo = new ParsedTrackInfo()
                    })
                });

            var result = Subject.GetMediaFiles(folder, null, _author, FilterFilesType.None, false);

            result.Should().HaveCount(1);
            capturedOverride.Should().NotBeNull();
            capturedOverride.Book.Should().Be(matchedBook);
            capturedOverride.Edition.Should().Be(matchedEdition);
        }

        [Test]
        public void should_complete_tracked_import_when_import_item_is_missing()
        {
            var trackedDownload = new TrackedDownload
            {
                DownloadItem = new DownloadClientItem
                {
                    DownloadId = "tracked-download-id",
                    CanMoveFiles = true
                },
                RemoteBook = new RemoteBook
                {
                    Books = new List<Book> { _book }
                }
            };

            var command = new ManualImportCommand
            {
                Files = new List<ManualImportFile>
                {
                    new ManualImportFile
                    {
                        Path = _filePath,
                        AuthorId = _author.Id,
                        BookId = _book.Id,
                        ForeignEditionId = _edition.ForeignEditionId,
                        Quality = new QualityModel(Quality.M4B),
                        DownloadId = trackedDownload.DownloadItem.DownloadId
                    }
                }
            };

            Mocker.GetMock<ITrackedDownloadService>()
                .Setup(x => x.Find(trackedDownload.DownloadItem.DownloadId))
                .Returns(trackedDownload);

            Action act = () => Subject.Execute(command);

            act.Should().NotThrow();
            trackedDownload.State.Should().Be(TrackedDownloadState.Imported);

            Mocker.GetMock<IImportApprovedBooks>()
                .Verify(x => x.Import(It.IsAny<List<ImportDecision<LocalBook>>>(), false, trackedDownload.DownloadItem, ImportMode.Auto), Times.Once());

            Mocker.GetMock<IEventAggregator>()
                .Verify(x => x.PublishEvent(It.IsAny<DownloadCompletedEvent>()), Times.Once());

            Mocker.GetMock<IDiskProvider>()
                .Verify(x => x.DeleteFolder(It.IsAny<string>(), true), Times.Never());
        }

        [Test]
        public void should_resolve_tracked_download_by_file_path_when_manual_import_download_id_missing()
        {
            var trackedDownload = new TrackedDownload
            {
                DownloadItem = new DownloadClientItem
                {
                    DownloadId = "tracked-by-path-id",
                    OutputPath = new OsPath(Path.GetDirectoryName(_filePath)),
                    CanMoveFiles = true
                },
                RemoteBook = new RemoteBook
                {
                    Books = new List<Book> { _book }
                }
            };

            var command = new ManualImportCommand
            {
                Files = new List<ManualImportFile>
                {
                    new ManualImportFile
                    {
                        Path = _filePath,
                        AuthorId = _author.Id,
                        BookId = _book.Id,
                        ForeignEditionId = _edition.ForeignEditionId,
                        Quality = new QualityModel(Quality.M4B),
                        DownloadId = null
                    }
                }
            };

            Mocker.GetMock<ITrackedDownloadService>()
                .Setup(x => x.GetTrackedDownloads())
                .Returns(new List<TrackedDownload> { trackedDownload });

            Action act = () => Subject.Execute(command);

            act.Should().NotThrow();
            trackedDownload.State.Should().Be(TrackedDownloadState.Imported);

            Mocker.GetMock<IImportApprovedBooks>()
                .Verify(x => x.Import(It.IsAny<List<ImportDecision<LocalBook>>>(), false, trackedDownload.DownloadItem, ImportMode.Auto), Times.Once());

            Mocker.GetMock<IEventAggregator>()
                .Verify(x => x.PublishEvent(It.IsAny<DownloadCompletedEvent>()), Times.Once());
        }
    }
}
