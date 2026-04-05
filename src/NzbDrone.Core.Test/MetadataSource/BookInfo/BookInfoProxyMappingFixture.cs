using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Books;
using NzbDrone.Core.Http;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.BookInfo;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.Goodreads
{
    [TestFixture]
    public class BookInfoProxyMappingFixture : CoreTest<BookInfoProxy>
    {
        [Test]
        public void should_fallback_to_work_authors_when_books_are_missing()
        {
            var work = new WorkResource
            {
                ForeignId = 10,
                Title = "1812",
                Books = null,
                Authors = new List<AuthorResource>
                {
                    new AuthorResource { ForeignId = 1, Name = "Eric Flint" }
                }
            };

            var authorId = InvokePrivateStatic<int>("GetAuthorId", work);

            authorId.Should().Be(1);
        }

        [Test]
        public void should_map_author_without_crashing_when_work_has_no_books()
        {
            var resource = new AuthorResource
            {
                ForeignId = 1,
                Name = "Eric Flint",
                Works = new List<WorkResource>
                {
                    new WorkResource
                    {
                        ForeignId = 10,
                        Title = "1812",
                        Books = null,
                        Authors = new List<AuthorResource>
                        {
                            new AuthorResource { ForeignId = 1, Name = "Eric Flint" }
                        }
                    }
                },
                Series = new List<SeriesResource>()
            };

            var author = InvokePrivateStatic<Author>("MapAuthor", resource);

            author.Should().NotBeNull();
            author.Books.Value.Should().HaveCount(1);
            author.Books.Value[0].ForeignBookId.Should().Be("10");
            author.Books.Value[0].Title.Should().Be("1812");
        }

        // Regression: a work carrying no author/contributor information at all
        // (sparse upstream data, e.g. under load) used to be silently dropped by
        // MapAuthor, which made the refresh see fewer books than are held locally
        // and caused RefreshEntityServiceBase to delete the "missing" local books.
        // Such a work is now attributed to the author being mapped.
        [Test]
        public void should_keep_work_when_work_has_no_author_information()
        {
            var resource = new AuthorResource
            {
                ForeignId = 1,
                Name = "Eric Flint",
                Works = new List<WorkResource>
                {
                    new WorkResource
                    {
                        ForeignId = 10,
                        Title = "1812",
                        Books = null,
                        Authors = null
                    }
                },
                Series = new List<SeriesResource>()
            };

            var author = InvokePrivateStatic<Author>("MapAuthor", resource);

            author.Should().NotBeNull();
            author.Books.Value.Should().HaveCount(1);
            author.Books.Value[0].ForeignBookId.Should().Be("10");
        }

        // Sanity: a work that explicitly lists a DIFFERENT author is still
        // dropped by MapAuthor (only the no-author-data case is preserved).
        [Test]
        public void should_still_drop_work_that_belongs_to_a_different_author()
        {
            var resource = new AuthorResource
            {
                ForeignId = 1,
                Name = "Eric Flint",
                Works = new List<WorkResource>
                {
                    new WorkResource
                    {
                        ForeignId = 10,
                        Title = "Someone Else's Work",
                        Books = null,
                        Authors = new List<AuthorResource>
                        {
                            new AuthorResource { ForeignId = 2, Name = "Someone Else" }
                        }
                    }
                },
                Series = new List<SeriesResource>()
            };

            var author = InvokePrivateStatic<Author>("MapAuthor", resource);

            author.Should().NotBeNull();
            author.Books.Value.Should().BeEmpty();
        }

        [Test]
        public void should_prefer_author_role_contributors_over_other_contributors()
        {
            var work = new WorkResource
            {
                ForeignId = 10,
                Title = "Example",
                Books = new List<BookResource>
                {
                    new BookResource
                    {
                        Contributors = new List<ContributorResource>
                        {
                            new ContributorResource { ForeignId = 2, Role = "Narrator" },
                            new ContributorResource { ForeignId = 1, Role = "Author" }
                        }
                    }
                }
            };

            var authorId = InvokePrivateStatic<int>("GetAuthorId", work);

            authorId.Should().Be(1);
        }

        [Test]
        public void should_fallback_to_work_authors_when_book_contributors_are_non_authors()
        {
            var work = new WorkResource
            {
                ForeignId = 10,
                Title = "Example",
                Books = new List<BookResource>
                {
                    new BookResource
                    {
                        Contributors = new List<ContributorResource>
                        {
                            new ContributorResource { ForeignId = 2, Role = "Narrator" }
                        }
                    }
                },
                Authors = new List<AuthorResource>
                {
                    new AuthorResource { ForeignId = 1, Name = "Primary Author" }
                }
            };

            var authorId = InvokePrivateStatic<int>("GetAuthorId", work);

            authorId.Should().Be(1);
        }

        [Test]
        public void should_merge_work_author_metadata_when_bulk_authors_are_sparse()
        {
            var authors = new Dictionary<string, AuthorMetadata>();
            var workAuthors = new List<AuthorResource>
            {
                new AuthorResource { ForeignId = 1, Name = "Primary Author" }
            };

            var merged = InvokePrivateStatic<Dictionary<string, AuthorMetadata>>(
                "MergeAuthorMetadata",
                authors,
                workAuthors);

            merged.Should().ContainKey("1");
            merged["1"].Name.Should().Be("Primary Author");
        }

        [Test]
        public void should_normalize_default_dates_to_null()
        {
            var normalized = InvokePrivateStatic<DateTime?>(
                "NormalizeDate",
                new DateTime?(default(DateTime)));

            normalized.Should().BeNull();
        }

        [Test]
        public void should_drop_malformed_release_dates_from_bookinfo_payload()
        {
            var options = (JsonSerializerOptions)typeof(BookInfoProxy)
                .GetField("SerializerSettings", BindingFlags.NonPublic | BindingFlags.Static)
                .GetValue(null);

            var work = JsonSerializer.Deserialize<WorkResource>(
                "{\"foreignId\":10,\"title\":\"1812\",\"releaseDate\":\"+020101-01-01T00:00:00.000Z\"}",
                options);

            var book = InvokePrivateStatic<Book>("MapBook", work);

            book.ReleaseDate.Should().BeNull();
        }

        // Bulk search goes through Newtonsoft and MapBulkBook, not the author/book poll path.
        [Test]
        public void should_keep_bulk_search_results_when_top_level_authors_are_missing()
        {
            ArrangeBulkSearch(
                new List<NzbDrone.Core.MetadataSource.Goodreads.SearchJsonResource> { SearchResult(10, 100, 1), SearchResult(11, 110, 1) },
                new List<WorkResource> { SparseWork(10, 100, new[] { 1 }, "Eric Flint"), SparseWork(11, 110, new[] { 1 }, "Eric Flint") },
                authors: null);

            var result = Subject.SearchForNewBook("1812 eric flint", null, false);

            result.Select(x => x.ForeignBookId).Should().BeEquivalentTo("10", "11");
            result.All(x => x.AuthorMetadata.Value.Name == "Eric Flint").Should().BeTrue();
        }

        [Test]
        public void should_skip_bulk_work_without_author_and_keep_the_rest_of_the_search()
        {
            var orphan = new WorkResource
            {
                ForeignId = 11,
                Title = "Orphan",
                Books = new List<BookResource> { new BookResource { ForeignId = 110, Title = "Orphan" } },
                Authors = new List<AuthorResource>()
            };

            ArrangeBulkSearch(
                new List<NzbDrone.Core.MetadataSource.Goodreads.SearchJsonResource> { SearchResult(10, 100, 1), SearchResult(11, 110, 0) },
                new List<WorkResource> { SparseWork(10, 100, new[] { 1 }, "Eric Flint"), orphan },
                new List<AuthorResource> { new AuthorResource { ForeignId = 1, Name = "Eric Flint" } });

            var result = Subject.SearchForNewBook("1812 eric flint", null, false);

            result.Select(x => x.ForeignBookId).Should().Equal("10");
        }

        [Test]
        public void should_attribute_coauthored_bulk_work_to_its_primary_author()
        {
            var coauthored = new WorkResource
            {
                ForeignId = 10,
                Title = "Coauthored",
                Books = new List<BookResource>
                {
                    new BookResource
                    {
                        ForeignId = 100,
                        Title = "Coauthored",
                        Contributors = new List<ContributorResource>
                        {
                            new ContributorResource { ForeignId = 1, Role = "Author" },
                            new ContributorResource { ForeignId = 2, Role = "Author" }
                        }
                    }
                },
                Authors = new List<AuthorResource>
                {
                    new AuthorResource { ForeignId = 1, Name = "Eric Flint" },
                    new AuthorResource { ForeignId = 2, Name = "Ryk Spoor" }
                }
            };

            ArrangeBulkSearch(
                new List<NzbDrone.Core.MetadataSource.Goodreads.SearchJsonResource> { SearchResult(10, 100, 1), SearchResult(11, 110, 1) },
                new List<WorkResource> { coauthored, SparseWork(11, 110, new[] { 1 }, "Eric Flint") },
                authors: null);

            var result = Subject.SearchForNewBook("coauthored", null, false);

            result.Select(x => x.ForeignBookId).Should().BeEquivalentTo("10", "11");
            result.Single(x => x.ForeignBookId == "10").AuthorMetadata.Value.ForeignAuthorId.Should().Be("1");
        }

        private void ArrangeBulkSearch(List<NzbDrone.Core.MetadataSource.Goodreads.SearchJsonResource> searchResults, List<WorkResource> works, List<AuthorResource> authors)
        {
            Mocker.GetMock<IMetadataRequestBuilder>()
                .Setup(x => x.GetRequestBuilder())
                .Returns(new HttpRequestBuilder("http://bookinfo.test/{route}").CreateFactory());

            Mocker.GetMock<NzbDrone.Core.MetadataSource.Goodreads.IGoodreadsSearchProxy>()
                .Setup(x => x.Search(It.IsAny<string>()))
                .Returns(searchResults);

            var body = Newtonsoft.Json.JsonConvert.SerializeObject(new BulkBookResource { Works = works, Authors = authors });

            Mocker.GetMock<IHttpClient>()
                .Setup(x => x.Post<BulkBookResource>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(r => new HttpResponse<BulkBookResource>(new HttpResponse(r, new HttpHeader(), body)));
        }

        private static NzbDrone.Core.MetadataSource.Goodreads.SearchJsonResource SearchResult(int workId, int bookId, int authorId)
        {
            return new NzbDrone.Core.MetadataSource.Goodreads.SearchJsonResource
            {
                WorkId = workId,
                BookId = bookId,
                Author = new NzbDrone.Core.MetadataSource.Goodreads.AuthorJsonResource { Id = authorId }
            };
        }

        // Work with one edition and its authors listed on the work itself, with no top-level bulk authors.
        private static WorkResource SparseWork(int workId, int editionId, int[] authorIds, string authorName)
        {
            return new WorkResource
            {
                ForeignId = workId,
                Title = $"Work {workId}",
                Books = new List<BookResource> { new BookResource { ForeignId = editionId, Title = $"Work {workId}" } },
                Authors = authorIds.Select(id => new AuthorResource { ForeignId = id, Name = authorName }).ToList()
            };
        }

        // A work that redirects to an author page omitting it is an unconfirmed absence, not a 404.
        [Test]
        public void should_report_fetch_failure_not_not_found_when_redirected_author_page_omits_the_work()
        {
            Mocker.GetMock<IMetadataRequestBuilder>()
                .Setup(x => x.GetRequestBuilder())
                .Returns(new HttpRequestBuilder("http://bookinfo.test/{route}").CreateFactory());

            var redirect = new HttpHeader { { "Location", "http://bookinfo.test/author/1" } };

            Mocker.GetMock<IHttpClient>()
                .Setup(x => x.Get(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(r => new HttpResponse(r, redirect, string.Empty, HttpStatusCode.Found));

            Mocker.GetMock<ICachedHttpResponseService>()
                .Setup(x => x.Get(It.IsAny<HttpRequest>(), It.IsAny<bool>(), It.IsAny<TimeSpan>()))
                .Returns<HttpRequest, bool, TimeSpan>((r, useCache, ttl) => new HttpResponse(
                    r,
                    new HttpHeader(),
                    "{\"ForeignId\":1,\"Name\":\"Eric Flint\",\"Works\":[{\"ForeignId\":11,\"Title\":\"Other\"}],\"Series\":[]}"));

            var method = typeof(BookInfoProxy).GetMethod("PollBook", BindingFlags.NonPublic | BindingFlags.Instance);
            var thrown = Assert.Throws<TargetInvocationException>(() => method.Invoke(Subject, new object[] { "10" }));

            thrown.InnerException.Should().BeOfType<BookInfoException>();
        }

        [Test]
        public void should_map_series_links_when_link_uses_edition_id()
        {
            var authorResource = new AuthorResource
            {
                ForeignId = 14168090,
                Name = "Jason Anspach",
                Works = new List<WorkResource>
                {
                    new WorkResource
                    {
                        ForeignId = 284588635,
                        Title = "Fracture",
                        Authors = new List<AuthorResource>
                        {
                            new AuthorResource { ForeignId = 14168090, Name = "Jason Anspach" }
                        },
                        Books = new List<BookResource>
                        {
                            new BookResource
                            {
                                ForeignId = 249509643,
                                Title = "Fracture",
                                Contributors = new List<ContributorResource>
                                {
                                    new ContributorResource { ForeignId = 14168090, Role = "Author" }
                                }
                            }
                        }
                    }
                },
                Series = new List<SeriesResource>
                {
                    new SeriesResource
                    {
                        ForeignId = 213397,
                        Title = "Galaxy's Edge",
                        LinkItems = new List<SeriesWorkLinkResource>
                        {
                            new SeriesWorkLinkResource
                            {
                                ForeignWorkId = 249509643,
                                PositionInSeries = "25",
                                SeriesPosition = 0,
                                Primary = true
                            }
                        }
                    }
                }
            };

            var author = InvokePrivateStatic<Author>("MapAuthor", authorResource);

            author.Books.Value.Should().HaveCount(1);
            author.Books.Value[0].ForeignBookId.Should().Be("284588635");
            author.Books.Value[0].SeriesLinks.Value.Should().HaveCount(1);
            author.Books.Value[0].SeriesLinks.Value[0].Position.Should().Be("25");
            author.Books.Value[0].SeriesLinks.Value[0].Series.Value.ForeignSeriesId.Should().Be("213397");
        }

        private static T InvokePrivateStatic<T>(string methodName, params object[] args)
        {
            var method = typeof(BookInfoProxy).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
            method.Should().NotBeNull();

            return (T)method.Invoke(null, args);
        }
    }
}
