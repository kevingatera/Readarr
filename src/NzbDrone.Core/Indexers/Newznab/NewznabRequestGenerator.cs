using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.Indexers.Newznab
{
    public class NewznabRequestGenerator : IIndexerRequestGenerator
    {
        private static readonly Regex TrailingBracketedTitlePart = new Regex(@"\s*(?:\[[^\]]+\]|\([^)]*\))\s*$", RegexOptions.Compiled);

        protected readonly INewznabCapabilitiesProvider _capabilitiesProvider;
        public int MaxPages { get; set; }
        public int PageSize { get; set; }
        public NewznabSettings Settings { get; set; }

        public NewznabRequestGenerator(INewznabCapabilitiesProvider capabilitiesProvider)
        {
            _capabilitiesProvider = capabilitiesProvider;

            MaxPages = 30;
            PageSize = 100;
        }

        private bool SupportsSearch
        {
            get
            {
                var capabilities = _capabilitiesProvider.GetCapabilities(Settings);

                return capabilities.SupportedSearchParameters != null &&
                       capabilities.SupportedSearchParameters.Contains("q");
            }
        }

        protected virtual bool SupportsBookSearch => false;

        public virtual IndexerPageableRequestChain GetRecentRequests()
        {
            var pageableRequests = new IndexerPageableRequestChain();

            var capabilities = _capabilitiesProvider.GetCapabilities(Settings);

            if (capabilities.SupportedBookSearchParameters != null)
            {
                pageableRequests.Add(GetPagedRequests(MaxPages, Settings.Categories, "book", ""));
            }
            else if (capabilities.SupportedSearchParameters != null)
            {
                pageableRequests.Add(GetPagedRequests(MaxPages, Settings.Categories, "search", ""));
            }

            return pageableRequests;
        }

        public virtual IndexerPageableRequestChain GetSearchRequests(BookSearchCriteria searchCriteria)
        {
            var pageableRequests = new IndexerPageableRequestChain();
            var fallbackBookQuery = GetFallbackBookQuery(searchCriteria);

            if (SupportsBookSearch)
            {
                AddBookPageableRequests(pageableRequests,
                    searchCriteria,
                    $"&author={NewsnabifyTitle(searchCriteria.AuthorQuery)}&title={NewsnabifyTitle(searchCriteria.BookQuery)}");

                AddBookPageableRequests(pageableRequests,
                    searchCriteria,
                    $"&title={NewsnabifyTitle(searchCriteria.BookQuery)}");

                if (fallbackBookQuery.IsNotNullOrWhiteSpace())
                {
                    AddBookPageableRequests(pageableRequests,
                        searchCriteria,
                        $"&author={NewsnabifyTitle(searchCriteria.AuthorQuery)}&title={NewsnabifyTitle(fallbackBookQuery)}");

                    AddBookPageableRequests(pageableRequests,
                        searchCriteria,
                        $"&title={NewsnabifyTitle(fallbackBookQuery)}");
                }
            }

            if (SupportsSearch)
            {
                pageableRequests.AddTier();

                pageableRequests.Add(GetPagedRequests(MaxPages,
                    Settings.Categories,
                    "search",
                    $"&q={NewsnabifyTitle(searchCriteria.BookQuery)}+{NewsnabifyTitle(searchCriteria.AuthorQuery)}"));

                pageableRequests.Add(GetPagedRequests(MaxPages,
                    Settings.Categories,
                    "search",
                    $"&q={NewsnabifyTitle(searchCriteria.AuthorQuery)}+{NewsnabifyTitle(searchCriteria.BookQuery)}"));

                pageableRequests.AddTier();

                pageableRequests.Add(GetPagedRequests(MaxPages,
                    Settings.Categories,
                    "search",
                    $"&q={NewsnabifyTitle(searchCriteria.BookQuery)}"));

                if (fallbackBookQuery.IsNotNullOrWhiteSpace())
                {
                    pageableRequests.AddTier();

                    pageableRequests.Add(GetPagedRequests(MaxPages,
                        Settings.Categories,
                        "search",
                        $"&q={NewsnabifyTitle(fallbackBookQuery)}+{NewsnabifyTitle(searchCriteria.AuthorQuery)}"));

                    pageableRequests.Add(GetPagedRequests(MaxPages,
                        Settings.Categories,
                        "search",
                        $"&q={NewsnabifyTitle(searchCriteria.AuthorQuery)}+{NewsnabifyTitle(fallbackBookQuery)}"));

                    pageableRequests.AddTier();

                    pageableRequests.Add(GetPagedRequests(MaxPages,
                        Settings.Categories,
                        "search",
                        $"&q={NewsnabifyTitle(fallbackBookQuery)}"));
                }
            }

            return pageableRequests;
        }

        public virtual IndexerPageableRequestChain GetSearchRequests(AuthorSearchCriteria searchCriteria)
        {
            var pageableRequests = new IndexerPageableRequestChain();

            if (SupportsBookSearch)
            {
                AddBookPageableRequests(pageableRequests,
                    searchCriteria,
                    $"&author={NewsnabifyTitle(searchCriteria.AuthorQuery)}");
            }

            if (SupportsSearch)
            {
                pageableRequests.AddTier();

                pageableRequests.Add(GetPagedRequests(MaxPages,
                    Settings.Categories,
                    "search",
                    $"&q={NewsnabifyTitle(searchCriteria.AuthorQuery)}"));
            }

            return pageableRequests;
        }

        private void AddBookPageableRequests(IndexerPageableRequestChain chain, SearchCriteriaBase searchCriteria, string parameters)
        {
            chain.AddTier();

            chain.Add(GetPagedRequests(MaxPages, Settings.Categories, "book", $"{parameters}"));
        }

        private IEnumerable<IndexerRequest> GetPagedRequests(int maxPages, IEnumerable<int> categories, string searchType, string parameters)
        {
            if (categories.Empty())
            {
                yield break;
            }

            var categoriesQuery = string.Join(",", categories.Distinct());

            var baseUrl =
                $"{Settings.BaseUrl.TrimEnd('/')}{Settings.ApiPath.TrimEnd('/')}?t={searchType}&cat={categoriesQuery}&extended=1{Settings.AdditionalParameters}";

            if (Settings.ApiKey.IsNotNullOrWhiteSpace())
            {
                baseUrl += "&apikey=" + Settings.ApiKey;
            }

            if (PageSize == 0)
            {
                yield return new IndexerRequest($"{baseUrl}{parameters}", HttpAccept.Rss);
            }
            else
            {
                for (var page = 0; page < maxPages; page++)
                {
                    yield return new IndexerRequest($"{baseUrl}&offset={page * PageSize}&limit={PageSize}{parameters}",
                        HttpAccept.Rss);
                }
            }
        }

        private static string NewsnabifyTitle(string title)
        {
            title = title.Replace("+", " ");
            return Uri.EscapeDataString(title);
        }

        private static string GetFallbackBookQuery(BookSearchCriteria searchCriteria)
        {
            if (searchCriteria?.BookTitle.IsNullOrWhiteSpace() ?? true)
            {
                return null;
            }

            var stripped = searchCriteria.BookTitle;

            while (true)
            {
                var updated = TrailingBracketedTitlePart.Replace(stripped, string.Empty).Trim();

                if (updated == stripped)
                {
                    break;
                }

                stripped = updated;
            }

            if (stripped.IsNullOrWhiteSpace() || stripped.Equals(searchCriteria.BookTitle, StringComparison.Ordinal))
            {
                return null;
            }

            var authorName = searchCriteria.Author?.Name ?? string.Empty;
            var splitTitle = stripped.SplitBookTitle(authorName).Item1;
            var fallbackQuery = SearchCriteriaBase.GetQueryTitle(splitTitle);

            return fallbackQuery.Equals(searchCriteria.BookQuery, StringComparison.OrdinalIgnoreCase) ? null : fallbackQuery;
        }
    }
}
