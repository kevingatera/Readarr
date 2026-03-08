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

        private static T InvokePrivateStatic<T>(string methodName, params object[] args)
        {
            var method = typeof(BookInfoProxy).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
            method.Should().NotBeNull();

            return (T)method.Invoke(null, args);
        }
    }
}
