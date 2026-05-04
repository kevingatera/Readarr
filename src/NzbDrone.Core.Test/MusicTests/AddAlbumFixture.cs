using System;
using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using FluentValidation;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MusicTests
{
    [TestFixture]
    public class AddBookFixture : CoreTest<AddBookService>
    {
        private Author _fakeAuthor;
        private Book _fakeBook;

        [SetUp]
        public void Setup()
        {
            _fakeAuthor = Builder<Author>
                .CreateNew()
                .With(s => s.Path = null)
                .With(s => s.Metadata = Builder<AuthorMetadata>.CreateNew().Build())
                .Build();
        }

        private void GivenValidBook(string bookId, string editionId)
        {
            _fakeBook = Builder<Book>
                .CreateNew()
                .With(x => x.Editions = Builder<Edition>
                      .CreateListOfSize(1)
                      .TheFirst(1)
                      .With(e => e.ForeignEditionId = editionId)
                      .With(e => e.Monitored = true)
                      .BuildList())
                .Build();

            Mocker.GetMock<IProvideBookInfo>()
                .Setup(s => s.GetBookInfo(bookId))
                .Returns(Tuple.Create(_fakeAuthor.Metadata.Value.ForeignAuthorId,
                                      _fakeBook,
                                      new List<AuthorMetadata> { _fakeAuthor.Metadata.Value }));

            Mocker.GetMock<IAddAuthorService>()
                .Setup(s => s.AddAuthor(It.IsAny<Author>(), It.IsAny<bool>()))
                .Returns(_fakeAuthor);
        }

        private void GivenValidPath()
        {
            Mocker.GetMock<IBuildFileNames>()
                  .Setup(s => s.GetAuthorFolder(It.IsAny<Author>(), null))
                  .Returns<Author, NamingConfig>((c, n) => c.Name);
        }

        private Book BookToAdd(string editionId, string bookId, string authorId)
        {
            return new Book
            {
                ForeignBookId = bookId,
                Editions = new List<Edition>
                {
                    new Edition
                    {
                        ForeignEditionId = editionId,
                        Monitored = true
                    }
                },
                AuthorMetadata = new AuthorMetadata
                {
                    ForeignAuthorId = authorId
                }
            };
        }

        [Test]
        public void should_be_able_to_add_a_book_without_passing_in_name()
        {
            var newBook = BookToAdd("edition", "book", "author");

            GivenValidBook("book", "edition");
            GivenValidPath();

            var book = Subject.AddBook(newBook);

            book.Title.Should().Be(_fakeBook.Title);
        }

        [Test]
        public void should_throw_if_book_cannot_be_found()
        {
            var newBook = BookToAdd("edition", "book", "author");

            Mocker.GetMock<IProvideBookInfo>()
                  .Setup(s => s.GetBookInfo("book"))
                  .Throws(new BookNotFoundException("edition"));

            Assert.Throws<ValidationException>(() => Subject.AddBook(newBook));

            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_be_able_to_add_a_book_with_only_foreign_edition_id()
        {
            var newBook = new Book
            {
                ForeignBookId = "book",
                ForeignEditionId = "edition",
                AuthorMetadata = new AuthorMetadata
                {
                    ForeignAuthorId = "author"
                }
            };

            GivenValidBook("book", "edition");
            GivenValidPath();

            var book = Subject.AddBook(newBook);

            book.Title.Should().Be(_fakeBook.Title);
            book.Editions.Value.Single(x => x.Monitored).ForeignEditionId.Should().Be("edition");
        }

        [Test]
        public void should_default_add_options_when_missing()
        {
            var newBook = BookToAdd("edition", "book", "author");
            newBook.AddOptions = null;

            GivenValidBook("book", "edition");
            GivenValidPath();

            var book = Subject.AddBook(newBook);

            book.AddOptions.Should().NotBeNull();
            book.AddOptions.AddType.Should().Be(BookAddType.Manual);
        }

        [Test]
        public void should_reuse_existing_equivalent_book_when_related_id_is_on_local_book()
        {
            var newBook = BookToAdd("247146569", "278243734", _fakeAuthor.Metadata.Value.ForeignAuthorId);

            _fakeBook = Builder<Book>
                .CreateNew()
                .With(x => x.ForeignBookId = "278243734")
                .With(x => x.Title = "Ground State")
                .With(x => x.CleanTitle = "groundstate")
                .With(x => x.ReleaseDate = new DateTime(2026, 1, 27))
                .With(x => x.RelatedBooks = new List<int>())
                .With(x => x.Editions = Builder<Edition>
                    .CreateListOfSize(1)
                    .TheFirst(1)
                    .With(e => e.ForeignEditionId = "247146569")
                    .With(e => e.Format = "Kindle Edition")
                    .With(e => e.Monitored = true)
                    .BuildList())
                .Build();

            var existingBook = Builder<Book>
                .CreateNew()
                .With(x => x.Id = 15103)
                .With(x => x.AuthorMetadataId = _fakeAuthor.AuthorMetadataId)
                .With(x => x.ForeignBookId = "261580815")
                .With(x => x.Title = "Ground State")
                .With(x => x.CleanTitle = "groundstate")
                .With(x => x.ReleaseDate = new DateTime(2026, 1, 27))
                .With(x => x.RelatedBooks = new List<int> { 278243734 })
                .With(x => x.Editions = Builder<Edition>
                    .CreateListOfSize(1)
                    .TheFirst(1)
                    .With(e => e.ForeignEditionId = "241936481")
                    .With(e => e.Format = "Audible Audio")
                    .With(e => e.Monitored = true)
                    .BuildList())
                .Build();

            Mocker.GetMock<IProvideBookInfo>()
                .Setup(s => s.GetBookInfo("278243734"))
                .Returns(Tuple.Create(_fakeAuthor.Metadata.Value.ForeignAuthorId,
                                      _fakeBook,
                                      new List<AuthorMetadata> { _fakeAuthor.Metadata.Value }));

            Mocker.GetMock<IAuthorService>()
                .Setup(s => s.FindById(_fakeAuthor.Metadata.Value.ForeignAuthorId))
                .Returns(_fakeAuthor);

            Mocker.GetMock<IBookService>()
                .Setup(s => s.GetBooksByAuthorMetadataId(_fakeAuthor.AuthorMetadataId))
                .Returns(new List<Book> { existingBook });

            var book = Subject.AddBook(newBook);

            book.Id.Should().Be(existingBook.Id);
            book.ForeignBookId.Should().Be(existingBook.ForeignBookId);

            Mocker.GetMock<IBookService>()
                .Verify(s => s.AddBook(It.IsAny<Book>(), It.IsAny<bool>()), Times.Never());
        }
    }
}
