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
        public void should_recover_author_from_reversed_filename_when_tags_are_swapped()
        {
            var authorMetadata = new AuthorMetadata { Name = "John Marrs" };

            var book = new Book
            {
                Title = "The Family Experiment",
                AuthorMetadata = new LazyLoaded<AuthorMetadata>(authorMetadata)
            };

            var edition = new Edition
            {
                Title = "The Family Experiment",
                Format = "Hardcover",
                Asin = "B0CQRGVTG8",
                Isbn13 = "9781335002891",
                Book = new LazyLoaded<Book>(book)
            };

            var localTracks = new List<LocalBook>
            {
                new LocalBook
                {
                    Path = "/downloads/complete/The Family Experiment - John Marrs/The Family Experiment - John Marrs.m4b",
                    FileTrackInfo = new ParsedTrackInfo
                    {
                        Authors = new List<string> { "The Family Experiment" },
                        BookTitle = "John Marrs"
                    }
                }
            };

            var dist = DistanceCalculator.BookDistance(localTracks, edition);

            dist.Reasons.Should().NotContain("author");
            dist.NormalizedDistance().Should().BeLessThan(0.20);
        }

        [Test]
        public void should_recover_author_from_by_separator_filename()
        {
            var authorMetadata = new AuthorMetadata { Name = "John Marrs" };

            var book = new Book
            {
                Title = "The Family Experiment",
                AuthorMetadata = new LazyLoaded<AuthorMetadata>(authorMetadata)
            };

            var edition = new Edition
            {
                Title = "The Family Experiment",
                Format = "Hardcover",
                Book = new LazyLoaded<Book>(book)
            };

            var localTracks = new List<LocalBook>
            {
                new LocalBook
                {
                    Path = "/downloads/complete/The Family Experiment by John Marrs/The Family Experiment by John Marrs.m4b",
                    FileTrackInfo = new ParsedTrackInfo
                    {
                        Authors = new List<string>(),
                        BookTitle = "The Family Experiment"
                    }
                }
            };

            var dist = DistanceCalculator.BookDistance(localTracks, edition);

            dist.Reasons.Should().NotContain("author");
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

        [Test]
        public void should_ignore_ebook_format_penalties_for_close_audiobook_match()
        {
            var authorMetadata = new AuthorMetadata { Name = "Jason Anspach" };

            var book = new Book
            {
                Title = "Hit & Fade",
                AuthorMetadata = new LazyLoaded<AuthorMetadata>(authorMetadata),
                SeriesLinks = new LazyLoaded<List<SeriesBookLink>>(new List<SeriesBookLink>
                {
                    new SeriesBookLink
                    {
                        Position = "2",
                        Series = new LazyLoaded<Series>(new Series { Title = "Forgotten Ruin" })
                    }
                })
            };

            var edition = new Edition
            {
                Title = "Hit & Fade",
                Format = "Kindle Edition",
                Book = new LazyLoaded<Book>(book)
            };

            var localTracks = new List<LocalBook>
            {
                new LocalBook
                {
                    Path = "/downloads/complete/Forgotten Ruin Book 2 - Hit & Fade.m4b",
                    FileTrackInfo = new ParsedTrackInfo
                    {
                        Title = "Hit & Fade",
                        BookTitle = "Forgotten Ruin Book 2 - Hit & Fade",
                        Authors = new List<string> { "Nick Cole, Jason Anspach" }
                    }
                }
            };

            var dist = DistanceCalculator.BookDistance(localTracks, edition);

            dist.Reasons.Should().NotContain("wrong format");
            dist.Reasons.Should().NotContain("audio format");
            dist.NormalizedDistance().Should().BeLessThan(0.20);
        }

        [Test]
        public void should_match_camel_cased_unabridged_part_files_for_metadata_poor_audiobook()
        {
            var authorMetadata = new AuthorMetadata { Name = "Lisa Kleypas" };

            var book = new Book
            {
                Title = "Prince of Dreams",
                AuthorMetadata = new LazyLoaded<AuthorMetadata>(authorMetadata),
                SeriesLinks = new LazyLoaded<List<SeriesBookLink>>(new List<SeriesBookLink>
                {
                    new SeriesBookLink
                    {
                        Position = "2",
                        SeriesPosition = 2,
                        Series = new LazyLoaded<Series>(new Series { Title = "The Stokehursts" })
                    }
                })
            };

            var edition = new Edition
            {
                Title = "Prince of Dreams",
                Format = "Kindle Edition",
                Book = new LazyLoaded<Book>(book)
            };

            var localTracks = new List<LocalBook>
            {
                new LocalBook
                {
                    Path = "/downloads/complete/Lisa Kleypas - Stokehurst 2 - Prince of Dreams v2/PrinceofDreamsUnabridgedPart1.mp3",
                    FileTrackInfo = new ParsedTrackInfo
                    {
                        Title = "PrinceofDreamsUnabridgedPart1",
                        BookTitle = "PrinceofDreamsUnabridgedPart1",
                        Authors = new List<string>()
                    }
                },
                new LocalBook
                {
                    Path = "/downloads/complete/Lisa Kleypas - Stokehurst 2 - Prince of Dreams v2/PrinceofDreamsUnabridgedPart2.mp3",
                    FileTrackInfo = new ParsedTrackInfo
                    {
                        Title = "PrinceofDreamsUnabridgedPart2",
                        BookTitle = "PrinceofDreamsUnabridgedPart2",
                        Authors = new List<string>()
                    }
                }
            };

            var dist = DistanceCalculator.BookDistance(localTracks, edition);

            dist.Reasons.Should().NotContain("author");
            dist.Reasons.Should().NotContain("book");
            dist.Reasons.Should().NotContain("audio format");
            dist.Reasons.Should().NotContain("wrong format");
            dist.NormalizedDistance().Should().BeLessThan(0.20);
        }

        [Test]
        public void should_ignore_track_part_suffixes_for_multi_file_audiobook_series_matching()
        {
            var authorMetadata = new AuthorMetadata { Name = "Lisa Kleypas" };

            var book = new Book
            {
                Title = "Dream Lake",
                AuthorMetadata = new LazyLoaded<AuthorMetadata>(authorMetadata),
                SeriesLinks = new LazyLoaded<List<SeriesBookLink>>(new List<SeriesBookLink>
                {
                    new SeriesBookLink
                    {
                        Position = "3",
                        SeriesPosition = 3,
                        Series = new LazyLoaded<Series>(new Series { Title = "Friday Harbor" })
                    }
                })
            };

            var edition = new Edition
            {
                Title = "Dream Lake",
                Format = "Kindle Edition",
                Book = new LazyLoaded<Book>(book)
            };

            var localTracks = new List<LocalBook>
            {
                new LocalBook
                {
                    Path = "/downloads/complete/Lisa Kleypas - Dream Lake/Dream Lake Pt 1.mp3",
                    FileTrackInfo = new ParsedTrackInfo
                    {
                        Title = "Dream Lake Pt 1",
                        BookTitle = "Dream Lake Pt 1",
                        Authors = new List<string>()
                    }
                },
                new LocalBook
                {
                    Path = "/downloads/complete/Lisa Kleypas - Dream Lake/Dream Lake Pt 2.mp3",
                    FileTrackInfo = new ParsedTrackInfo
                    {
                        Title = "Dream Lake Pt 2",
                        BookTitle = "Dream Lake Pt 2",
                        Authors = new List<string>()
                    }
                }
            };

            var dist = DistanceCalculator.BookDistance(localTracks, edition);

            dist.Reasons.Should().NotContain("series part");
            dist.Reasons.Should().NotContain("audio format");
            dist.Reasons.Should().NotContain("wrong format");
            dist.NormalizedDistance().Should().BeLessThan(0.20);
        }

        [Test]
        public void should_ignore_ebook_format_penalties_when_exact_audio_title_lacks_author_tags()
        {
            var authorMetadata = new AuthorMetadata { Name = "Lisa Kleypas" };

            var book = new Book
            {
                Title = "Marrying Winterborne",
                AuthorMetadata = new LazyLoaded<AuthorMetadata>(authorMetadata)
            };

            var edition = new Edition
            {
                Title = "Marrying Winterborne",
                Format = "Kindle Edition",
                Book = new LazyLoaded<Book>(book)
            };

            var localTracks = new List<LocalBook>
            {
                new LocalBook
                {
                    Path = "/downloads/complete/Marrying Winterborne/MarryingWinterborne.mp3",
                    FileTrackInfo = new ParsedTrackInfo
                    {
                        Title = "MarryingWinterborne",
                        BookTitle = "MarryingWinterborne",
                        Authors = new List<string>()
                    }
                }
            };

            var dist = DistanceCalculator.BookDistance(localTracks, edition);

            dist.Reasons.Should().NotContain("author");
            dist.Reasons.Should().NotContain("book");
            dist.Reasons.Should().NotContain("audio format");
            dist.Reasons.Should().NotContain("wrong format");
            dist.NormalizedDistance().Should().BeLessThan(0.20);
        }

        [Test]
        public void should_not_soften_author_penalty_when_audio_tags_name_conflicting_author()
        {
            var authorMetadata = new AuthorMetadata { Name = "Lisa Kleypas" };

            var book = new Book
            {
                Title = "Dream Lake",
                AuthorMetadata = new LazyLoaded<AuthorMetadata>(authorMetadata)
            };

            var edition = new Edition
            {
                Title = "Dream Lake",
                Format = "Kindle Edition",
                Book = new LazyLoaded<Book>(book)
            };

            var localTracks = new List<LocalBook>
            {
                new LocalBook
                {
                    Path = "/downloads/complete/Dream Lake/DreamLake.mp3",
                    FileTrackInfo = new ParsedTrackInfo
                    {
                        Title = "DreamLake",
                        BookTitle = "DreamLake",
                        Authors = new List<string> { "Stephen King" }
                    }
                }
            };

            var dist = DistanceCalculator.BookDistance(localTracks, edition);

            dist.Reasons.Should().Contain("author");
            dist.NormalizedDistance().Should().BeGreaterThan(0.20);
        }

        [Test]
        public void should_keep_ebook_format_penalties_for_wrong_audiobook_candidate()
        {
            var authorMetadata = new AuthorMetadata { Name = "Jason Anspach" };

            var book = new Book
            {
                Title = "Gods & Legionnaires",
                AuthorMetadata = new LazyLoaded<AuthorMetadata>(authorMetadata),
                SeriesLinks = new LazyLoaded<List<SeriesBookLink>>(new List<SeriesBookLink>
                {
                    new SeriesBookLink
                    {
                        Position = "2",
                        Series = new LazyLoaded<Series>(new Series { Title = "Galaxy's Edge: Savage Wars" })
                    }
                })
            };

            var edition = new Edition
            {
                Title = "Gods & Legionnaires",
                Format = "Kindle Edition",
                Book = new LazyLoaded<Book>(book)
            };

            var localTracks = new List<LocalBook>
            {
                new LocalBook
                {
                    Path = "/downloads/complete/Forgotten Ruin Book 2 - Hit & Fade.m4b",
                    FileTrackInfo = new ParsedTrackInfo
                    {
                        Title = "Hit & Fade",
                        BookTitle = "Forgotten Ruin Book 2 - Hit & Fade",
                        Authors = new List<string> { "Nick Cole, Jason Anspach" }
                    }
                }
            };

            var dist = DistanceCalculator.BookDistance(localTracks, edition);

            dist.Reasons.Should().Contain("wrong format");
            dist.NormalizedDistance().Should().BeGreaterThan(0.20);
        }

        [Test]
        public void should_soften_series_part_penalty_for_single_file_audio_with_strong_title_and_missing_ids()
        {
            var authorMetadata = new AuthorMetadata { Name = "Jason Anspach" };

            var book = new Book
            {
                Title = "The Betrayed",
                AuthorMetadata = new LazyLoaded<AuthorMetadata>(authorMetadata),
                SeriesLinks = new LazyLoaded<List<SeriesBookLink>>(new List<SeriesBookLink>
                {
                    new SeriesBookLink
                    {
                        Position = "24",
                        Series = new LazyLoaded<Series>(new Series { Title = "Galaxy's Edge" })
                    }
                })
            };

            var edition = new Edition
            {
                Title = "The Betrayed",
                Asin = "B0DJBFP298",
                Format = "Audible Audio",
                Book = new LazyLoaded<Book>(book)
            };

            var localTracks = new List<LocalBook>
            {
                new LocalBook
                {
                    Path = "/downloads/complete/Nick Cole - Galaxy's Edge, Book 19 - The Betrayed.m4b",
                    FileTrackInfo = new ParsedTrackInfo
                    {
                        Title = "The Betrayed",
                        BookTitle = "Galaxy's Edge, Book 19 - The Betrayed",
                        Authors = new List<string> { "Jason Anspach" }
                    }
                }
            };

            var dist = DistanceCalculator.BookDistance(localTracks, edition);

            dist.Penalties.Should().ContainKey("series_part");
            dist.Penalties["series_part"].Should().ContainSingle().Which.Should().Be(0.5);
            dist.NormalizedDistance().Should().BeLessThan(0.20);
        }

        [Test]
        public void should_keep_full_series_part_penalty_when_local_identifier_exists()
        {
            var authorMetadata = new AuthorMetadata { Name = "Jason Anspach" };

            var book = new Book
            {
                Title = "The Betrayed",
                AuthorMetadata = new LazyLoaded<AuthorMetadata>(authorMetadata),
                SeriesLinks = new LazyLoaded<List<SeriesBookLink>>(new List<SeriesBookLink>
                {
                    new SeriesBookLink
                    {
                        Position = "24",
                        Series = new LazyLoaded<Series>(new Series { Title = "Galaxy's Edge" })
                    }
                })
            };

            var edition = new Edition
            {
                Title = "The Betrayed",
                Asin = "B0DJBFP298",
                Format = "Audible Audio",
                Book = new LazyLoaded<Book>(book)
            };

            var localTracks = new List<LocalBook>
            {
                new LocalBook
                {
                    Path = "/downloads/complete/Nick Cole - Galaxy's Edge, Book 19 - The Betrayed.m4b",
                    FileTrackInfo = new ParsedTrackInfo
                    {
                        Title = "The Betrayed",
                        BookTitle = "Galaxy's Edge, Book 19 - The Betrayed",
                        Authors = new List<string> { "Jason Anspach" },
                        Asin = "B000000000"
                    }
                }
            };

            var dist = DistanceCalculator.BookDistance(localTracks, edition);

            dist.Penalties.Should().ContainKey("series_part");
            dist.Penalties["series_part"].Should().ContainSingle().Which.Should().Be(1.0);
        }
    }
}
