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

        private static T InvokePrivateStatic<T>(string methodName, params object[] args)
        {
            var method = typeof(BookInfoProxy).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
            method.Should().NotBeNull();

            return (T)method.Invoke(null, args);
        }
    }
}
