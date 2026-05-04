using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.MediaFiles
{
    public class DownloadedBooksCommandService : IExecute<DownloadedBooksScanCommand>
    {
        private readonly IDownloadedBooksImportService _downloadedTracksImportService;
        private readonly ITrackedDownloadService _trackedDownloadService;
        private readonly IAuthorService _authorService;
        private readonly IDiskProvider _diskProvider;
        private readonly ICompletedDownloadService _completedDownloadService;
        private readonly ICommandResultReporter _commandResultReporter;
        private readonly Logger _logger;

        public DownloadedBooksCommandService(IDownloadedBooksImportService downloadedTracksImportService,
                                                ITrackedDownloadService trackedDownloadService,
                                                IAuthorService authorService,
                                                IDiskProvider diskProvider,
                                                ICompletedDownloadService completedDownloadService,
                                                ICommandResultReporter commandResultReporter,
                                                Logger logger)
        {
            _downloadedTracksImportService = downloadedTracksImportService;
            _trackedDownloadService = trackedDownloadService;
            _authorService = authorService;
            _diskProvider = diskProvider;
            _completedDownloadService = completedDownloadService;
            _commandResultReporter = commandResultReporter;
            _logger = logger;
        }

        private List<ImportResult> ProcessPath(DownloadedBooksScanCommand message)
        {
            if (!_diskProvider.FolderExists(message.Path) && !_diskProvider.FileExists(message.Path))
            {
                _logger.Warn("Folder/File specified for import scan [{0}] doesn't exist.", message.Path);
                return new List<ImportResult>();
            }

            if (message.DownloadClientId.IsNotNullOrWhiteSpace())
            {
                var trackedDownload = _trackedDownloadService.Find(message.DownloadClientId);

                if (trackedDownload != null)
                {
                    _logger.Debug("External directory scan request for known download {0}. [{1}]", message.DownloadClientId, message.Path);

                    var importResults = _downloadedTracksImportService.ProcessPath(message.Path, message.ImportMode, GetTrackedDownloadAuthor(trackedDownload), trackedDownload.DownloadItem);

                    _completedDownloadService.VerifyImport(trackedDownload, importResults);

                    return importResults;
                }

                _logger.Warn("External directory scan request for unknown download {0}, attempting normal import. [{1}]", message.DownloadClientId, message.Path);
            }

            return _downloadedTracksImportService.ProcessPath(message.Path, message.ImportMode);
        }

        private Author GetTrackedDownloadAuthor(TrackedDownload trackedDownload)
        {
            var remoteBook = trackedDownload?.RemoteBook;
            if (remoteBook == null)
            {
                return null;
            }

            var author = remoteBook.Author ?? remoteBook.Books?.FirstOrDefault(x => x?.Author != null)?.Author;
            if (author != null)
            {
                return author;
            }

            var authorMetadataId = remoteBook.Books?.FirstOrDefault(x => x?.AuthorMetadataId > 0)?.AuthorMetadataId ?? 0;
            if (authorMetadataId > 0)
            {
                return _authorService.GetAuthorByMetadataId(authorMetadataId);
            }

            return null;
        }

        public void Execute(DownloadedBooksScanCommand message)
        {
            List<ImportResult> importResults;

            if (message.Path.IsNotNullOrWhiteSpace())
            {
                importResults = ProcessPath(message);
            }
            else
            {
                throw new ArgumentException("A path must be provided", "path");
            }

            if (importResults == null || importResults.All(v => v.Result != ImportResultType.Imported))
            {
                // Allow the command to complete successfully, but report as unsuccessful
                _logger.ProgressDebug("Failed to import");
                _commandResultReporter.Report(CommandResult.Unsuccessful);
            }
        }
    }
}
