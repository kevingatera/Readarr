using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Http;
using NzbDrone.Core.Books;
using NzbDrone.Core.Http;
using NzbDrone.Core.MetadataSource.BookInfo;
using NzbDrone.Core.MetadataSource.Goodreads;

namespace NzbDrone.Core.Test.MetadataSource.Goodreads
{
    [TestFixture]
    public class BookInfoProxySearchFallbackFixture
    {
        [Test]
        public void should_retry_add_search_with_full_editions_when_fast_path_is_empty()
        {
            var calls = new List<bool>();
            var author = BuildAuthor("14168090", "Jason Anspach");
            var book = BuildBook("227248027", "The Betrayed", author);

            var subject = CreateSubject((title, authorName, getAllEditions) =>
            {
                calls.Add(getAllEditions);
                return getAllEditions ? new List<Book> { book } : new List<Book>();
            });

            var result = subject.SearchForNewEntity("the betrayed anspach");

            calls.Should().Equal(false, true);
            result.Should().HaveCount(2);
            result[0].Should().Be(author);
            result[1].Should().Be(book);
        }

        [Test]
        public void should_not_retry_add_search_when_fast_path_returns_results()
        {
            var calls = new List<bool>();
            var author = BuildAuthor("14168090", "Jason Anspach");
            var book = BuildBook("227248027", "The Betrayed", author);

            var subject = CreateSubject((title, authorName, getAllEditions) =>
            {
                calls.Add(getAllEditions);
                return new List<Book> { book };
            });

            var result = subject.SearchForNewEntity("the betrayed anspach");

            calls.Should().Equal(false);
            result.Should().HaveCount(2);
            result[0].Should().Be(author);
            result[1].Should().Be(book);
        }

        private static TestableBookInfoProxy CreateSubject(Func<string, string, bool, List<Book>> searchHandler)
        {
            var cacheManager = new Mock<ICacheManager>();
            cacheManager
                .Setup(x => x.GetCache<HashSet<string>>(It.IsAny<Type>()))
                .Returns(new Mock<ICached<HashSet<string>>>().Object);

            return new TestableBookInfoProxy(
                Mock.Of<IHttpClient>(),
                Mock.Of<ICachedHttpResponseService>(),
                Mock.Of<IGoodreadsSearchProxy>(),
                Mock.Of<IAuthorService>(),
                Mock.Of<IBookService>(),
                Mock.Of<IEditionService>(),
                Mock.Of<IMetadataRequestBuilder>(),
                LogManager.GetLogger("BookInfoProxySearchFallbackFixture"),
                cacheManager.Object,
                searchHandler);
        }

        private static Author BuildAuthor(string foreignAuthorId, string name)
        {
            return new Author
            {
                Metadata = new AuthorMetadata
                {
                    ForeignAuthorId = foreignAuthorId,
                    Name = name
                },
                CleanName = name
            };
        }

        private static Book BuildBook(string foreignBookId, string title, Author author)
        {
            return new Book
            {
                ForeignBookId = foreignBookId,
                Title = title,
                Author = author,
                AuthorMetadata = author.Metadata.Value
            };
        }

        private class TestableBookInfoProxy : BookInfoProxy
        {
            private readonly Func<string, string, bool, List<Book>> _searchHandler;

            public TestableBookInfoProxy(
                IHttpClient httpClient,
                ICachedHttpResponseService cachedHttpClient,
                IGoodreadsSearchProxy goodreadsSearchProxy,
                IAuthorService authorService,
                IBookService bookService,
                IEditionService editionService,
                IMetadataRequestBuilder requestBuilder,
                Logger logger,
                ICacheManager cacheManager,
                Func<string, string, bool, List<Book>> searchHandler)
                : base(
                    httpClient,
                    cachedHttpClient,
                    goodreadsSearchProxy,
                    authorService,
                    bookService,
                    editionService,
                    requestBuilder,
                    logger,
                    cacheManager)
            {
                _searchHandler = searchHandler;
            }

            public override List<Book> SearchForNewBook(string title, string author, bool getAllEditions = true)
            {
                return _searchHandler(title, author, getAllEditions);
            }
        }
    }
}
