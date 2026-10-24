using System;
using NzbDrone.Common.Messaging;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Download
{
    public class BookGrabbedEvent : IEvent
    {
        public RemoteBook Book { get; private set; }
        public int DownloadClientId { get; set; }
        public string DownloadClient { get; set; }
        public string DownloadClientName { get; set; }
        public string DownloadId { get; set; }

        // Shared by every book row written for this one grab.
        public string GrabId { get; private set; }

        public BookGrabbedEvent(RemoteBook book)
        {
            Book = book;
            GrabId = Guid.NewGuid().ToString();
        }
    }
}
