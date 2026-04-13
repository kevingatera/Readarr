using System.Collections.Generic;
using System.IO;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles
{
    [TestFixture]
    public class ImportApprovedTracksFixture : CoreTest<ImportApprovedBooks>
    {
        private List<ImportDecision<LocalBook>> _rejectedDecisions;
        private List<ImportDecision<LocalBook>> _approvedDecisions;

        private DownloadClientItem _downloadClientItem;
        private DownloadClientItemClientInfo _clientInfo;

        [SetUp]
        public void Setup()
        {
            _rejectedDecisions = new List<ImportDecision<LocalBook>>();
            _approvedDecisions = new List<ImportDecision<LocalBook>>();

            var author = Builder<Author>.CreateNew()
                                        .With(e => e.QualityProfile = new QualityProfile { Items = Qualities.QualityFixture.GetDefaultQualities() })
                                        .With(s => s.Path = @"C:\Test\Music\Alien Ant Farm".AsOsAgnostic())
                                        .Build();

            var book = Builder<Book>.CreateNew()
                .With(e => e.Author = author)
                .Build();

            var edition = Builder<Edition>.CreateNew()
                .With(e => e.Book = book)
                .With(e => e.Monitored = true)
                .Build();

            book.Editions = new List<Edition> { edition };

            var rootFolder = Builder<RootFolder>.CreateNew()
                .With(r => r.IsCalibreLibrary = false)
                .Build();

            _rejectedDecisions.Add(new ImportDecision<LocalBook>(new LocalBook(), new Rejection("Rejected!")));
            _rejectedDecisions.Add(new ImportDecision<LocalBook>(new LocalBook(), new Rejection("Rejected!")));
            _rejectedDecisions.Add(new ImportDecision<LocalBook>(new LocalBook(), new Rejection("Rejected!")));

            _approvedDecisions.Add(new ImportDecision<LocalBook>(
                                       new LocalBook
                                       {
                                           Author = author,
                                           Book = book,
                                           Edition = edition,
                                           Part = 1,
                                           Path = Path.Combine(author.Path, "Alien Ant Farm - 01 - Pilot.mp3"),
                                           Quality = new QualityModel(Quality.MP3),
                                           FileTrackInfo = new ParsedTrackInfo
                                           {
                                               ReleaseGroup = "DRONE"
                                           }
                                       }));

            Mocker.GetMock<IUpgradeMediaFiles>()
                  .Setup(s => s.UpgradeBookFile(It.IsAny<BookFile>(), It.IsAny<LocalBook>(), It.IsAny<bool>()))
                  .Returns(new BookFileMoveResult());

            _clientInfo = Builder<DownloadClientItemClientInfo>.CreateNew().Build();
            _downloadClientItem = Builder<DownloadClientItem>.CreateNew().With(x => x.DownloadClientInfo = _clientInfo).Build();

            Mocker.GetMock<IMediaFileService>()
                .Setup(s => s.GetFilesByBook(It.IsAny<int>()))
                .Returns(new List<BookFile>());

            Mocker.GetMock<IRootFolderService>()
                .Setup(s => s.GetBestRootFolder(It.IsAny<string>()))
                .Returns(rootFolder);

            Mocker.GetMock<IEditionService>()
                .Setup(s => s.SetMonitored(edition))
                .Returns(new List<Edition> { edition });
        }

        [Test]
        public void should_not_import_any_if_there_are_no_approved_decisions()
        {
            Subject.Import(_rejectedDecisions, false).Where(i => i.Result == ImportResultType.Imported).Should().BeEmpty();

            Mocker.GetMock<IMediaFileService>().Verify(v => v.Add(It.IsAny<BookFile>()), Times.Never());
        }

        [Test]
        public void should_import_each_approved()
        {
            Subject.Import(_approvedDecisions, false).Should().HaveCount(1);
        }

        [Test]
        public void should_reuse_existing_persisted_edition_when_import_decision_has_transient_edition()
        {
            var persistedEdition = Builder<Edition>.CreateNew()
                .With(x => x.Id = 12)
                .With(x => x.BookId = _approvedDecisions.First().Item.Book.Id)
                .With(x => x.ForeignEditionId = "existing-foreign-edition")
                .With(x => x.Monitored = true)
                .Build();

            var transientEdition = Builder<Edition>.CreateNew()
                .With(x => x.Id = 0)
                .With(x => x.BookId = _approvedDecisions.First().Item.Book.Id)
                .With(x => x.ForeignEditionId = persistedEdition.ForeignEditionId)
                .With(x => x.Monitored = false)
                .Build();

            var decision = _approvedDecisions.First();
            decision.Item.Edition = transientEdition;

            Mocker.GetMock<IEditionService>()
                .Setup(s => s.GetEditionByForeignEditionId(persistedEdition.ForeignEditionId))
                .Returns(persistedEdition);

            Mocker.GetMock<IEditionService>()
                .Setup(s => s.SetMonitored(persistedEdition))
                .Returns(new List<Edition> { persistedEdition });

            Subject.Import(new List<ImportDecision<LocalBook>> { decision }, false);

            decision.Item.Edition.Should().BeSameAs(persistedEdition);

            Mocker.GetMock<IEditionService>()
                .Verify(v => v.SetMonitored(persistedEdition), Times.Once());

            Mocker.GetMock<IMediaFileService>()
                .Verify(v => v.AddMany(It.Is<List<BookFile>>(files => files.Single().EditionId == persistedEdition.Id)), Times.Once());
        }

        [Test]
        public void should_resolve_book_edition_when_import_decision_has_no_edition()
        {
            var decision = _approvedDecisions.First();
            var book = decision.Item.Book;
            var existingEdition = decision.Item.Edition;

            book.ForeignEditionId = existingEdition.ForeignEditionId;
            decision.Item.Edition = null;

            Mocker.GetMock<IEditionService>()
                .Setup(s => s.GetEditionByForeignEditionId(existingEdition.ForeignEditionId))
                .Returns(existingEdition);

            Subject.Import(new List<ImportDecision<LocalBook>> { decision }, false);

            decision.Item.Edition.Should().BeSameAs(existingEdition);

            Mocker.GetMock<IEditionService>()
                .Verify(v => v.SetMonitored(existingEdition), Times.Once());

            Mocker.GetMock<IMediaFileService>()
                .Verify(v => v.AddMany(It.Is<List<BookFile>>(files => files.Single().EditionId == existingEdition.Id)), Times.Once());
        }

        [Test]
        public void should_reject_when_existing_persisted_edition_belongs_to_different_book()
        {
            var foreignEditionId = "cross-book-edition";
            var transientEdition = Builder<Edition>.CreateNew()
                .With(x => x.Id = 0)
                .With(x => x.BookId = _approvedDecisions.First().Item.Book.Id)
                .With(x => x.ForeignEditionId = foreignEditionId)
                .With(x => x.Monitored = false)
                .Build();

            var wrongBookEdition = Builder<Edition>.CreateNew()
                .With(x => x.Id = 77)
                .With(x => x.BookId = _approvedDecisions.First().Item.Book.Id + 999)
                .With(x => x.ForeignEditionId = foreignEditionId)
                .With(x => x.Monitored = true)
                .Build();

            var decision = _approvedDecisions.First();
            decision.Item.Edition = transientEdition;

            Mocker.GetMock<IEditionService>()
                .Setup(s => s.GetEditionByForeignEditionId(foreignEditionId))
                .Returns(wrongBookEdition);

            var result = Subject.Import(new List<ImportDecision<LocalBook>> { decision }, false);

            result.Should().ContainSingle();
            result.Single().Result.Should().Be(ImportResultType.Rejected);

            Mocker.GetMock<IEditionService>()
                .Verify(v => v.SetMonitored(It.IsAny<Edition>()), Times.Never());

            Mocker.GetMock<IMediaFileService>()
                .Verify(v => v.AddMany(It.IsAny<List<BookFile>>()), Times.Never());
        }

        [Test]
        public void should_only_import_approved()
        {
            var all = new List<ImportDecision<LocalBook>>();
            all.AddRange(_rejectedDecisions);
            all.AddRange(_approvedDecisions);

            var result = Subject.Import(all, false);

            result.Should().HaveCount(all.Count);
            result.Where(i => i.Result == ImportResultType.Imported).Should().HaveCount(_approvedDecisions.Count);
        }

        [Test]
        public void should_only_import_each_track_once()
        {
            var all = new List<ImportDecision<LocalBook>>();
            all.AddRange(_approvedDecisions);
            all.Add(new ImportDecision<LocalBook>(_approvedDecisions.First().Item));

            var result = Subject.Import(all, false);

            result.Where(i => i.Result == ImportResultType.Imported).Should().HaveCount(_approvedDecisions.Count);
        }

        [Test]
        public void should_move_new_downloads()
        {
            Subject.Import(new List<ImportDecision<LocalBook>> { _approvedDecisions.First() }, true);

            Mocker.GetMock<IUpgradeMediaFiles>()
                  .Verify(v => v.UpgradeBookFile(It.IsAny<BookFile>(), _approvedDecisions.First().Item, false),
                          Times.Once());
        }

        [Test]
        public void should_publish_TrackImportedEvent_for_new_downloads()
        {
            Subject.Import(new List<ImportDecision<LocalBook>> { _approvedDecisions.First() }, true);

            Mocker.GetMock<IEventAggregator>()
                .Verify(v => v.PublishEvent(It.IsAny<TrackImportedEvent>()), Times.Once());
        }

        [Test]
        public void should_not_move_existing_files()
        {
            var track = _approvedDecisions.First();
            track.Item.ExistingFile = true;
            Subject.Import(new List<ImportDecision<LocalBook>> { track }, false);

            Mocker.GetMock<IUpgradeMediaFiles>()
                  .Verify(v => v.UpgradeBookFile(It.IsAny<BookFile>(), _approvedDecisions.First().Item, false),
                          Times.Never());
        }

        [Test]
        public void should_import_higher_quality_files_first()
        {
            var lqDecision = _approvedDecisions.First();
            lqDecision.Item.Quality = new QualityModel(Quality.MOBI);
            lqDecision.Item.Size = 10.Megabytes();

            var hqDecision = new ImportDecision<LocalBook>(
                new LocalBook
                {
                    Author = lqDecision.Item.Author,
                    Book = lqDecision.Item.Book,
                    Edition = lqDecision.Item.Edition,
                    Part = 1,
                    Path = @"C:\Test\Music\Alien Ant Farm\Alien Ant Farm - 01 - Pilot.mp3".AsOsAgnostic(),
                    Quality = new QualityModel(Quality.AZW3),
                    Size = 1.Megabytes(),
                    FileTrackInfo = new ParsedTrackInfo
                    {
                        ReleaseGroup = "DRONE"
                    }
                });

            var all = new List<ImportDecision<LocalBook>>();
            all.Add(lqDecision);
            all.Add(hqDecision);

            var results = Subject.Import(all, false);

            results.Should().HaveCount(all.Count);
            results.Should().ContainSingle(d => d.Result == ImportResultType.Imported);
            results.Should().ContainSingle(d => d.Result == ImportResultType.Imported && d.ImportDecision.Item.Size == hqDecision.Item.Size);
        }

        [Test]
        public void should_import_larger_files_for_same_quality_first()
        {
            var fileDecision = _approvedDecisions.First();
            fileDecision.Item.Size = 1.Gigabytes();

            var sampleDecision = new ImportDecision<LocalBook>(
                new LocalBook
                {
                    Author = fileDecision.Item.Author,
                    Book = fileDecision.Item.Book,
                    Edition = fileDecision.Item.Edition,
                    Part = 1,
                    Path = @"C:\Test\Music\Alien Ant Farm\Alien Ant Farm - 01 - Pilot.mp3".AsOsAgnostic(),
                    Quality = new QualityModel(Quality.MP3),
                    Size = 80.Megabytes()
                });

            var all = new List<ImportDecision<LocalBook>>();
            all.Add(fileDecision);
            all.Add(sampleDecision);

            var results = Subject.Import(all, false);

            results.Should().HaveCount(all.Count);
            results.Should().ContainSingle(d => d.Result == ImportResultType.Imported);
            results.Should().ContainSingle(d => d.Result == ImportResultType.Imported && d.ImportDecision.Item.Size == fileDecision.Item.Size);
        }

        [Test]
        public void should_copy_when_cannot_move_files_downloads()
        {
            Subject.Import(new List<ImportDecision<LocalBook>> { _approvedDecisions.First() }, true, new DownloadClientItem { Title = "Alien.Ant.Farm-Truant", CanMoveFiles = false, DownloadClientInfo = _clientInfo });

            Mocker.GetMock<IUpgradeMediaFiles>()
                  .Verify(v => v.UpgradeBookFile(It.IsAny<BookFile>(), _approvedDecisions.First().Item, true), Times.Once());
        }

        [Test]
        public void should_use_override_importmode()
        {
            Subject.Import(new List<ImportDecision<LocalBook>> { _approvedDecisions.First() }, true, new DownloadClientItem { Title = "Alien.Ant.Farm-Truant", CanMoveFiles = false, DownloadClientInfo = _clientInfo }, ImportMode.Move);

            Mocker.GetMock<IUpgradeMediaFiles>()
                  .Verify(v => v.UpgradeBookFile(It.IsAny<BookFile>(), _approvedDecisions.First().Item, false), Times.Once());
        }

        [Test]
        public void should_delete_existing_trackfiles_with_the_same_path()
        {
            Mocker.GetMock<IMediaFileService>()
                .Setup(s => s.GetFileWithPath(It.IsAny<string>()))
                .Returns(Builder<BookFile>.CreateNew().Build());

            var track = _approvedDecisions.First();
            track.Item.ExistingFile = true;
            Subject.Import(new List<ImportDecision<LocalBook>> { track }, false);

            Mocker.GetMock<IMediaFileService>()
                .Verify(v => v.Delete(It.IsAny<BookFile>(), DeleteMediaFileReason.ManualOverride), Times.Once());
        }

        [Test]
        public void should_skip_automatic_existing_file_cross_book_relink()
        {
            var track = _approvedDecisions.First();
            track.Item.ExistingFile = true;
            track.Item.AllowSameFileMatch = false;
            track.Item.Edition.Id = 10;

            Mocker.GetMock<IMediaFileService>()
                .Setup(s => s.GetFileWithPath(It.IsAny<string>()))
                .Returns(Builder<BookFile>.CreateNew()
                    .With(x => x.Path = track.Item.Path)
                    .With(x => x.EditionId = 11)
                    .Build());

            var result = Subject.Import(new List<ImportDecision<LocalBook>> { track }, false, null, ImportMode.Auto);

            result.Should().ContainSingle();
            result.Single().Result.Should().Be(ImportResultType.Skipped);
            result.Single().Errors.Should().Contain("Automatic scan skipped: existing file is already linked to another book");

            Mocker.GetMock<IMediaFileService>()
                .Verify(v => v.Delete(It.IsAny<BookFile>(), DeleteMediaFileReason.ManualOverride), Times.Never());
        }

        [Test]
        public void should_allow_existing_file_cross_book_relink_for_same_file_override()
        {
            var track = _approvedDecisions.First();
            track.Item.ExistingFile = true;
            track.Item.AllowSameFileMatch = true;
            track.Item.Edition.Id = 10;

            Mocker.GetMock<IMediaFileService>()
                .Setup(s => s.GetFileWithPath(It.IsAny<string>()))
                .Returns(Builder<BookFile>.CreateNew()
                    .With(x => x.Path = track.Item.Path)
                    .With(x => x.EditionId = 11)
                    .Build());

            Subject.Import(new List<ImportDecision<LocalBook>> { track }, false, null, ImportMode.Auto);

            Mocker.GetMock<IMediaFileService>()
                .Verify(v => v.Delete(It.IsAny<BookFile>(), DeleteMediaFileReason.ManualOverride), Times.Once());
        }
    }
}
