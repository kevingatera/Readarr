using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Test.Services
{
    [TestFixture]
    public class RefreshSeriesServiceFixture
    {
        [Test]
        public void should_bind_remote_series_links_to_local_book_ids()
        {
            var localBook = new Book
            {
                Id = 42,
                ForeignBookId = "foreign-book-1"
            };

            var remoteBook = new Book
            {
                ForeignBookId = localBook.ForeignBookId
            };

            var link = new SeriesBookLink
            {
                Book = new LazyLoaded<Book>(remoteBook)
            };

            var result = RefreshSeriesService.BindLinksToLocalBooks(
                new[] { link },
                new Dictionary<string, Book>
                {
                    [localBook.ForeignBookId] = localBook
                });

            result.Should().ContainSingle();
            result[0].Book.Value.Should().BeSameAs(localBook);
            result[0].BookId.Should().Be(localBook.Id);
        }
    }
}
