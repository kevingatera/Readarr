using System;
using System.Collections.Generic;
using System.Linq;

namespace NzbDrone.Core.History
{
    public static class GrabCohort
    {
        // Rows written before grab ids existed carry no shared identity. One grab writes its
        // rows in a loop, so they differ by milliseconds; a window of seconds keeps a re-grab
        // of the same download out of the cohort.
        private static readonly TimeSpan MaxGapWithinLegacyCohort = TimeSpan.FromSeconds(5);

        // Returns the Grabbed rows from the most recent grab of a download, newest first.
        public static List<EntityHistory> Latest(IEnumerable<EntityHistory> history)
        {
            var grabbed = history
                .Where(h => h.EventType == EntityHistoryEventType.Grabbed)
                .OrderByDescending(h => h.Date)
                .ToList();

            if (grabbed.Count == 0)
            {
                return new List<EntityHistory>();
            }

            var newestGrabId = GrabIdOf(grabbed[0]);

            if (newestGrabId != null)
            {
                return grabbed.Where(h => GrabIdOf(h) == newestGrabId).ToList();
            }

            var cohort = new List<EntityHistory>();

            foreach (var row in grabbed)
            {
                if (GrabIdOf(row) != null)
                {
                    break;
                }

                if (cohort.Count > 0 && cohort[^1].Date - row.Date > MaxGapWithinLegacyCohort)
                {
                    break;
                }

                cohort.Add(row);
            }

            return cohort;
        }

        private static string GrabIdOf(EntityHistory row)
        {
            return row.Data?.GetValueOrDefault(EntityHistory.GRAB_ID);
        }
    }
}
