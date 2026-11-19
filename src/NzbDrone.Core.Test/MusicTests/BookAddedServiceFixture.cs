using System;
using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.IndexerSearch;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.BookTests
{
    [TestFixture]
    public class BookAddedServiceFixture : CoreTest<BookAddedService>
    {
        private Author _author;

        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<ICacheManager>()
                .Setup(x => x.GetCache<List<int>>(It.IsAny<Type>()))
                .Returns(new Cached<List<int>>());

            _author = Builder<Author>.CreateNew()
                .With(a => a.Id = 1)
                .With(a => a.Monitored = true)
                .With(a => a.AddOptions = null)
                .Build();
        }

        private Book GivenBook(int id, DateTime? releaseDate)
        {
            return Builder<Book>.CreateNew()
                .With(b => b.Id = id)
                .With(b => b.Monitored = true)
                .With(b => b.ReleaseDate = releaseDate)
                .With(b => b.AddOptions = new AddBookOptions())
                .Build();
        }

        private void RefreshAndScan(params Book[] added)
        {
            Mocker.GetMock<IBookService>()
                .Setup(x => x.GetBooksByAuthor(_author.Id))
                .Returns(added.ToList());

            Subject.Handle(new BookInfoRefreshedEvent(_author, added.ToList(), new List<Book>(), new List<Book>()));
            Subject.SearchForRecentlyAdded(_author.Id);
        }

        [Test]
        public void should_search_newly_added_book_released_recently()
        {
            RefreshAndScan(GivenBook(10, DateTime.UtcNow.AddDays(-3)));

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(x => x.Push(It.Is<BookSearchCommand>(c => c.BookIds.SequenceEqual(new List<int> { 10 })), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Once());
        }

        [Test]
        public void should_not_search_back_catalogue_book_found_by_refresh()
        {
            RefreshAndScan(GivenBook(10, new DateTime(1957, 4, 1)), GivenBook(11, DateTime.UtcNow.AddYears(-2)));

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(x => x.Push(It.IsAny<BookSearchCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Never());
        }

        [Test]
        public void should_only_search_recent_books_from_a_mixed_refresh()
        {
            RefreshAndScan(GivenBook(10, new DateTime(1990, 1, 1)), GivenBook(11, DateTime.UtcNow.AddDays(-1)), GivenBook(12, DateTime.UtcNow.AddDays(30)));

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(x => x.Push(It.Is<BookSearchCommand>(c => c.BookIds.SequenceEqual(new List<int> { 11 })), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Once());
        }

        [TestCase(-13, true)]
        [TestCase(-16, false)]
        [TestCase(0.5, true)]
        [TestCase(3, false)]
        public void should_respect_recent_release_window(double days, bool searched)
        {
            RefreshAndScan(GivenBook(10, DateTime.UtcNow.AddDays(days)));

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(x => x.Push(It.IsAny<BookSearchCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), searched ? Times.Once() : Times.Never());
        }

        [Test]
        public void should_not_search_undated_book_found_by_refresh()
        {
            RefreshAndScan(GivenBook(10, null));

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(x => x.Push(It.IsAny<BookSearchCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Never());
        }

        [Test]
        public void should_still_search_old_book_explicitly_flagged_for_search()
        {
            var book = GivenBook(10, new DateTime(1957, 4, 1));
            book.AddOptions.SearchForNewBook = true;

            Mocker.GetMock<IBookService>()
                .Setup(x => x.GetBooksByAuthor(_author.Id))
                .Returns(new List<Book> { book });

            Subject.SearchForRecentlyAdded(_author.Id);

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(x => x.Push(It.Is<BookSearchCommand>(c => c.BookIds.SequenceEqual(new List<int> { 10 })), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Once());
        }
    }
}
