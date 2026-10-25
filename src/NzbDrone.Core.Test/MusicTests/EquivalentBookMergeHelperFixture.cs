using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;

namespace NzbDrone.Core.Test.MusicTests
{
    [TestFixture]
    public class EquivalentBookMergeHelperFixture
    {
        [Test]
        public void should_merge_empty_local_duplicate_into_file_backed_match()
        {
            var remote = CreateBook(0, "1", "Ground State", new DateTime(2026, 1, 27), "Audible Audio");
            remote.RelatedBooks.Add(2);

            var localBooks = new List<Book>
            {
                CreateBook(101, "1", "Ground State", new DateTime(2026, 1, 27), "Audible Audio"),
                CreateBook(102, "2", "Ground State", new DateTime(2026, 1, 27), "Kindle Edition")
            };
            var fileCounts = new Dictionary<int, int>
            {
                [101] = 1,
                [102] = 0
            };

            var result = EquivalentBookMergeHelper.FindMatchingLocalBook(localBooks, remote, fileCounts);

            result.Item1.Id.Should().Be(101);
            result.Item2.Select(x => x.Id).Should().BeEquivalentTo(new[] { 102 });
        }

        [Test]
        public void should_not_match_distinct_related_work_with_same_title_and_date()
        {
            var localBook = CreateBook(102, "2", "Ground State", new DateTime(2026, 1, 27), "Kindle Edition");
            var canonical = CreateBook(101, "1", "Ground State", new DateTime(2026, 1, 27), "Audible Audio");
            canonical.RelatedBooks.Add(2);

            var match = EquivalentBookMergeHelper.FindMatchingRemoteBook(localBook, new List<Book> { canonical });

            match.Should().BeNull();
        }

        [Test]
        public void should_not_match_local_work_reached_only_through_related_link()
        {
            var localBook = CreateBook(201, "1", "Ground State", new DateTime(2026, 1, 27), "Audible Audio");
            var remoteBook = CreateBook(202, "2", "Ground State", new DateTime(2026, 1, 27), "Audible Audio");
            remoteBook.RelatedBooks.Add(1);

            var result = EquivalentBookMergeHelper.FindMatchingLocalBook(new List<Book> { localBook }, remoteBook, new Dictionary<int, int>());

            result.Item1.Should().BeNull();
        }

        private static Author CreateAuthor(params Book[] books)
        {
            var metadata = new AuthorMetadata
            {
                Id = 7,
                ForeignAuthorId = "author-1",
                Name = "Craig Alanson"
            };

            var author = new Author
            {
                Id = 5,
                AuthorMetadataId = metadata.Id,
                Metadata = metadata,
                Books = books.ToList(),
                Series = new List<Series>()
            };

            foreach (var book in books)
            {
                book.Author = author;
                book.AuthorMetadata = metadata;
                book.AuthorMetadataId = metadata.Id;
            }

            return author;
        }

        private static Book CreateBook(int id, string foreignBookId, string title, DateTime releaseDate, string format, string seriesPosition = null)
        {
            var book = new Book
            {
                Id = id,
                ForeignBookId = foreignBookId,
                Title = title,
                CleanTitle = Parser.Parser.CleanAuthorName(title),
                ReleaseDate = releaseDate,
                Editions = new List<Edition>
                {
                    new Edition
                    {
                        ForeignEditionId = foreignBookId + "-edition",
                        Format = format,
                        Monitored = true,
                        Ratings = new Ratings { Votes = 10, Value = 4 }
                    }
                },
                SeriesLinks = new List<SeriesBookLink>(),
                Ratings = new Ratings { Votes = 10, Value = 4 },
                Added = releaseDate
            };

            if (seriesPosition != null)
            {
                var series = new Series { ForeignSeriesId = "series-1", Title = "Series" };
                book.SeriesLinks = new List<SeriesBookLink>
                {
                    new SeriesBookLink
                    {
                        Series = series,
                        Book = book,
                        Position = seriesPosition,
                        IsPrimary = true
                    }
                };
            }

            return book;
        }
    }
}
