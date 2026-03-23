using System;
using System.Collections.Generic;
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
                .With(x => x.Path = @"C:\Audiobooks\Jason Anspach".AsOsAgnostic())
                .Build();

            _book = Builder<Book>.CreateNew()
                .With(x => x.Id = 15402)
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

            Mocker.GetMock<IMetadataTagService>()
                .Setup(x => x.ReadTags(_fileInfo))
                .Returns(new ParsedTrackInfo());

            Mocker.GetMock<IImportApprovedBooks>()
                .Setup(x => x.Import(It.IsAny<List<ImportDecision<LocalBook>>>(), It.IsAny<bool>(), It.IsAny<DownloadClientItem>(), It.IsAny<ImportMode>()))
                .Returns((List<ImportDecision<LocalBook>> decisions, bool replaceExisting, DownloadClientItem downloadClientItem, ImportMode importMode) =>
                    decisions.Select(d => new ImportResult(d)).ToList());
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
    }
}
