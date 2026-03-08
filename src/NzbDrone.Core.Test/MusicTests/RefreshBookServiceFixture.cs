using System;
using System.Collections.Generic;
using System.Reflection;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MusicTests
{
    [TestFixture]
    public class RefreshBookServiceFixture : CoreTest<RefreshBookService>
    {
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

            var method = typeof(RefreshBookService).GetMethod("GetSkyhookData", BindingFlags.NonPublic | BindingFlags.Instance);
            var result = (Author)method.Invoke(Subject, new object[] { localBook });

            result.Should().NotBeNull();
            result.Should().BeSameAs(localAuthor);
            result.Books.Should().HaveCount(1);
            result.Books[0].AuthorMetadataId.Should().Be(localBook.AuthorMetadataId);
            result.Books[0].Author.Should().BeSameAs(localAuthor);
            result.Books[0].AuthorMetadata.Value.ForeignAuthorId.Should().Be(localAuthor.ForeignAuthorId);
        }
    }
}
