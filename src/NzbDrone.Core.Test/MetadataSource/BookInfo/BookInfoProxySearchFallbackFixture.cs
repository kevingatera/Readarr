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
using NzbDrone.Core.MetadataSource;
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

        [Test]
        public void should_not_backfill_series_linked_works_during_book_search()
        {
            var httpClient = new Mock<IHttpClient>();
            var cachedHttpClient = new Mock<ICachedHttpResponseService>();
            var searchProxy = new Mock<IGoodreadsSearchProxy>();
            var requestBuilder = new Mock<IMetadataRequestBuilder>();
            var cacheManager = new Mock<ICacheManager>();

            searchProxy
                .Setup(x => x.Search("query"))
                .Returns(new List<SearchJsonResource>
                {
                    new SearchJsonResource
                    {
                        WorkId = 10,
                        BookId = 100,
                        Author = new AuthorJsonResource { Id = 1, Name = "Author" }
                    }
                });

            requestBuilder
                .Setup(x => x.GetRequestBuilder())
                .Returns(new HttpRequestBuilder("http://metadata.invalid/{route}").CreateFactory());

            cacheManager
                .Setup(x => x.GetCache<HashSet<string>>(It.IsAny<Type>()))
                .Returns(new Mock<ICached<HashSet<string>>>().Object);

            var authorJson = "{\"ForeignId\":1,\"Name\":\"Author\",\"Works\":[{\"ForeignId\":10,\"Title\":\"Known Work\",\"Authors\":[{\"ForeignId\":1,\"Name\":\"Author\"}]}],\"Series\":[{\"ForeignId\":50,\"Title\":\"Series\",\"LinkItems\":[{\"ForeignWorkId\":20,\"PositionInSeries\":\"2\",\"SeriesPosition\":2,\"Primary\":true}]}]}";

            cachedHttpClient
                .Setup(x => x.Get(It.IsAny<HttpRequest>(), It.IsAny<bool>(), It.IsAny<TimeSpan>()))
                .Returns((HttpRequest request, bool useCache, TimeSpan ttl) =>
                    new HttpResponse(
                        request,
                        new HttpHeader { ContentType = "application/json" },
                        authorJson));

            var subject = new BookInfoProxy(
                httpClient.Object,
                cachedHttpClient.Object,
                searchProxy.Object,
                Mock.Of<IAuthorService>(),
                Mock.Of<IBookService>(),
                Mock.Of<IEditionService>(),
                requestBuilder.Object,
                LogManager.GetLogger("BookInfoProxySearchFallbackFixture"),
                cacheManager.Object);

            var result = subject.SearchForNewBook("query", null, true);

            result.Should().ContainSingle();
            result[0].ForeignBookId.Should().Be("10");
            httpClient.Verify(x => x.Get(It.IsAny<HttpRequest>()), Times.Never());
        }

        [Test]
        public void should_treat_changed_author_response_with_null_ids_as_unavailable()
        {
            var httpClient = new Mock<IHttpClient>();
            httpClient
                .Setup(x => x.Get<RecentUpdatesResource>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request => new HttpResponse<RecentUpdatesResource>(
                    new HttpResponse(request, new HttpHeader { ContentType = "application/json" }, "{\"Limited\":false,\"Ids\":null}")));

            var requestBuilder = new Mock<IMetadataRequestBuilder>();
            requestBuilder
                .Setup(x => x.GetRequestBuilder())
                .Returns(new HttpRequestBuilder("http://metadata.invalid/{route}").CreateFactory());

            var cacheManager = new Mock<ICacheManager>();
            cacheManager
                .Setup(x => x.GetCache<HashSet<string>>(It.IsAny<Type>()))
                .Returns(new Mock<ICached<HashSet<string>>>().Object);

            var subject = new BookInfoProxy(
                httpClient.Object,
                Mock.Of<ICachedHttpResponseService>(),
                Mock.Of<IGoodreadsSearchProxy>(),
                Mock.Of<IAuthorService>(),
                Mock.Of<IBookService>(),
                Mock.Of<IEditionService>(),
                requestBuilder.Object,
                LogManager.GetLogger("BookInfoProxySearchFallbackFixture"),
                cacheManager.Object);

            var result = subject.GetChangedAuthors(DateTime.UtcNow);

            result.Should().BeNull();
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
