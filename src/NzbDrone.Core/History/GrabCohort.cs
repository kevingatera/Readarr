using System;
using System.Collections.Generic;
using System.Linq;

namespace NzbDrone.Core.History
{
    public static class GrabCohort
    {
        // One grab writes a row per book in a loop, so rows from the same grab
        // differ by milliseconds. A re-grab of the same download is minutes or hours later.
        private static readonly TimeSpan MaxGapWithinCohort = TimeSpan.FromMinutes(1);

        // Returns the Grabbed rows from the most recent grab of a download, newest first.
        public static List<EntityHistory> Latest(IEnumerable<EntityHistory> history)
        {
            var cohort = new List<EntityHistory>();

            foreach (var grabbed in history
                         .Where(h => h.EventType == EntityHistoryEventType.Grabbed)
                         .OrderByDescending(h => h.Date))
            {
                if (cohort.Count > 0 && cohort[^1].Date - grabbed.Date > MaxGapWithinCohort)
                {
                    break;
                }

                cohort.Add(grabbed);
            }

            return cohort;
        }
    }
}
