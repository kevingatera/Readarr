using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.BookImport.Specifications
{
    public class SameFileSpecification : IImportDecisionEngineSpecification<LocalBook>
    {
        private readonly Logger _logger;

        public SameFileSpecification(Logger logger)
        {
            _logger = logger;
        }

        public Decision IsSatisfiedBy(LocalBook localBook, DownloadClientItem downloadClientItem)
        {
            var bookFiles = localBook.Book?.BookFiles?.Value;

            if (bookFiles == null || !bookFiles.Any())
            {
                _logger.Debug("No existing book file, skipping");
                return Decision.Accept();
            }

            foreach (var bookFile in bookFiles)
            {
                if (bookFile == null)
                {
                    var book = localBook.Book;
                    _logger.Trace("Unable to get book file details from the DB. BookId: {0}", book.Id);

                    return Decision.Accept();
                }

                if (bookFile.Size == localBook.Size)
                {
                    if (localBook.AllowSameFileMatch && bookFile.Path.PathEquals(localBook.Path))
                    {
                        _logger.Debug("'{0}' Matches an existing file path and same-file remap is allowed", localBook.Path);
                        continue;
                    }

                    _logger.Debug("'{0}' Has the same filesize as existing file", localBook.Path);
                    return Decision.Reject("Has the same filesize as existing file");
                }
            }

            return Decision.Accept();
        }
    }
}
