using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.ImportLists.Exclusions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.BookInfo;
using NzbDrone.Core.Profiles.Metadata;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MusicTests
{
    // An author payload can be partial. These tests run the real RefreshBookService under
    // RefreshAuthorService, so a book the payload omits is deleted only after a direct 404.
    [TestFixture]
    public class RefreshAuthorServiceDegradedFixture : CoreTest<RefreshAuthorService>
    {
        private AuthorMetadata _metadata;
        private Author _local;
        private List<Book> _localBooks;

        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<IRefreshBookService>(Mocker.Resolve<RefreshBookService>());

            _metadata = new AuthorMetadata { Id = 12, ForeignAuthorId = "7044164", Name = "Scott Meyer" };
            _local = new Author
            {
                Id = 34,
                AuthorMetadataId = 12,
                Metadata = _metadata,
                Path = System.IO.Path.GetTempPath()
            };

            _localBooks = new List<Book>();

            Mocker.GetMock<IBookService>()
                .Setup(x => x.GetBooksForRefresh(_local.AuthorMetadataId, It.IsAny<List<string>>()))
                .Returns(() => _localBooks);

            Mocker.GetMock<IMediaFileService>()
                .Setup(x => x.GetFilesByBook(It.IsAny<int>()))
                .Returns(new List<BookFile>());

            Mocker.GetMock<IMediaFileService>()
                .Setup(x => x.GetFilesByAuthor(It.IsAny<int>()))
                .Returns(new List<BookFile>());

            Mocker.GetMock<IEditionService>()
                .Setup(x => x.GetEditionsForRefresh(It.IsAny<int>(), It.IsAny<List<string>>()))
                .Returns(new List<Edition>());

            Mocker.GetMock<IImportListExclusionService>()
                .Setup(x => x.FindByForeignId(It.IsAny<List<string>>()))
                .Returns(new List<ImportListExclusion>());

            Mocker.GetMock<IAuthorService>()
                .Setup(x => x.FindById(_metadata.ForeignAuthorId))
                .Returns(_local);
        }

        [Test]
        public void should_delete_only_books_that_direct_lookup_confirms_absent_for_a_small_author()
        {
            var present = LocalBook(1, "Present", "101");
            var absent = LocalBook(2, "Absent", "102");

            // The payload lists only the first book. The second is omitted and its lookup is a 404.
            Mocker.GetMock<IMetadataProfileService>()
                .Setup(x => x.FilterBooks(It.IsAny<Author>(), It.IsAny<int>()))
                .Returns(new List<Book> { RemoteBook("101", "Present") });

            Mocker.GetMock<IProvideBookInfo>()
                .Setup(x => x.GetBookInfo("102"))
                .Throws(new BookNotFoundException("102"));

            Subject.RefreshEntityInfo(_local, new List<Author>(), RemoteAuthor(new List<Book> { RemoteBook("101", "Present") }), true, false, null);

            // The 404 is logged by the lookup, and the deletion is logged as a warning by design.
            ExceptionVerification.ExpectedErrors(1);
            ExceptionVerification.ExpectedWarns(1);
            Mocker.GetMock<IBookService>().Verify(x => x.DeleteBook(absent.Id, false, false), Times.Once());
            Mocker.GetMock<IBookService>().Verify(x => x.DeleteBook(present.Id, It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void should_abort_author_refresh_without_deleting_when_a_lookup_is_rate_limited()
        {
            LocalBook(1, "Present", "101");
            var limited = LocalBook(2, "Limited", "102");

            Mocker.GetMock<IMetadataProfileService>()
                .Setup(x => x.FilterBooks(It.IsAny<Author>(), It.IsAny<int>()))
                .Returns(new List<Book> { RemoteBook("101", "Present") });

            Mocker.GetMock<IProvideBookInfo>()
                .Setup(x => x.GetBookInfo("102"))
                .Throws(new BookInfoException("429 from metadata source"));

            Assert.Throws<BookInfoException>(() => Subject.RefreshEntityInfo(_local, new List<Author>(), RemoteAuthor(new List<Book> { RemoteBook("101", "Present") }), true, false, null));

            Mocker.GetMock<IBookService>().Verify(x => x.DeleteBook(limited.Id, It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void should_not_merge_unrelated_works_that_share_a_title()
        {
            var local = LocalBook(1, "Same Title", "201");

            // Same title, different work id. Exact work id identity means the local book is not merged into it.
            Mocker.GetMock<IMetadataProfileService>()
                .Setup(x => x.FilterBooks(It.IsAny<Author>(), It.IsAny<int>()))
                .Returns(new List<Book> { RemoteBook("202", "Same Title") });

            Mocker.GetMock<IProvideBookInfo>()
                .Setup(x => x.GetBookInfo("201"))
                .Throws(new BookNotFoundException("201"));

            Subject.RefreshEntityInfo(_local, new List<Author>(), RemoteAuthor(new List<Book> { RemoteBook("202", "Same Title") }), true, false, null);

            ExceptionVerification.ExpectedErrors(1);
            ExceptionVerification.ExpectedWarns(1);
            Mocker.GetMock<IBookService>().Verify(x => x.DeleteBook(local.Id, false, false), Times.Once());
            Mocker.GetMock<IBookService>().Verify(x => x.InsertMany(It.Is<List<Book>>(l => l.Count == 1 && l[0].ForeignBookId == "202")), Times.Once());
        }

        private Book LocalBook(int id, string title, string foreignBookId)
        {
            var book = new Book
            {
                Id = id,
                Title = title,
                ForeignBookId = foreignBookId,
                AuthorId = _local.Id,
                AuthorMetadataId = _local.AuthorMetadataId,
                Author = _local,
                AuthorMetadata = _metadata
            };

            _localBooks.Add(book);
            return book;
        }

        private Book RemoteBook(string foreignBookId, string title)
        {
            return new Book
            {
                ForeignBookId = foreignBookId,
                Title = title,
                AuthorMetadata = _metadata,
                Author = _local,
                Editions = new List<Edition>()
            };
        }

        private Author RemoteAuthor(List<Book> books)
        {
            return new Author
            {
                Metadata = _metadata,
                AuthorMetadataId = _metadata.Id,
                ForeignAuthorId = _metadata.ForeignAuthorId,
                Books = books,
                Series = new List<Series>()
            };
        }
    }
}
