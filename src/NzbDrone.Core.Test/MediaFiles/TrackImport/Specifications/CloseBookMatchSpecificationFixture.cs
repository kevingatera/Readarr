using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MediaFiles.BookImport.Identification;
using NzbDrone.Core.MediaFiles.BookImport.Specifications;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Test.MediaFiles.BookImport.Specifications
{
    [TestFixture]
    public class CloseBookMatchSpecificationFixture
    {
        private CloseBookMatchSpecification _subject;

        [SetUp]
        public void Setup()
        {
            _subject = new CloseBookMatchSpecification(NzbDroneLogger.GetLogger(typeof(CloseBookMatchSpecificationFixture)));
        }

        [Test]
        public void should_accept_close_new_download_when_only_series_part_pushes_it_over_threshold()
        {
            var distance = new Distance();
            distance.Add("source", 0.0);
            distance.Add("author", 0.05);
            distance.Add("book", 0.20);
            distance.Add("series_part", 0.50);
            distance.Add("asin_missing", 1.0);

            var item = new LocalEdition
            {
                NewDownload = true,
                Distance = distance
            };

            var decision = _subject.IsSatisfiedBy(item, null);

            decision.Accepted.Should().BeTrue();
        }

        [Test]
        public void should_reject_when_non_series_penalties_still_exceed_threshold()
        {
            var distance = new Distance();
            distance.Add("source", 0.0);
            distance.Add("author", 0.20);
            distance.Add("book", 0.80);
            distance.Add("series_part", 0.50);
            distance.Add("asin_missing", 1.0);

            var item = new LocalEdition
            {
                NewDownload = true,
                Distance = distance
            };

            var decision = _subject.IsSatisfiedBy(item, null);

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Contain("Book match is not close enough");
        }

        [Test]
        public void should_reject_close_series_mismatch_when_identifier_missing_signal_is_absent()
        {
            var distance = new Distance();
            distance.Add("source", 0.0);
            distance.Add("author", 0.03);
            distance.Add("book", 0.25);
            distance.Add("series_part", 0.50);

            var item = new LocalEdition
            {
                NewDownload = true,
                Distance = distance
            };

            var decision = _subject.IsSatisfiedBy(item, null);

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Contain("Book match is not close enough");
        }

        [Test]
        public void should_accept_metadata_poor_any_edition_match_with_strong_title_and_author()
        {
            var distance = new Distance();
            distance.Add("source", 0.0);
            distance.Add("author", 0.0);
            distance.Add("book", 0.0);
            distance.Add("isbn_missing", 1.0);
            distance.Add("asin_missing", 1.0);
            distance.Add("publisher", 0.0);
            distance.Add("ebook_format", 0.0);

            var item = new LocalEdition
            {
                NewDownload = true,
                Distance = distance,
                Edition = new Edition
                {
                    Book = new LazyLoaded<Book>(new Book { AnyEditionOk = true })
                }
            };

            var decision = _subject.IsSatisfiedBy(item, null);

            decision.Accepted.Should().BeTrue();
        }

        [Test]
        public void should_reject_metadata_poor_match_when_any_edition_is_disabled()
        {
            var distance = new Distance();
            distance.Add("author", 0.0);
            distance.Add("book", 0.0);
            distance.Add("isbn", 1.0);
            distance.Add("asin_missing", 1.0);
            distance.Add("publisher", 1.0);
            distance.Add("ebook_format", 1.0);

            var item = new LocalEdition
            {
                NewDownload = true,
                Distance = distance,
                Edition = new Edition
                {
                    Book = new LazyLoaded<Book>(new Book { AnyEditionOk = false })
                }
            };

            var decision = _subject.IsSatisfiedBy(item, null);

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Contain("Book match is not close enough");
        }

        [Test]
        public void should_reject_metadata_poor_match_with_hard_identity_mismatch()
        {
            var distance = new Distance();
            distance.Add("author", 0.0);
            distance.Add("book", 0.0);
            distance.Add("language", 1.0);
            distance.Add("isbn_missing", 1.0);
            distance.Add("publisher", 1.0);

            var item = new LocalEdition
            {
                NewDownload = true,
                Distance = distance,
                Edition = new Edition
                {
                    Book = new LazyLoaded<Book>(new Book { AnyEditionOk = true })
                }
            };

            var decision = _subject.IsSatisfiedBy(item, null);

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Contain("Book match is not close enough");
        }
    }
}
