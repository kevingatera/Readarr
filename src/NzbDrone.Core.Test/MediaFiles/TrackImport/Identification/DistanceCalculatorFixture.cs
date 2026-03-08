using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MediaFiles.BookImport.Identification;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.BookImport.Identification
{
    [TestFixture]
    public class DistanceCalculatorFixture : TestBase
    {
        [Test]
        public void should_reverse_single_reversed_author()
        {
            var input = new List<string> { "Last, First" };
            var authors = DistanceCalculator.GetAuthorVariants(input);

            authors.Should().Contain("First Last");
        }

        [Test]
        public void should_reverse_two_reversed_author()
        {
            var input = new List<string>
            {
                "Last, First",
                "Last2, First2"
            };

            var authors = DistanceCalculator.GetAuthorVariants(input);

            authors.Should().HaveCount(4);
            authors.Should().Contain("First Last");
            authors.Should().Contain("First2 Last2");
            authors.Should().Contain("Last, First");
            authors.Should().Contain("Last2, First2");
        }

        [Test]
        public void should_not_reverse_single_author()
        {
            var input = new List<string> { "First Last" };
            var authors = DistanceCalculator.GetAuthorVariants(input);

            authors.Should().HaveCount(1);
            authors.Should().Contain("First Last");
        }

        [TestCase("First1 Last1, First2 Last2", "First1 Last1", "First2 Last2")]
        [TestCase("First1 Last1; First2 Last2", "First1 Last1", "First2 Last2")]
        [TestCase("First1 Last1 & First2 Last2", "First1 Last1", "First2 Last2")]
        [TestCase("First1 Last1 / First2 Last2", "First1 Last1", "First2 Last2")]
        [TestCase("First1 Last1 and First2 Last2", "First1 Last1", "First2 Last2")]
        public void should_split_concatenated_author(string inputString, string first, string second)
        {
            var input = new List<string> { inputString };
            var authors = DistanceCalculator.GetAuthorVariants(input);

            authors.Should().Contain(inputString);
            authors.Should().Contain(first);
            authors.Should().Contain(second);
            authors.Should().HaveCount(3);
        }

        [Test]
        public void should_split_concatenated_with_trailing_and()
        {
            var inputString = "First Last, First2 Last2 & First3 Last3";
            var input = new List<string> { inputString };
            var authors = DistanceCalculator.GetAuthorVariants(input);

            authors.Should().Contain(inputString);
            authors.Should().Contain("First Last");
            authors.Should().Contain("First2 Last2");
            authors.Should().Contain("First3 Last3");
            authors.Should().HaveCount(4);
        }

        [Test]
        public void should_not_split_if_multiple_input()
        {
            var input = new List<string>
            {
                "First Last",
                "Second Third, Fourth Fifth"
            };

            var authors = DistanceCalculator.GetAuthorVariants(input);

            authors.Should().HaveCount(4);
            authors.Should().Contain("First Last");
            authors.Should().Contain("Second Third, Fourth Fifth");
            authors.Should().Contain("Second Third");
            authors.Should().Contain("Fourth Fifth");
        }

        [Test]
        public void should_use_track_title_for_single_file_audiobook_matching()
        {
            var authorMetadata = new AuthorMetadata { Name = "Jason Anspach" };

            var book = new Book
            {
                Title = "Gods & Legionnaires",
                AuthorMetadata = new LazyLoaded<AuthorMetadata>(authorMetadata)
            };

            var edition = new Edition
            {
                Title = "Gods & Legionnaires",
                Book = new LazyLoaded<Book>(book)
            };

            var localTracks = new List<LocalBook>
            {
                new LocalBook
                {
                    Path = "/downloads/complete/02 Gods & Legionnaires/Savage Wars (Galaxy's Edge) Book 2 - Gods & Legionnaires.m4b",
                    FileTrackInfo = new ParsedTrackInfo
                    {
                        Title = "Gods & Legionnaires",
                        BookTitle = "02 Gods & Legionnaires",
                        Authors = new List<string>
                        {
                            "Galaxy's Edge (Savage Wars)",
                            "Jason Anspach, Nick Cole"
                        }
                    }
                }
            };

            var dist = DistanceCalculator.BookDistance(localTracks, edition);

            dist.Reasons.Should().NotContain("book");
        }

        [Test]
        public void should_match_series_part_aliases_against_series_position()
        {
            var authorMetadata = new AuthorMetadata { Name = "Jason Anspach" };

            var book = new Book
            {
                Title = "Message for the Dead",
                AuthorMetadata = new LazyLoaded<AuthorMetadata>(authorMetadata),
                SeriesLinks = new LazyLoaded<List<SeriesBookLink>>(new List<SeriesBookLink>
                {
                    new SeriesBookLink
                    {
                        Position = "4",
                        Series = new LazyLoaded<Series>(new Series { Title = "Galaxy's Edge" })
                    }
                })
            };

            var edition = new Edition
            {
                Title = "Message for the Dead",
                Book = new LazyLoaded<Book>(book)
            };

            var localTracks = new List<LocalBook>
            {
                new LocalBook
                {
                    Path = "/downloads/complete/Galaxy's Edge, Part IV.m4b",
                    FileTrackInfo = new ParsedTrackInfo
                    {
                        Authors = new List<string> { "Jason Anspach" },
                        BookTitle = "Galaxy's Edge, Part IV"
                    }
                }
            };

            var dist = DistanceCalculator.BookDistance(localTracks, edition);

            dist.Reasons.Should().NotContain("book");
        }

        [Test]
        public void should_penalize_wrong_series_part_candidate_for_single_file_audiobook()
        {
            var authorMetadata = new AuthorMetadata { Name = "Jason Anspach" };

            var correctBook = new Book
            {
                Title = "Gods & Legionnaires",
                AuthorMetadata = new LazyLoaded<AuthorMetadata>(authorMetadata),
                SeriesLinks = new LazyLoaded<List<SeriesBookLink>>(new List<SeriesBookLink>
                {
                    new SeriesBookLink
                    {
                        Position = "2",
                        SeriesPosition = 2,
                        Series = new LazyLoaded<Series>(new Series { Title = "Galaxy's Edge: Savage Wars" })
                    }
                })
            };

            var wrongBook = new Book
            {
                Title = "Forget Nothing",
                AuthorMetadata = new LazyLoaded<AuthorMetadata>(authorMetadata),
                SeriesLinks = new LazyLoaded<List<SeriesBookLink>>(new List<SeriesBookLink>
                {
                    new SeriesBookLink
                    {
                        Position = "0.6",
                        SeriesPosition = 0.6,
                        Series = new LazyLoaded<Series>(new Series { Title = "Galaxy's Edge" })
                    }
                })
            };

            var localTracks = new List<LocalBook>
            {
                new LocalBook
                {
                    Path = "/downloads/complete/02 Gods & Legionnaires/Savage Wars (Galaxy's Edge) Book 2 - Gods & Legionnaires.m4b",
                    FileTrackInfo = new ParsedTrackInfo
                    {
                        Title = "Gods & Legionnaires",
                        BookTitle = "02 Gods & Legionnaires",
                        Authors = new List<string>
                        {
                            "Galaxy's Edge (Savage Wars)",
                            "Jason Anspach, Nick Cole"
                        }
                    }
                }
            };

            var correctEdition = new Edition
            {
                Title = "Gods & Legionnaires",
                Book = new LazyLoaded<Book>(correctBook)
            };

            var wrongEdition = new Edition
            {
                Title = "Forget Nothing",
                Book = new LazyLoaded<Book>(wrongBook)
            };

            var correctDistance = DistanceCalculator.BookDistance(localTracks, correctEdition);
            var wrongDistance = DistanceCalculator.BookDistance(localTracks, wrongEdition);

            correctDistance.Reasons.Should().NotContain("series part");
            wrongDistance.Reasons.Should().Contain("series part");
            wrongDistance.NormalizedDistance().Should().BeGreaterThan(correctDistance.NormalizedDistance());
        }
    }
}
