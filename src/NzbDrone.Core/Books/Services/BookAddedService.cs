using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.IndexerSearch;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Books
{
    public interface IBookAddedService
    {
        void SearchForRecentlyAdded(int authorId);
    }

    public class BookAddedService : IHandle<BookInfoRefreshedEvent>, IBookAddedService
    {
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly IBookService _bookService;
        private readonly Logger _logger;
        private readonly ICached<List<int>> _addedBooksCache;

        // Newly added books released before this window are not searched automatically.
        public static readonly TimeSpan RecentReleaseWindow = TimeSpan.FromDays(14);

        public BookAddedService(ICacheManager cacheManager,
                                   IManageCommandQueue commandQueueManager,
                                   IBookService bookService,
                                   Logger logger)
        {
            _commandQueueManager = commandQueueManager;
            _bookService = bookService;
            _logger = logger;
            _addedBooksCache = cacheManager.GetCache<List<int>>(GetType());
        }

        public void SearchForRecentlyAdded(int authorId)
        {
            var allBooks = _bookService.GetBooksByAuthor(authorId);
            var toSearch = allBooks.Where(x => x.AddOptions.SearchForNewBook).ToList();

            if (toSearch.Any())
            {
                toSearch.ForEach(x => x.AddOptions.SearchForNewBook = false);

                _bookService.SetAddOptions(toSearch);
            }

            var recentlyAddedIds = _addedBooksCache.Find(authorId.ToString());
            if (recentlyAddedIds != null)
            {
                toSearch.AddRange(allBooks.Where(x => recentlyAddedIds.Contains(x.Id)));
            }

            if (toSearch.Any())
            {
                _commandQueueManager.Push(new BookSearchCommand(toSearch.Select(e => e.Id).ToList()));
            }

            _addedBooksCache.Remove(authorId.ToString());
        }

        public void Handle(BookInfoRefreshedEvent message)
        {
            if (message.Author.AddOptions == null)
            {
                if (!message.Author.Monitored)
                {
                    _logger.Debug("Author is not monitored");
                    return;
                }

                if (message.Added.Empty())
                {
                    _logger.Debug("No new books, skipping search");
                    return;
                }

                if (message.Added.None(a => a.ReleaseDate.HasValue))
                {
                    _logger.Debug("No new books have an release date");
                    return;
                }

                // A refresh also "adds" back-catalogue books the metadata source only just
                // returned (series backfill, ID churn). Only search books released recently,
                // like Sonarr does; older ones stay wanted for RSS or a manual search.
                var previouslyReleased = message.Added.Where(a => a.Monitored && IsRecentRelease(a)).ToList();

                if (previouslyReleased.Empty())
                {
                    _logger.Debug("No newly added books were released recently, skipping search");
                    return;
                }

                _addedBooksCache.Set(message.Author.Id.ToString(), previouslyReleased.Select(e => e.Id).ToList());
            }
        }

        private static bool IsRecentRelease(Book book)
        {
            return book.ReleaseDate.HasValue &&
                   book.ReleaseDate.Value.After(DateTime.UtcNow - RecentReleaseWindow) &&
                   book.ReleaseDate.Value.Before(DateTime.UtcNow.AddDays(1));
        }
    }
}
