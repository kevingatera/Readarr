using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.BookInfo;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MusicTests
{
    [TestFixture]
    public class RefreshBookServiceFixture : CoreTest<RefreshBookService>
    {
        [Test]
        public void should_monitor_the_file_bearing_edition_when_the_selected_edition_has_no_files()
        {
            var selected = new Edition { Id = 1, Monitored = true, Ratings = new Ratings() };
            var fileBearing = new Edition { Id = 2, Monitored = false, Ratings = new Ratings() };
            var children = new RefreshEntityServiceBase<Book, Edition>.SortedChildren
            {
                UpToDate = new List<Edition> { selected, fileBearing }
            };

            Mocker.GetMock<IMediaFileService>()
                .Setup(x => x.GetFilesByEdition(selected.Id))
                .Returns(new List<BookFile>());
            Mocker.GetMock<IMediaFileService>()
                .Setup(x => x.GetFilesByEdition(fileBearing.Id))
                .Returns(new List<BookFile> { new BookFile() });

            var method = typeof(RefreshBookService).GetMethod("MonitorSingleEdition", BindingFlags.NonPublic | BindingFlags.Instance);
            method.Invoke(Subject, new object[] { children });

            selected.Monitored.Should().BeFalse();
            fileBearing.Monitored.Should().BeTrue();
            children.Updated.Should().BeEquivalentTo(new[] { selected, fileBearing });
        }

        [Test]
        public void should_fallback_to_local_author_when_remote_author_fetch_fails()
        {
            var localAuthorMetadata = new AuthorMetadata
            {
                Id = 12,
                ForeignAuthorId = "7044164",
                Name = "Scott Meyer"
            };

            var localAuthor = new Author
            {
                Id = 34,
                AuthorMetadataId = 12,
                Metadata = localAuthorMetadata
            };

            var localBook = new Book
            {
                Id = 56,
                Title = "An Unwelcome Quest",
                ForeignBookId = "42792043",
                AuthorMetadataId = 12,
                Author = localAuthor,
                AuthorMetadata = localAuthorMetadata
            };

            var remoteBook = new Book
            {
                Title = localBook.Title,
                ForeignBookId = localBook.ForeignBookId
            };

            Mocker.GetMock<IProvideBookInfo>()
                .Setup(x => x.GetBookInfo(localBook.ForeignBookId))
                .Returns(Tuple.Create(localAuthor.ForeignAuthorId, remoteBook, new List<AuthorMetadata>()));

            Mocker.GetMock<IProvideAuthorInfo>()
                .Setup(x => x.GetAuthorInfo(localAuthor.ForeignAuthorId, true))
                .Throws(new BookInfoException("Unexpected error fetching author data"));

            Mocker.GetMock<IAuthorService>()
                .Setup(x => x.GetAuthor(localAuthor.Id))
                .Returns(localAuthor);

            Mocker.GetMock<IBookService>()
                .Setup(x => x.GetBooksByAuthorMetadataId(localBook.AuthorMetadataId))
                .Returns(new List<Book>());

            Mocker.GetMock<IMediaFileService>()
                .Setup(x => x.GetFilesByAuthorMetadataId(localBook.AuthorMetadataId))
                .Returns(new List<BookFile>());

            var method = typeof(RefreshBookService).GetMethod("GetSkyhookData", BindingFlags.NonPublic | BindingFlags.Instance);
            var result = (Author)method.Invoke(Subject, new object[] { localBook });

            ExceptionVerification.ExpectedWarns(1);

            result.Should().NotBeNull();
            result.Should().BeSameAs(localAuthor);
            result.Books.Value.Should().HaveCount(1);
            result.Books.Value[0].AuthorMetadataId.Should().Be(localBook.AuthorMetadataId);
            result.Books.Value[0].Author.Value.Should().BeSameAs(localAuthor);
            result.Books.Value[0].AuthorMetadata.Value.ForeignAuthorId.Should().Be(localAuthor.ForeignAuthorId);
        }

        // Regression: a file-less book absent from a degraded author payload used
        // to be deleted without a per-book lookup. GetRemoteData must now attempt a
        // direct per-book fetch (GetSkyhookData -> GetBookInfo) so that a book which
        // genuinely exists upstream is preserved rather than treated as removed.
        [Test]
        public void should_attempt_per_book_lookup_when_book_missing_from_remote_payload()
        {
            var localAuthorMetadata = new AuthorMetadata
            {
                Id = 12,
                ForeignAuthorId = "7044164",
                Name = "Scott Meyer"
            };

            var localAuthor = new Author
            {
                Id = 34,
                AuthorMetadataId = 12,
                Metadata = localAuthorMetadata
            };

            var localBook = new Book
            {
                Id = 56,
                Title = "An Unwelcome Quest",
                ForeignBookId = "42792043",
                AuthorMetadataId = 12,
                Author = localAuthor,
                AuthorMetadata = localAuthorMetadata
            };

            // The remote author payload does NOT contain this book (simulating a
            // degraded / truncated fetch). The per-book lookup DOES find it.
            var remoteBook = new Book
            {
                Title = localBook.Title,
                ForeignBookId = localBook.ForeignBookId
            };

            Mocker.GetMock<IProvideBookInfo>()
                .Setup(x => x.GetBookInfo(localBook.ForeignBookId))
                .Returns(Tuple.Create(localAuthor.ForeignAuthorId, remoteBook, new List<AuthorMetadata>()));

            Mocker.GetMock<IProvideAuthorInfo>()
                .Setup(x => x.GetAuthorInfo(localAuthor.ForeignAuthorId, true))
                .Returns(localAuthor);

            Mocker.GetMock<IAuthorService>()
                .Setup(x => x.GetAuthor(localAuthor.Id))
                .Returns(localAuthor);

            // GetSkyhookData reads the author's local books and file counts to
            // collapse equivalent remote books; provide empty collections so the
            // auto-mocks do not return null and trip LINQ.
            Mocker.GetMock<IBookService>()
                .Setup(x => x.GetBooksByAuthorMetadataId(localBook.AuthorMetadataId))
                .Returns(new List<Book>());

            Mocker.GetMock<IMediaFileService>()
                .Setup(x => x.GetFilesByAuthorMetadataId(localBook.AuthorMetadataId))
                .Returns(new List<BookFile>());

            // Pass an empty remote list (book missing from payload). GetRemoteData
            // should still resolve the book via the per-book fetch.
            var method = typeof(RefreshBookService).GetMethod("GetRemoteData", BindingFlags.NonPublic | BindingFlags.Instance);
            var result = (RefreshEntityServiceBase<Book, Edition>.RemoteData)method.Invoke(Subject, new object[] { localBook, new List<Book>(), null });

            result.Entity.Should().NotBeNull();
            result.Entity.ForeignBookId.Should().Be(localBook.ForeignBookId);

            // The per-book fetch must have been invoked (the fix's core behavior)
            Mocker.GetMock<IProvideBookInfo>()
                .Verify(x => x.GetBookInfo(localBook.ForeignBookId), Times.Once());
        }

        // Sanity: when a book IS present in the unfiltered author payload but was
        // removed from the filtered remote list (metadata profile / exclusions),
        // GetRemoteData must NOT do a per-book fetch - the filter should be honored
        // and the book allowed to be deleted normally. This preserves the
        // metadata-profile semantics that the broader Fix D would otherwise defeat.
        [Test]
        public void should_not_per_book_lookup_when_book_filtered_out_by_profile()
        {
            var localAuthorMetadata = new AuthorMetadata
            {
                Id = 12,
                ForeignAuthorId = "7044164",
                Name = "Scott Meyer"
            };

            var localAuthor = new Author
            {
                Id = 34,
                AuthorMetadataId = 12,
                Metadata = localAuthorMetadata
            };

            var localBook = new Book
            {
                Id = 56,
                Title = "An Unwelcome Quest",
                ForeignBookId = "42792043",
                AuthorMetadataId = 12,
                Author = localAuthor,
                AuthorMetadata = localAuthorMetadata
            };

            // The book IS in the unfiltered author payload (so it was intentionally
            // filtered out of the remote list by the metadata profile / exclusions).
            var unfilteredBook = new Book
            {
                Title = localBook.Title,
                ForeignBookId = localBook.ForeignBookId
            };

            var unfilteredAuthor = new Author
            {
                Metadata = localAuthorMetadata,
                Books = new List<Book> { unfilteredBook }
            };

            var method = typeof(RefreshBookService).GetMethod("GetRemoteData", BindingFlags.NonPublic | BindingFlags.Instance);

            // Pass an empty filtered remote list but a non-null unfiltered author
            // payload containing the book. GetRemoteData should NOT call GetBookInfo.
            var result = (RefreshEntityServiceBase<Book, Edition>.RemoteData)method.Invoke(Subject, new object[] { localBook, new List<Book>(), unfilteredAuthor });

            result.Entity.Should().BeNull();
            Mocker.GetMock<IProvideBookInfo>()
                .Verify(x => x.GetBookInfo(It.IsAny<string>()), Times.Never());
        }

        // Full refresh path for one local book: the lookup decides, nothing else.
        [Test]
        public void should_delete_file_less_book_when_direct_lookup_is_404()
        {
            var book = ArrangeLocalBook(fileCount: 0);
            Mocker.GetMock<IProvideBookInfo>()
                .Setup(x => x.GetBookInfo(book.ForeignBookId))
                .Throws(new BookNotFoundException(book.ForeignBookId));

            Subject.RefreshBookInfo(book, new List<Book>(), UnfilteredAuthor(new List<Book>()), false);

            // 404 is logged as an error by the lookup, and the deletion as a warning by design.
            ExceptionVerification.ExpectedErrors(1);
            ExceptionVerification.ExpectedWarns(1);
            Mocker.GetMock<IBookService>().Verify(x => x.DeleteBook(book.Id, false, false), Times.Once());
        }

        [Test]
        public void should_keep_book_with_files_when_direct_lookup_is_404()
        {
            var book = ArrangeLocalBook(fileCount: 1);
            Mocker.GetMock<IProvideBookInfo>()
                .Setup(x => x.GetBookInfo(book.ForeignBookId))
                .Throws(new BookNotFoundException(book.ForeignBookId));

            Subject.RefreshBookInfo(book, new List<Book>(), UnfilteredAuthor(new List<Book>()), false);

            // Lookup 404 is logged, and the refresh logs that the book was not found but keeps it because it has files.
            ExceptionVerification.ExpectedErrors(2);
            Mocker.GetMock<IBookService>().Verify(x => x.DeleteBook(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void should_abort_refresh_without_deleting_when_direct_lookup_is_rate_limited()
        {
            var book = ArrangeLocalBook(fileCount: 0);
            Mocker.GetMock<IProvideBookInfo>()
                .Setup(x => x.GetBookInfo(book.ForeignBookId))
                .Throws(new BookInfoException("429 from metadata source"));

            Assert.Throws<BookInfoException>(() => Subject.RefreshBookInfo(book, new List<Book>(), UnfilteredAuthor(new List<Book>()), false));

            Mocker.GetMock<IBookService>().Verify(x => x.DeleteBook(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void should_delete_profile_filtered_book_without_lookup()
        {
            var book = ArrangeLocalBook(fileCount: 0);

            // Listed in the unfiltered payload, so the metadata profile removed it on purpose.
            Subject.RefreshBookInfo(book, new List<Book>(), UnfilteredAuthor(new List<Book> { new Book { ForeignBookId = book.ForeignBookId } }), false);

            ExceptionVerification.ExpectedWarns(1);
            Mocker.GetMock<IProvideBookInfo>().Verify(x => x.GetBookInfo(It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IBookService>().Verify(x => x.DeleteBook(book.Id, false, false), Times.Once());
        }

        private Book ArrangeLocalBook(int fileCount)
        {
            var metadata = new AuthorMetadata { Id = 12, ForeignAuthorId = "7044164", Name = "Scott Meyer" };
            var author = new Author { Id = 34, AuthorMetadataId = 12, Metadata = metadata };
            var book = new Book
            {
                Id = 56,
                Title = "An Unwelcome Quest",
                ForeignBookId = "42792043",
                AuthorId = 34,
                AuthorMetadataId = 12,
                Author = author,
                AuthorMetadata = metadata
            };

            Mocker.GetMock<IMediaFileService>()
                .Setup(x => x.GetFilesByBook(book.Id))
                .Returns(Enumerable.Range(0, fileCount).Select(_ => new BookFile()).ToList());

            return book;
        }

        private static Author UnfilteredAuthor(List<Book> books)
        {
            return new Author
            {
                Metadata = new AuthorMetadata { Id = 12, ForeignAuthorId = "7044164", Name = "Scott Meyer" },
                Books = books
            };
        }
    }
}
