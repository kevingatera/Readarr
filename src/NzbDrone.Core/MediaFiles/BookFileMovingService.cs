using System;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Crypto;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnsureThat;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles
{
    public interface IMoveBookFiles
    {
        BookFile MoveBookFile(BookFile bookFile, Author author);
        BookFile MoveBookFile(BookFile bookFile, LocalBook localBook);
        BookFile CopyBookFile(BookFile bookFile, LocalBook localBook);
    }

    public class BookFileMovingService : IMoveBookFiles
    {
        private readonly IEditionService _editionService;
        private readonly IMediaFileService _mediaFileService;
        private readonly IUpdateBookFileService _updateBookFileService;
        private readonly IBuildFileNames _buildFileNames;
        private readonly IDiskTransferService _diskTransferService;
        private readonly IDiskProvider _diskProvider;
        private readonly IRootFolderWatchingService _rootFolderWatchingService;
        private readonly IMediaFileAttributeService _mediaFileAttributeService;
        private readonly IHashProvider _hashProvider;
        private readonly IEventAggregator _eventAggregator;
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        public BookFileMovingService(IEditionService editionService,
                                      IMediaFileService mediaFileService,
                                      IUpdateBookFileService updateBookFileService,
                                      IBuildFileNames buildFileNames,
                                      IDiskTransferService diskTransferService,
                                      IDiskProvider diskProvider,
                                      IRootFolderWatchingService rootFolderWatchingService,
                                      IMediaFileAttributeService mediaFileAttributeService,
                                      IHashProvider hashProvider,
                                      IEventAggregator eventAggregator,
                                      IConfigService configService,
                                      Logger logger)
        {
            _editionService = editionService;
            _mediaFileService = mediaFileService;
            _updateBookFileService = updateBookFileService;
            _buildFileNames = buildFileNames;
            _diskTransferService = diskTransferService;
            _diskProvider = diskProvider;
            _rootFolderWatchingService = rootFolderWatchingService;
            _mediaFileAttributeService = mediaFileAttributeService;
            _hashProvider = hashProvider;
            _eventAggregator = eventAggregator;
            _configService = configService;
            _logger = logger;
        }

        public BookFile MoveBookFile(BookFile bookFile, Author author)
        {
            var edition = _editionService.GetEdition(bookFile.EditionId);
            var newFileName = _buildFileNames.BuildBookFileName(author, edition, bookFile);
            var filePath = _buildFileNames.BuildBookFilePath(author, edition, newFileName, Path.GetExtension(bookFile.Path));

            EnsureBookFolder(bookFile, author, edition.Book.Value, filePath);

            _logger.Debug("Renaming book file: {0} to {1}", bookFile, filePath);

            return TransferFile(bookFile, author, bookFile.Edition.Value.Book.Value, filePath, TransferMode.Move);
        }

        public BookFile MoveBookFile(BookFile bookFile, LocalBook localBook)
        {
            var newFileName = _buildFileNames.BuildBookFileName(localBook.Author, localBook.Edition, bookFile);
            var filePath = _buildFileNames.BuildBookFilePath(localBook.Author, localBook.Edition, newFileName, Path.GetExtension(localBook.Path));

            EnsureTrackFolder(bookFile, localBook, filePath);

            _logger.Debug("Moving book file: {0} to {1}", bookFile.Path, filePath);

            return TransferFile(bookFile, localBook.Author, localBook.Book, filePath, TransferMode.Move);
        }

        public BookFile CopyBookFile(BookFile bookFile, LocalBook localBook)
        {
            var newFileName = _buildFileNames.BuildBookFileName(localBook.Author, localBook.Edition, bookFile);
            var filePath = _buildFileNames.BuildBookFilePath(localBook.Author, localBook.Edition, newFileName, Path.GetExtension(localBook.Path));

            EnsureTrackFolder(bookFile, localBook, filePath);

            if (_configService.CopyUsingHardlinks)
            {
                _logger.Debug("Hardlinking book file: {0} to {1}", bookFile.Path, filePath);
                return TransferFile(bookFile, localBook.Author, localBook.Book, filePath, TransferMode.HardLinkOrCopy);
            }

            _logger.Debug("Copying book file: {0} to {1}", bookFile.Path, filePath);
            return TransferFile(bookFile, localBook.Author, localBook.Book, filePath, TransferMode.Copy);
        }

        private BookFile TransferFile(BookFile bookFile, Author author, Book book, string destinationFilePath, TransferMode mode)
        {
            Ensure.That(bookFile, () => bookFile).IsNotNull();
            Ensure.That(author, () => author).IsNotNull();
            Ensure.That(destinationFilePath, () => destinationFilePath).IsValidPath(PathValidationType.CurrentOs);

            var bookFilePath = bookFile.Path;

            if (!_diskProvider.FileExists(bookFilePath))
            {
                throw new FileNotFoundException("Book file path does not exist", bookFilePath);
            }

            if (bookFilePath == destinationFilePath)
            {
                throw new SameFilenameException("File not moved, source and destination are the same", bookFilePath);
            }

            if (_diskProvider.FileExists(destinationFilePath))
            {
                // An existing destination is only adopted when it is provably the same release and no
                // book file row already owns it. Any other case is a conflict, and the tracked binding stays as it is.
                if (_mediaFileService.GetFileWithPath(destinationFilePath) != null)
                {
                    throw new DestinationAlreadyExistsException($"Destination {destinationFilePath} is already tracked as a book file");
                }

                if (!HasSameContent(bookFilePath, destinationFilePath))
                {
                    throw new DestinationAlreadyExistsException($"Destination {destinationFilePath} exists with different content");
                }

                _logger.Info("Destination file {0} already holds identical content without a tracked entry, adopting it", destinationFilePath);

                if (mode == TransferMode.Move)
                {
                    _diskProvider.DeleteFile(bookFilePath);
                }
            }
            else
            {
                _rootFolderWatchingService.ReportFileSystemChangeBeginning(bookFilePath, destinationFilePath);
                _diskTransferService.TransferFile(bookFilePath, destinationFilePath, mode);
            }

            bookFile.Path = destinationFilePath;

            _updateBookFileService.ChangeFileDateForFile(bookFile, author, book);

            try
            {
                _mediaFileAttributeService.SetFolderLastWriteTime(author.Path, bookFile.DateAdded);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Unable to set last write time");
            }

            _mediaFileAttributeService.SetFilePermissions(destinationFilePath);

            return bookFile;
        }

        private bool HasSameContent(string sourcePath, string destinationPath)
        {
            if (_diskProvider.GetFileSize(sourcePath) != _diskProvider.GetFileSize(destinationPath))
            {
                return false;
            }

            return _hashProvider.ComputeMd5(sourcePath).SequenceEqual(_hashProvider.ComputeMd5(destinationPath));
        }

        private void EnsureTrackFolder(BookFile bookFile, LocalBook localBook, string filePath)
        {
            EnsureBookFolder(bookFile, localBook.Author, localBook.Book, filePath);
        }

        private void EnsureBookFolder(BookFile bookFile, Author author, Book book, string filePath)
        {
            var trackFolder = Path.GetDirectoryName(filePath);
            var bookFolder = _buildFileNames.BuildBookPath(author);
            var authorFolder = author.Path;
            var rootFolder = new OsPath(authorFolder).Directory.FullPath;

            if (!_diskProvider.FolderExists(rootFolder))
            {
                throw new RootFolderNotFoundException(string.Format("Root folder '{0}' was not found.", rootFolder));
            }

            var changed = false;
            var newEvent = new TrackFolderCreatedEvent(author, bookFile);

            _rootFolderWatchingService.ReportFileSystemChangeBeginning(authorFolder, bookFolder, trackFolder);

            if (!_diskProvider.FolderExists(authorFolder))
            {
                CreateFolder(authorFolder);
                newEvent.AuthorFolder = authorFolder;
                changed = true;
            }

            if (authorFolder != bookFolder && !_diskProvider.FolderExists(bookFolder))
            {
                CreateFolder(bookFolder);
                newEvent.BookFolder = bookFolder;
                changed = true;
            }

            if (bookFolder != trackFolder && !_diskProvider.FolderExists(trackFolder))
            {
                CreateFolder(trackFolder);
                newEvent.TrackFolder = trackFolder;
                changed = true;
            }

            if (changed)
            {
                _eventAggregator.PublishEvent(newEvent);
            }
        }

        private void CreateFolder(string directoryName)
        {
            Ensure.That(directoryName, () => directoryName).IsNotNullOrWhiteSpace();

            var parentFolder = new OsPath(directoryName).Directory.FullPath;
            if (!_diskProvider.FolderExists(parentFolder))
            {
                CreateFolder(parentFolder);
            }

            try
            {
                _diskProvider.CreateFolder(directoryName);
            }
            catch (IOException ex)
            {
                _logger.Error(ex, "Unable to create directory: {0}", directoryName);
            }

            _mediaFileAttributeService.SetFolderPermissions(directoryName);
        }
    }
}
