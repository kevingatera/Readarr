using System.Collections.Generic;
using System.IO;
using FizzWare.NBuilder;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Download;
using NzbDrone.Core.History;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Test.Qualities;

namespace NzbDrone.Core.Test.HistoryTests
{
    public class HistoryServiceFixture : CoreTest<HistoryService>
    {
        private QualityProfile _profile;
        private QualityProfile _profileCustom;

        [SetUp]
        public void Setup()
        {
            _profile = new QualityProfile
            {
                Cutoff = Quality.MP3.Id,
                Items = QualityFixture.GetDefaultQualities(),
            };

            _profileCustom = new QualityProfile
            {
                Cutoff = Quality.MP3.Id,
                Items = QualityFixture.GetDefaultQualities(Quality.MP3),
            };
        }

        [Test]
        public void should_use_file_name_for_source_title_if_scene_name_is_null()
        {
            var author = Builder<Author>.CreateNew().Build();
            var trackFile = Builder<BookFile>.CreateNew()
                .With(f => f.SceneName = null)
                .With(f => f.Author = author)
                .Build();

            var localTrack = new LocalBook
            {
                Author = author,
                Book = new Book(),
                Path = @"C:\Test\Unsorted\Author.01.Hymn.mp3"
            };

            var downloadClientItem = new DownloadClientItem
            {
                DownloadClientInfo = new DownloadClientItemClientInfo
                {
                    Protocol = DownloadProtocol.Usenet,
                    Id = 1,
                    Name = "sab"
                },
                DownloadId = "abcd"
            };

            Subject.Handle(new TrackImportedEvent(localTrack, trackFile, new List<BookFile>(), true, downloadClientItem));

            Mocker.GetMock<IHistoryRepository>()
                .Verify(v => v.Insert(It.Is<EntityHistory>(h => h.SourceTitle == Path.GetFileNameWithoutExtension(localTrack.Path))));
        }

        [Test]
        public void should_record_history_for_existing_file_imports()
        {
            var author = Builder<Author>.CreateNew().With(x => x.Id = 44).Build();
            var book = Builder<Book>.CreateNew().With(x => x.Id = 55).Build();
            var trackFile = Builder<BookFile>.CreateNew()
                .With(x => x.Id = 66)
                .With(x => x.Path = @"C:\Test\Books\Author - Book.m4b")
                .With(f => f.Author = author)
                .Build();

            var localTrack = new LocalBook
            {
                Author = author,
                Book = book,
                Path = @"C:\Test\Unsorted\Author - Book.m4b",
                Size = 1234,
                Quality = new QualityModel(Quality.M4B)
            };

            Subject.Handle(new TrackImportedEvent(localTrack, trackFile, new List<BookFile>(), false, null));

            Mocker.GetMock<IHistoryRepository>()
                .Verify(v => v.Insert(It.Is<EntityHistory>(h =>
                    h.EventType == EntityHistoryEventType.BookFileImported &&
                    h.AuthorId == author.Id &&
                    h.BookId == book.Id &&
                    h.Data["DroppedPath"] == localTrack.Path &&
                    h.Data["ImportedPath"] == trackFile.Path)), Times.Once());
        }
    }
}
