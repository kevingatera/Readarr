using System;
using System.Collections.Generic;
using System.Reflection;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MetadataSource.BookInfo;

namespace NzbDrone.Core.Test.MetadataSource.Goodreads
{
    [TestFixture]
    public class BookInfoProxyMappingFixture
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

        [Test]
        public void should_find_missing_series_work_ids_not_present_in_author_works()
        {
            var authorResource = new AuthorResource
            {
                ForeignId = 1,
                Name = "Author",
                Works = new List<WorkResource>
                {
                    new WorkResource { ForeignId = 10, Title = "Known Work" }
                },
                Series = new List<SeriesResource>
                {
                    new SeriesResource
                    {
                        ForeignId = 50,
                        Title = "Series",
                        LinkItems = new List<SeriesWorkLinkResource>
                        {
                            new SeriesWorkLinkResource { ForeignWorkId = 10, PositionInSeries = "1", SeriesPosition = 1, Primary = true },
                            new SeriesWorkLinkResource { ForeignWorkId = 20, PositionInSeries = "2", SeriesPosition = 2, Primary = true },
                            new SeriesWorkLinkResource { ForeignWorkId = 0, PositionInSeries = "x", SeriesPosition = 0, Primary = false }
                        }
                    }
                }
            };

            var missing = InvokePrivateStatic<List<int>>("GetMissingSeriesWorkIds", authorResource);

            missing.Should().BeEquivalentTo(new[] { 20 });
        }

        [Test]
        public void should_merge_only_series_supplemental_works_for_the_same_author()
        {
            var authorResource = new AuthorResource
            {
                ForeignId = 1,
                Name = "Author",
                Works = new List<WorkResource>
                {
                    new WorkResource
                    {
                        ForeignId = 10,
                        Title = "Known Work",
                        Authors = new List<AuthorResource>
                        {
                            new AuthorResource { ForeignId = 1, Name = "Author" }
                        }
                    }
                },
                Series = new List<SeriesResource>()
            };

            var supplemental = new List<WorkResource>
            {
                new WorkResource
                {
                    ForeignId = 20,
                    Title = "Series Missing Work",
                    Authors = new List<AuthorResource>
                    {
                        new AuthorResource { ForeignId = 1, Name = "Author" }
                    }
                },
                new WorkResource
                {
                    ForeignId = 30,
                    Title = "Other Author Work",
                    Authors = new List<AuthorResource>
                    {
                        new AuthorResource { ForeignId = 2, Name = "Someone Else" }
                    }
                }
            };

            InvokePrivateStatic<object>("MergeSeriesSupplementalWorks", authorResource, supplemental, 1);

            authorResource.Works.Should().HaveCount(2);
            authorResource.Works.Should().Contain(x => x.ForeignId == 10);
            authorResource.Works.Should().Contain(x => x.ForeignId == 20);
            authorResource.Works.Should().NotContain(x => x.ForeignId == 30);
        }

        private static T InvokePrivateStatic<T>(string methodName, params object[] args)
        {
            var method = typeof(BookInfoProxy).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
            method.Should().NotBeNull();

            return (T)method.Invoke(null, args);
        }
    }
}
