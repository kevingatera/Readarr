using System;
using System.IO;
using FluentAssertions;
using FizzWare.NBuilder;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.TrackFileMovingServiceTests
{
    [TestFixture]
    public class MoveTrackFileFixture : CoreTest<BookFileMovingService>
    {
        private Author _author;
        private BookFile _trackFile;
        private LocalBook _localtrack;

        [SetUp]
        public void Setup()
        {
            _author = Builder<Author>.CreateNew()
                                     .With(s => s.Path = @"C:\Test\Music\Author".AsOsAgnostic())
                                     .Build();

            _trackFile = Builder<BookFile>.CreateNew()
                                               .With(f => f.Path = null)
                                               .With(f => f.Path = Path.Combine(_author.Path, @"Book\File.mp3"))
                                               .Build();

            _localtrack = Builder<LocalBook>.CreateNew()
                                                 .With(l => l.Author = _author)
                                                 .With(l => l.Book = Builder<Book>.CreateNew().Build())
                                                 .Build();

            Mocker.GetMock<IBuildFileNames>()
                  .Setup(s => s.BuildBookFileName(It.IsAny<Author>(), It.IsAny<Edition>(), It.IsAny<BookFile>(), null, null))
                  .Returns("File Name");

            Mocker.GetMock<IBuildFileNames>()
                  .Setup(s => s.BuildBookFilePath(It.IsAny<Author>(), It.IsAny<Edition>(), It.IsAny<string>(), It.IsAny<string>()))
                  .Returns(@"C:\Test\Music\Author\Book\File Name.mp3".AsOsAgnostic());

            Mocker.GetMock<IBuildFileNames>()
                  .Setup(s => s.BuildBookPath(It.IsAny<Author>()))
                  .Returns(@"C:\Test\Music\Author\Book".AsOsAgnostic());

            var rootFolder = @"C:\Test\Music\".AsOsAgnostic();
            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FolderExists(rootFolder))
                  .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FileExists(It.IsAny<string>()))
                  .Returns(true);
        }

        [Test]
        public void should_catch_UnauthorizedAccessException_during_folder_inheritance()
        {
            WindowsOnly();

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.InheritFolderPermissions(It.IsAny<string>()))
                  .Throws<UnauthorizedAccessException>();

            Subject.MoveBookFile(_trackFile, _localtrack);
        }

        [Test]
        public void should_catch_InvalidOperationException_during_folder_inheritance()
        {
            WindowsOnly();

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.InheritFolderPermissions(It.IsAny<string>()))
                  .Throws<InvalidOperationException>();

            Subject.MoveBookFile(_trackFile, _localtrack);
        }

        [Test]
        public void should_notify_on_author_folder_creation()
        {
            Subject.MoveBookFile(_trackFile, _localtrack);

            Mocker.GetMock<IEventAggregator>()
                  .Verify(s => s.PublishEvent<TrackFolderCreatedEvent>(It.Is<TrackFolderCreatedEvent>(p =>
                      p.AuthorFolder.IsNotNullOrWhiteSpace())), Times.Once());
        }

        [Test]
        public void should_notify_on_book_folder_creation()
        {
            Subject.MoveBookFile(_trackFile, _localtrack);

            Mocker.GetMock<IEventAggregator>()
                  .Verify(s => s.PublishEvent<TrackFolderCreatedEvent>(It.Is<TrackFolderCreatedEvent>(p =>
                      p.BookFolder.IsNotNullOrWhiteSpace())), Times.Once());
        }

        [Test]
        public void should_not_notify_if_author_folder_already_exists()
        {
            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FolderExists(_author.Path))
                  .Returns(true);

            Subject.MoveBookFile(_trackFile, _localtrack);

            Mocker.GetMock<IEventAggregator>()
                  .Verify(s => s.PublishEvent<TrackFolderCreatedEvent>(It.Is<TrackFolderCreatedEvent>(p =>
                      p.AuthorFolder.IsNotNullOrWhiteSpace())), Times.Never());
        }

        [Test]
        public void should_adopt_existing_destination_when_destination_file_is_untracked()
        {
            var destinationPath = @"C:\Test\Music\Author\Book\File Name.mp3".AsOsAgnostic();

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FileExists(destinationPath))
                  .Returns(true);

            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.GetFileWithPath(destinationPath))
                  .Returns((BookFile)null);

            var result = Subject.MoveBookFile(_trackFile, _localtrack);

            result.Path.Should().Be(destinationPath);

            Mocker.GetMock<IDiskTransferService>()
                  .Verify(v => v.TransferFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TransferMode>(), It.IsAny<bool>()), Times.Never());

            Mocker.GetMock<IUpdateBookFileService>()
                  .Verify(v => v.ChangeFileDateForFile(result, _author, _localtrack.Book), Times.Once());
        }
    }
}
