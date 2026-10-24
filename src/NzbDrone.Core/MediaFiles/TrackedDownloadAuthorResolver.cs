using System;
using System.Linq;
using NzbDrone.Core.Books;
using NzbDrone.Core.Download.TrackedDownloads;

namespace NzbDrone.Core.MediaFiles
{
    // Shared by the download scan and manual import so both resolve the author the same way.
    public static class TrackedDownloadAuthorResolver
    {
        public static Author Resolve(TrackedDownload trackedDownload, Func<int, Author> getAuthorByMetadataId)
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
                return getAuthorByMetadataId(authorMetadataId);
            }

            return null;
        }
    }
}
