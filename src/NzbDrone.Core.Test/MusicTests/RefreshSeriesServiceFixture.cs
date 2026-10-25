using System.Collections.Generic;
using System.Reflection;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MusicTests
{
    [TestFixture]
    public class RefreshSeriesServiceFixture : CoreTest<RefreshSeriesService>
    {
        private Series _local;
        private Author _author;
        private List<SeriesBookLink> _localLinks;

        [SetUp]
        public void Setup()
        {
            _author = new Author { Id = 34, AuthorMetadataId = 12, ForeignAuthorId = "7044164" };
            _local = new Series { Id = 7, ForeignSeriesId = "900", ForeignAuthorId = _author.ForeignAuthorId, Title = "Series" };

            _localLinks = new List<SeriesBookLink>
            {
                new SeriesBookLink { Id = 1, SeriesId = _local.Id, BookId = 1, Series = _local },
                new SeriesBookLink { Id = 2, SeriesId = _local.Id, BookId = 2, Series = _local }
            };

            Mocker.GetMock<ISeriesBookLinkService>()
                .Setup(x => x.GetLinksBySeriesAndAuthor(_local.Id, _author.ForeignAuthorId))
                .Returns(() => _localLinks);

            Mocker.GetMock<ISeriesBookLinkService>()
                .Setup(x => x.GetLinksBySeries(_local.Id))
                .Returns(() => _localLinks);
        }

        [Test]
        public void should_keep_links_that_a_partial_series_payload_omits()
        {
            // The payload links only book 1. Book 2's link is omitted, which is not proof it is obsolete.
            var remote = RemoteSeries(new List<SeriesBookLink> { new SeriesBookLink { BookId = 1, Book = new Book { Id = 1 } } });

            Subject.RefreshEntityInfo(_local, new List<Series> { remote }, _author, true, false, null);

            Mocker.GetMock<IRefreshSeriesBookLinkService>()
                .Verify(x => x.RefreshSeriesBookLinkInfo(
                    It.IsAny<List<SeriesBookLink>>(),
                    It.IsAny<List<SeriesBookLink>>(),
                    It.IsAny<List<System.Tuple<SeriesBookLink, SeriesBookLink>>>(),
                    It.Is<List<SeriesBookLink>>(d => d.Count == 0),
                    It.IsAny<List<SeriesBookLink>>(),
                    It.IsAny<List<SeriesBookLink>>(),
                    It.IsAny<bool>()),
                    Times.Once());
        }

        [Test]
        public void should_keep_series_with_links_when_payload_omits_the_series()
        {
            Subject.RefreshEntityInfo(_local, new List<Series>(), _author, true, false, null);

            // The series still has links, so it is kept and the omission is logged as an error.
            ExceptionVerification.ExpectedErrors(1);
            Mocker.GetMock<ISeriesService>().Verify(x => x.Delete(_local.Id), Times.Never());
        }

        [Test]
        public void should_allow_series_delete_only_when_no_links_remain()
        {
            var method = typeof(RefreshSeriesService).GetMethod("ShouldDelete", BindingFlags.NonPublic | BindingFlags.Instance);

            _localLinks = new List<SeriesBookLink>();
            ((bool)method.Invoke(Subject, new object[] { _local })).Should().BeTrue();

            _localLinks = new List<SeriesBookLink> { new SeriesBookLink { Id = 1, SeriesId = _local.Id, BookId = 1 } };
            ((bool)method.Invoke(Subject, new object[] { _local })).Should().BeFalse();
        }

        private static Series RemoteSeries(List<SeriesBookLink> links)
        {
            return new Series
            {
                ForeignSeriesId = "900",
                Title = "Series",
                LinkItems = links
            };
        }
    }
}
