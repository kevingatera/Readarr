using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MusicTests
{
    // Focused tests for the degraded-fetch guard on RefreshAuthorService.
    // The guard prevents an author refresh from deleting local books when the
    // remote payload is implausibly incomplete relative to the local set.
    [TestFixture]
    public class RefreshAuthorServiceDegradedFixture : CoreTest<RefreshAuthorService>
    {
        // RefreshAuthorService.IsRemoteChildrenDegraded is protected; expose it
        // through a tiny subclass so the heuristic can be unit tested directly.
        private class TestableRefreshAuthorService : RefreshAuthorService
        {
            public TestableRefreshAuthorService()
                : base(null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null)
            {
            }

            public bool PublicIsRemoteChildrenDegraded(Author entity, List<Book> localChildren, List<Book> remoteChildren)
            {
                return IsRemoteChildrenDegraded(entity, localChildren, remoteChildren);
            }
        }

        private TestableRefreshAuthorService _subject;
        private Author _author;

        [SetUp]
        public void Setup()
        {
            _subject = new TestableRefreshAuthorService();

            _author = Builder<Author>.CreateNew().Build();
        }

        private List<Book> Books(int count)
        {
            return Builder<Book>.CreateListOfSize(count).Build().ToList();
        }

        [Test]
        public void should_not_flag_degraded_when_remote_has_most_of_local()
        {
            var local = Books(10);

            // remote has 8 of 10 local books -> 80% retained, not degraded
            _subject.PublicIsRemoteChildrenDegraded(_author, local, Books(8)).Should().BeFalse();
        }

        [Test]
        public void should_flag_degraded_when_remote_has_far_fewer_than_local()
        {
            var local = Books(10);

            // remote has 2 of 10 local books -> 20% retained, implausible drop
            _subject.PublicIsRemoteChildrenDegraded(_author, local, Books(2)).Should().BeTrue();
        }

        [Test]
        public void should_flag_degraded_when_remote_is_empty_but_local_has_many_books()
        {
            var local = Books(20);

            // an empty remote with 20 local books is almost certainly a failed fetch
            _subject.PublicIsRemoteChildrenDegraded(_author, local, new List<Book>()).Should().BeTrue();
        }

        [Test]
        public void should_not_flag_degraded_for_small_authors_below_baseline()
        {
            // small/new authors legitimately have few books; the guard must not
            // fire for them or it would prevent normal cleanup of tiny libraries
            var local = Books(3);

            _subject.PublicIsRemoteChildrenDegraded(_author, local, new List<Book>()).Should().BeFalse();
        }

        [Test]
        public void should_flag_degraded_at_baseline_threshold_with_half_drop()
        {
            // exactly at the 5-book baseline with a 50% drop is the boundary;
            // 5 local / 2 remote is below 50% so it IS flagged (2 < 5*0.5 = 2.5)
            var local = Books(5);

            _subject.PublicIsRemoteChildrenDegraded(_author, local, Books(2)).Should().BeTrue();
        }

        [Test]
        public void should_not_flag_degraded_at_exact_50_percent_boundary()
        {
            // the comparison is strict <, so exactly 50% is NOT flagged
            var local = Books(10);

            _subject.PublicIsRemoteChildrenDegraded(_author, local, Books(5)).Should().BeFalse();
        }

        [Test]
        public void should_flag_degraded_for_large_author_with_massive_drop()
        {
            var local = Books(100);

            // 49 of 100 is below 50% so it IS flagged
            _subject.PublicIsRemoteChildrenDegraded(_author, local, Books(49)).Should().BeTrue();

            // 50 of 100 is exactly 50% so it is NOT flagged
            _subject.PublicIsRemoteChildrenDegraded(_author, local, Books(50)).Should().BeFalse();
        }

        [Test]
        public void should_not_flag_degraded_when_inputs_are_null()
        {
            _subject.PublicIsRemoteChildrenDegraded(_author, null, Books(5)).Should().BeFalse();
            _subject.PublicIsRemoteChildrenDegraded(_author, Books(10), null).Should().BeFalse();
        }
    }
}
