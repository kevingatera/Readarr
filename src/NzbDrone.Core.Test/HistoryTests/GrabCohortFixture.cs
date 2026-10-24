using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.History;

namespace NzbDrone.Core.Test.HistoryTests
{
    [TestFixture]
    public class GrabCohortFixture
    {
        private static EntityHistory Grab(int bookId, DateTime date)
        {
            return new EntityHistory
            {
                EventType = EntityHistoryEventType.Grabbed,
                BookId = bookId,
                Date = date
            };
        }

        [Test]
        public void should_keep_every_book_from_a_grab_recorded_milliseconds_apart()
        {
            var now = DateTime.UtcNow;

            var cohort = GrabCohort.Latest(new List<EntityHistory>
            {
                Grab(6, now),
                Grab(5, now.AddMilliseconds(-3)),
                Grab(4, now.AddMilliseconds(-6))
            });

            cohort.Should().HaveCount(3);
        }

        [Test]
        public void should_ignore_earlier_grabs_of_the_same_download()
        {
            var now = DateTime.UtcNow;

            var cohort = GrabCohort.Latest(new List<EntityHistory>
            {
                Grab(6, now),
                Grab(5, now.AddMilliseconds(-3)),
                Grab(9, now.AddHours(-2)),
                Grab(8, now.AddHours(-2).AddMilliseconds(-2))
            });

            cohort.Should().HaveCount(2);
            cohort.Should().OnlyContain(h => h.BookId == 6 || h.BookId == 5);
        }

        [Test]
        public void should_ignore_rows_that_are_not_grabs()
        {
            var now = DateTime.UtcNow;

            var cohort = GrabCohort.Latest(new List<EntityHistory>
            {
                new EntityHistory { EventType = EntityHistoryEventType.BookFileImported, BookId = 3, Date = now },
                Grab(4, now.AddMilliseconds(-1))
            });

            cohort.Should().ContainSingle().Which.BookId.Should().Be(4);
        }

        [Test]
        public void should_return_empty_when_there_is_no_grab()
        {
            GrabCohort.Latest(new List<EntityHistory>()).Should().BeEmpty();
        }
    }
}
