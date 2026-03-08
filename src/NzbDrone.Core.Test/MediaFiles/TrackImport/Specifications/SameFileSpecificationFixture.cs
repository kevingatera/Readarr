using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport.Specifications;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.BookImport.Specifications
{
    [TestFixture]
    public class SameFileSpecificationFixture : CoreTest<SameFileSpecification>
    {
        private const long FileSize = 123456789;
        private string _path;
        private LocalBook _localBook;

        [SetUp]
        public void Setup()
        {
            _path = @"C:\Test\Author\Book\Author - Book.m4b".AsOsAgnostic();

            _localBook = new LocalBook
            {
                Path = _path,
                Size = FileSize,
                ExistingFile = true,
                Book = new Book
                {
                    BookFiles = new List<BookFile>
                    {
                        new BookFile
                        {
                            Path = _path,
                            Size = FileSize
                        }
                    }
                }
            };
        }

        [Test]
        public void should_reject_same_size_existing_file_by_default()
        {
            Subject.IsSatisfiedBy(_localBook, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_accept_same_path_existing_file_when_same_file_match_is_allowed()
        {
            _localBook.AllowSameFileMatch = true;

            Subject.IsSatisfiedBy(_localBook, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_still_reject_same_size_different_path_when_same_file_match_is_allowed()
        {
            _localBook.AllowSameFileMatch = true;
            _localBook.Book.BookFiles = new List<BookFile>
            {
                new BookFile
                {
                    Path = @"C:\Test\Author\Book\Author - Book copy.m4b".AsOsAgnostic(),
                    Size = FileSize
                }
            };

            Subject.IsSatisfiedBy(_localBook, null).Accepted.Should().BeFalse();
        }
    }
}
