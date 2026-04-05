using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.MediaFiles.BookImport.Identification;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.BookImport.Specifications
{
    public class CloseBookMatchSpecification : IImportDecisionEngineSpecification<LocalEdition>
    {
        private const double _bookThreshold = 0.20;
        private const double _seriesPartRelaxedThreshold = 0.22;
        private readonly Logger _logger;

        public CloseBookMatchSpecification(Logger logger)
        {
            _logger = logger;
        }

        public Decision IsSatisfiedBy(LocalEdition item, DownloadClientItem downloadClientItem)
        {
            double dist;
            string reasons;

            // strict when a new download
            if (item.NewDownload)
            {
                dist = item.Distance.NormalizedDistance();
                reasons = item.Distance.Reasons;
                if (dist > _bookThreshold)
                {
                    if (ShouldAcceptSeriesPartEdgeCase(item, dist))
                    {
                        _logger.Debug($"Accepting edge-case series mismatch for close title match: {dist} vs {_bookThreshold} {reasons}");
                        return Decision.Accept();
                    }

                    _logger.Debug($"Book match is not close enough: {dist} vs {_bookThreshold} {reasons}. Skipping {item}");
                    return Decision.Reject($"Book match is not close enough: {1 - dist:P1} vs {1 - _bookThreshold:P0} {reasons}");
                }
            }

            // otherwise importing existing files in library
            else
            {
                // get book distance ignoring whether tracks are missing
                dist = item.Distance.NormalizedDistanceExcluding(new List<string> { "missing_tracks", "unmatched_tracks" });
                reasons = item.Distance.Reasons;
                if (dist > _bookThreshold)
                {
                    _logger.Debug($"Book match is not close enough: {dist} vs {_bookThreshold} {reasons}. Skipping {item}");
                    return Decision.Reject($"Book match is not close enough: {1 - dist:P1} vs {1 - _bookThreshold:P0} {reasons}");
                }
            }

            _logger.Debug($"Accepting release {item}: dist {dist} vs {_bookThreshold} {reasons}");
            return Decision.Accept();
        }

        private bool ShouldAcceptSeriesPartEdgeCase(LocalEdition item, double dist)
        {
            if (item?.Distance == null || dist > _seriesPartRelaxedThreshold)
            {
                return false;
            }

            var penalties = item.Distance.Penalties;

            if (!HasPositivePenalty(penalties, "series_part"))
            {
                return false;
            }

            // Do not relax when local identifiers actively disagree with remote metadata.
            if (HasPositivePenalty(penalties, "asin") || HasPositivePenalty(penalties, "isbn"))
            {
                return false;
            }

            // Require at least one missing-id signal so this only applies to metadata-poor downloads.
            if (!HasPositivePenalty(penalties, "asin_missing") &&
                !HasPositivePenalty(penalties, "isbn_missing"))
            {
                return false;
            }

            // If all non-series evidence is already within threshold, allow the close miss.
            var distWithoutSeries = item.Distance.NormalizedDistanceExcluding(new List<string> { "series_part" });
            return distWithoutSeries <= _bookThreshold;
        }

        private static bool HasPositivePenalty(Dictionary<string, List<double>> penalties, string key)
        {
            return penalties.TryGetValue(key, out var values) && values.Any(value => value > 0.0);
        }
    }
}
