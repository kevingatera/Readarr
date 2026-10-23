using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
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
        public void should_not_backfill_when_edition_search_redirects_to_author()
        {
            var httpClient = new Mock<IHttpClient>();
            var cachedHttpClient = new Mock<ICachedHttpResponseService>();

            httpClient
                .Setup(x => x.Get(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) => new HttpResponse(
                    request,
                    new HttpHeader { { "Location", "http://metadata.invalid/author/1" } },
                    string.Empty,
                    HttpStatusCode.Found));

            cachedHttpClient
                .Setup(x => x.Get(It.IsAny<HttpRequest>(), It.IsAny<bool>(), It.IsAny<TimeSpan>()))
                .Returns((HttpRequest request, bool useCache, TimeSpan ttl) => new HttpResponse(
                    request,
                    new HttpHeader { ContentType = "application/json" },
                    AuthorJson(KnownWorkWithEdition("10", "100"), SeriesLink("50", "20"))));

            var subject = CreateHttpBackedProxy(httpClient.Object, cachedHttpClient.Object);

            var result = subject.SearchForNewBook("edition:100", null, false);

            result.Should().ContainSingle();
            httpClient.Verify(x => x.Get(It.Is<HttpRequest>(r => r.Url.ToString().Contains("/work/"))), Times.Never());
        }

        [Test]
        public void should_cap_series_backfill_requests_per_author_refresh()
        {
            var httpClient = new Mock<IHttpClient>();
            var cachedHttpClient = new Mock<ICachedHttpResponseService>();

            httpClient
                .Setup(x => x.Get(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) => new HttpResponse(request, new HttpHeader(), string.Empty, HttpStatusCode.NotFound));

            var linkedWorks = string.Join(",", Enumerable.Range(1000, 30).Select(id => $"{{\"ForeignWorkId\":{id},\"Primary\":true}}"));

            cachedHttpClient
                .Setup(x => x.Get(It.IsAny<HttpRequest>(), It.IsAny<bool>(), It.IsAny<TimeSpan>()))
                .Returns((HttpRequest request, bool useCache, TimeSpan ttl) => new HttpResponse(
                    request,
                    new HttpHeader { ContentType = "application/json" },
                    AuthorJson(KnownWorkWithEdition("10", "100"), "{\"ForeignId\":50,\"Title\":\"Series\",\"LinkItems\":[" + linkedWorks + "]}")));

            var subject = CreateHttpBackedProxy(httpClient.Object, cachedHttpClient.Object);

            subject.GetAuthorInfo("1");

            // One request per backfill candidate up to MaxSeriesBackfillRequests (20).
            httpClient.Verify(x => x.Get(It.Is<HttpRequest>(r => r.Url.ToString().Contains("/work/"))), Times.Exactly(20));
        }

        [Test]
        public void should_drop_backfilled_work_whose_id_does_not_match_the_request()
        {
            var httpClient = new Mock<IHttpClient>();
            var cachedHttpClient = new Mock<ICachedHttpResponseService>();

            // work/20 answers with a different work (a redirect or colliding ID).
            httpClient
                .Setup(x => x.Get(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) => new HttpResponse(
                    request,
                    new HttpHeader(),
                    "{\"ForeignId\":21,\"Title\":\"Other\",\"Authors\":[{\"ForeignId\":1,\"Name\":\"Author\"}]}"));

            cachedHttpClient
                .Setup(x => x.Get(It.IsAny<HttpRequest>(), It.IsAny<bool>(), It.IsAny<TimeSpan>()))
                .Returns((HttpRequest request, bool useCache, TimeSpan ttl) => new HttpResponse(
                    request,
                    new HttpHeader { ContentType = "application/json" },
                    AuthorJson(KnownWorkWithEdition("10", "100"), SeriesLink("50", "20"))));

            var subject = CreateHttpBackedProxy(httpClient.Object, cachedHttpClient.Object);

            var author = subject.GetAuthorInfo("1");

            author.Books.Value.Select(x => x.ForeignBookId).Should().Equal("10");
        }

        [Test]
        public void should_not_backfill_a_series_link_that_names_a_known_edition()
        {
            var httpClient = new Mock<IHttpClient>();
            var cachedHttpClient = new Mock<ICachedHttpResponseService>();

            httpClient
                .Setup(x => x.Get(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) => new HttpResponse(request, new HttpHeader(), string.Empty, HttpStatusCode.NotFound));

            cachedHttpClient
                .Setup(x => x.Get(It.IsAny<HttpRequest>(), It.IsAny<bool>(), It.IsAny<TimeSpan>()))
                .Returns((HttpRequest request, bool useCache, TimeSpan ttl) => new HttpResponse(
                    request,
                    new HttpHeader { ContentType = "application/json" },
                    AuthorJson(KnownWorkWithEdition("10", "100"), SeriesLink("50", "100"))));

            var subject = CreateHttpBackedProxy(httpClient.Object, cachedHttpClient.Object);

            var author = subject.GetAuthorInfo("1");

            // 100 is an edition of work 10, so it links to that book and is never fetched as a work.
            httpClient.Verify(x => x.Get(It.Is<HttpRequest>(r => r.Url.ToString().Contains("/work/"))), Times.Never());
            author.Books.Value.Single().SeriesLinks.Value.Should().HaveCount(1);
        }

        private static BookInfoProxy CreateHttpBackedProxy(IHttpClient httpClient, ICachedHttpResponseService cachedHttpClient)
        {
            var requestBuilder = new Mock<IMetadataRequestBuilder>();
            requestBuilder
                .Setup(x => x.GetRequestBuilder())
                .Returns(new HttpRequestBuilder("http://metadata.invalid/{route}").CreateFactory());

            var cacheManager = new Mock<ICacheManager>();
            cacheManager
                .Setup(x => x.GetCache<HashSet<string>>(It.IsAny<Type>()))
                .Returns(new Mock<ICached<HashSet<string>>>().Object);

            return new BookInfoProxy(
                httpClient,
                cachedHttpClient,
                Mock.Of<IGoodreadsSearchProxy>(),
                Mock.Of<IAuthorService>(),
                Mock.Of<IBookService>(),
                Mock.Of<IEditionService>(),
                requestBuilder.Object,
                LogManager.GetLogger("BookInfoProxySearchFallbackFixture"),
                cacheManager.Object);
        }

        // Author 1 with one known work (ForeignId = workId) carrying one edition (ForeignId = editionId).
        private static string KnownWorkWithEdition(string workId, string editionId)
        {
            return "{\"ForeignId\":" + workId + ",\"Title\":\"Known Work\","
                + "\"Authors\":[{\"ForeignId\":1,\"Name\":\"Author\"}],"
                + "\"Books\":[{\"ForeignId\":" + editionId + ",\"Title\":\"Known Work\","
                + "\"Contributors\":[{\"ForeignId\":1,\"Role\":\"Author\"}]}]}";
        }

        private static string SeriesLink(string seriesId, string workId)
        {
            return "{\"ForeignId\":" + seriesId + ",\"Title\":\"Series\",\"LinkItems\":[{\"ForeignWorkId\":" + workId + ",\"PositionInSeries\":\"2\",\"SeriesPosition\":2,\"Primary\":true}]}";
        }

        private static string AuthorJson(string work, string series)
        {
            return "{\"ForeignId\":1,\"Name\":\"Author\",\"Works\":[" + work + "],\"Series\":[" + series + "]}";
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
