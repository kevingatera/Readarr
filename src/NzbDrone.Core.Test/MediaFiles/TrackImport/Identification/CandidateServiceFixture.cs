using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.MediaFiles.BookImport.Identification;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.Goodreads;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.BookImport.Identification
{
    [TestFixture]
    public class CandidateServiceFixture : CoreTest<CandidateService>
    {
        [Test]
        public void should_not_throw_on_goodreads_exception()
        {
            Mocker.GetMock<ISearchForNewBook>()
                .Setup(s => s.SearchForNewBook(It.IsAny<string>(), It.IsAny<string>(), true))
                .Throws(new GoodreadsException("Bad search"));

            var edition = new LocalEdition
            {
                LocalBooks = new List<LocalBook>
                {
                    new LocalBook
                    {
                        FileTrackInfo = new ParsedTrackInfo
                        {
                            Authors = new List<string> { "Author" },
                            BookTitle = "Book"
                        }
                    }
                }
            };

            Subject.GetRemoteCandidates(edition, null).Should().BeEmpty();
        }

        [Test]
        public void should_use_folder_book_title_when_file_title_is_missing()
        {
            var author = new Author { AuthorMetadataId = 14 };
            var localEdition = new LocalEdition
            {
                LocalBooks = new List<LocalBook>
                {
                    new LocalBook
                    {
                        FileTrackInfo = new ParsedTrackInfo
                        {
                            Authors = new List<string> { "Jason Anspach" },
                            BookTitle = null
                        },
                        FolderTrackInfo = new ParsedBookInfo
                        {
                            AuthorName = "Jason Anspach",
                            BookTitle = "Galaxy's Edge, Part IV"
                        }
                    }
                }
            };

            Mocker.GetMock<IBookService>()
                .Setup(s => s.GetCandidates(author.AuthorMetadataId, It.IsAny<string>()))
                .Returns(new List<Book>());

            Mocker.GetMock<IEditionService>()
                .Setup(s => s.GetCandidates(author.AuthorMetadataId, It.IsAny<string>()))
                .Returns(new List<Edition>());

            Subject.GetDbCandidatesFromTags(localEdition, new IdentificationOverrides { Author = author }, false);

            Mocker.GetMock<IBookService>()
                .Verify(s => s.GetCandidates(author.AuthorMetadataId, It.Is<string>(x => x == "Galaxy's Edge, Part IV")), Times.AtLeastOnce());
        }

        [Test]
        public void should_use_series_position_candidates_for_part_titles()
        {
            var author = new Author { AuthorMetadataId = 14 };

            var book = new Book
            {
                Id = 508,
                Title = "Message for the Dead",
                SeriesLinks = new LazyLoaded<List<SeriesBookLink>>(new List<SeriesBookLink>
                {
                    new SeriesBookLink
                    {
                        Position = "4",
                        SeriesPosition = 4,
                        Series = new LazyLoaded<Series>(new Series { Title = "Galaxy's Edge" })
                    }
                })
            };

            var edition = new Edition
            {
                Id = 900,
                BookId = 508,
                Title = "Message for the Dead",
                Book = new LazyLoaded<Book>(book)
            };

            Mocker.GetMock<IBookService>()
                .Setup(s => s.GetCandidates(author.AuthorMetadataId, It.IsAny<string>()))
                .Returns(new List<Book>());

            Mocker.GetMock<IEditionService>()
                .Setup(s => s.GetCandidates(author.AuthorMetadataId, It.IsAny<string>()))
                .Returns(new List<Edition>());

            Mocker.GetMock<IBookService>()
                .Setup(s => s.GetBooksByAuthorMetadataId(author.AuthorMetadataId))
                .Returns(new List<Book> { book });

            Mocker.GetMock<IEditionService>()
                .Setup(s => s.GetEditionsByBook(508))
                .Returns(new List<Edition> { edition });

            var localEdition = new LocalEdition
            {
                LocalBooks = new List<LocalBook>
                {
                    new LocalBook
                    {
                        FileTrackInfo = new ParsedTrackInfo
                        {
                            Authors = new List<string> { "Jason Anspach" },
                            BookTitle = "Galaxy's Edge, Part IV"
                        }
                    }
                }
            };

            var result = Subject.GetDbCandidatesFromTags(localEdition, new IdentificationOverrides { Author = author }, false);

            result.Should().Contain(x => x.Edition.Id == 900);
        }

        [Test]
        public void should_use_track_title_when_book_tag_contains_series_prefix()
        {
            var author = new Author { AuthorMetadataId = 14 };
            var localEdition = new LocalEdition
            {
                LocalBooks = new List<LocalBook>
                {
                    new LocalBook
                    {
                        FileTrackInfo = new ParsedTrackInfo
                        {
                            Title = "Gods & Legionnaires",
                            BookTitle = "02 Gods & Legionnaires",
                            Authors = new List<string>
                            {
                                "Galaxy's Edge (Savage Wars)",
                                "Jason Anspach, Nick Cole"
                            }
                        }
                    }
                }
            };

            Mocker.GetMock<IBookService>()
                .Setup(s => s.GetCandidates(author.AuthorMetadataId, It.IsAny<string>()))
                .Returns(new List<Book>());

            Mocker.GetMock<IEditionService>()
                .Setup(s => s.GetCandidates(author.AuthorMetadataId, It.IsAny<string>()))
                .Returns(new List<Edition>());

            Subject.GetDbCandidatesFromTags(localEdition, new IdentificationOverrides { Author = author }, false);

            Mocker.GetMock<IBookService>()
                .Verify(s => s.GetCandidates(author.AuthorMetadataId, It.Is<string>(x => x == "Gods & Legionnaires")), Times.AtLeastOnce());
        }
    }
}
