using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NLog;
using NUnit.Framework;
using NzbDrone.Core.Books;

namespace NzbDrone.Core.Test.MusicTests
{
    [TestFixture]
    public class EquivalentBookMergeHelperFixture
    {
        private static readonly Logger Logger = LogManager.GetLogger("EquivalentBookMergeHelperFixture");

        [Test]
        public void should_collapse_equivalent_remote_books_and_rewrite_series_links()
        {
            var audioBook = CreateBook(11, "1", "Ground State", new DateTime(2026, 1, 27), "Audible Audio");
            var kindleBook = CreateBook(12, "2", "Ground State", new DateTime(2026, 1, 27), "Kindle Edition");

            var author = CreateAuthor(audioBook, kindleBook);
            var series = new Series { ForeignSeriesId = "series-1", Title = "Expeditionary Force" };
            series.LinkItems = new List<SeriesBookLink>
            {
                new SeriesBookLink
                {
                    Series = series,
                    Book = kindleBook,
                    Position = "19",
                    IsPrimary = true
                }
            };
            author.Series = new List<Series> { series };

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

            EquivalentBookMergeHelper.CollapseEquivalentRemoteBooks(author, localBooks, fileCounts, Logger);

            author.Books.Value.Should().HaveCount(1);
            author.Books.Value[0].ForeignBookId.Should().Be("1");
            author.Books.Value[0].Editions.Value.Select(x => x.ForeignEditionId).Should().BeEquivalentTo(new[] { "1-edition", "2-edition" });
            author.Books.Value[0].RelatedBooks.Should().Contain(2);
            author.Series.Value[0].LinkItems.Value.Should().ContainSingle();
            author.Series.Value[0].LinkItems.Value[0].Book.Value.ForeignBookId.Should().Be("1");
            author.Books.Value[0].SeriesLinks.Value.Should().ContainSingle();
            author.Books.Value[0].SeriesLinks.Value[0].Position.Should().Be("19");
        }

        [Test]
        public void should_not_collapse_remote_books_when_series_positions_conflict()
        {
            var first = CreateBook(11, "1", "A Storm of Swords", new DateTime(2000, 8, 8), "Audiobook", "5");
            var second = CreateBook(12, "2", "A Storm of Swords", new DateTime(2000, 8, 8), "Mass Market Paperback", "3");
            var author = CreateAuthor(first, second);
            var fileCounts = new Dictionary<int, int> { [101] = 1, [102] = 0 };
            var localBooks = new List<Book>
            {
                CreateBook(101, "1", "A Storm of Swords", new DateTime(2000, 8, 8), "Audiobook", "5"),
                CreateBook(102, "2", "A Storm of Swords", new DateTime(2000, 8, 8), "Mass Market Paperback", "3")
            };

            EquivalentBookMergeHelper.CollapseEquivalentRemoteBooks(author, localBooks, fileCounts, Logger);

            author.Books.Value.Should().HaveCount(2);
        }

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
        public void should_match_equivalent_remote_book_by_related_book_id()
        {
            var localBook = CreateBook(102, "2", "Ground State", new DateTime(2026, 1, 27), "Kindle Edition");
            var canonical = CreateBook(101, "1", "Ground State", new DateTime(2026, 1, 27), "Audible Audio");
            canonical.RelatedBooks.Add(2);

            var match = EquivalentBookMergeHelper.FindMatchingRemoteBook(localBook, new List<Book> { canonical });

            match.Should().NotBeNull();
            match.ForeignBookId.Should().Be("1");
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
