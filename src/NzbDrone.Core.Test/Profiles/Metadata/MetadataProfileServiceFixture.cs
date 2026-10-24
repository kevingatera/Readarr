using System;
using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Profiles.Metadata;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Profiles.Metadata
{
    [TestFixture]

    public class MetadataProfileServiceFixture : CoreTest<MetadataProfileService>
    {
        [Test]
        public void init_should_add_default_profiles()
        {
            Subject.Handle(new ApplicationStartedEvent());

            Mocker.GetMock<IMetadataProfileRepository>()
                  .Verify(v => v.Insert(It.IsAny<MetadataProfile>()), Times.Exactly(2));
        }

        [Test]

        //This confirms that new profiles are added only if no other profiles exists.
        //We don't want to keep adding them back if a user deleted them on purpose.
        public void Init_should_skip_if_any_profiles_already_exist()
        {
            Mocker.GetMock<IMetadataProfileRepository>()
                  .Setup(s => s.All())
                  .Returns(Builder<MetadataProfile>.CreateListOfSize(2).Build().ToList());

            Subject.Handle(new ApplicationStartedEvent());

            Mocker.GetMock<IMetadataProfileRepository>()
                  .Verify(v => v.Insert(It.Is<MetadataProfile>(x => x.Name == "Standard")), Times.Never());
        }

        [Test]
        public void init_should_add_none_profile_if_it_doesnt_exist()
        {
            Mocker.GetMock<IMetadataProfileRepository>()
                  .Setup(s => s.All())
                  .Returns(Builder<MetadataProfile>.CreateListOfSize(2).Build().ToList());

            Subject.Handle(new ApplicationStartedEvent());

            Mocker.GetMock<IMetadataProfileRepository>()
                  .Verify(v => v.Insert(It.Is<MetadataProfile>(x => x.Name == "None")), Times.Once());
        }

        [Test]
        public void init_should_move_existing_none_profile()
        {
            var profiles = Builder<MetadataProfile>.CreateListOfSize(2)
                .TheFirst(1)
                .With(x => x.Name = MetadataProfileService.NONE_PROFILE_NAME)
                .BuildList();

            Mocker.GetMock<IMetadataProfileRepository>()
                .Setup(s => s.All())
                .Returns(profiles);

            Subject.Handle(new ApplicationStartedEvent());

            Mocker.GetMock<IMetadataProfileRepository>()
                .Verify(v => v.Update(It.Is<MetadataProfile>(x => x.Id == profiles.First().Id && x.Name == "None.1")), Times.Once());

            Mocker.GetMock<IMetadataProfileRepository>()
                  .Verify(v => v.Insert(It.Is<MetadataProfile>(x => x.Name == "None")), Times.Once());
        }

        [Test]
        public void should_not_be_able_to_edit_none_profile()
        {
            var profile = Builder<MetadataProfile>.CreateNew()
                .With(p => p.Name = MetadataProfileService.NONE_PROFILE_NAME)
                .Build();

            Assert.Throws<InvalidOperationException>(() => Subject.Update(profile));
        }

        [Test]
        public void should_not_be_able_to_delete_none_profile()
        {
            var profile = Builder<MetadataProfile>.CreateNew()
                .With(p => p.Name = MetadataProfileService.NONE_PROFILE_NAME)
                .Build();

            Mocker.GetMock<IMetadataProfileRepository>().Setup(c => c.Get(profile.Id)).Returns(profile);

            Assert.Throws<MetadataProfileInUseException>(() => Subject.Delete(profile.Id));
        }

        [Test]
        public void should_not_be_able_to_delete_profile_if_assigned_to_author()
        {
            var profile = Builder<MetadataProfile>.CreateNew()
                .With(p => p.Id = 2)
                .Build();

            var authorList = Builder<Author>.CreateListOfSize(3)
                                            .Random(1)
                                            .With(c => c.MetadataProfileId = profile.Id)
                                            .Build().ToList();

            var importLists = Builder<ImportListDefinition>.CreateListOfSize(2)
                .All()
                .With(c => c.MetadataProfileId = 1)
                .Build().ToList();

            var rootFolders = Builder<RootFolder>.CreateListOfSize(2)
                .All()
                .With(f => f.DefaultMetadataProfileId = 1)
                .BuildList();

            Mocker.GetMock<IAuthorService>().Setup(c => c.GetAllAuthors()).Returns(authorList);
            Mocker.GetMock<IImportListFactory>().Setup(c => c.All()).Returns(importLists);
            Mocker.GetMock<IRootFolderService>().Setup(c => c.All()).Returns(rootFolders);
            Mocker.GetMock<IMetadataProfileRepository>().Setup(c => c.Get(profile.Id)).Returns(profile);

            Assert.Throws<MetadataProfileInUseException>(() => Subject.Delete(profile.Id));

            Mocker.GetMock<IMetadataProfileRepository>().Verify(c => c.Delete(It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void should_not_be_able_to_delete_profile_if_assigned_to_import_list()
        {
            var profile = Builder<MetadataProfile>.CreateNew()
                .With(p => p.Id = 2)
                .Build();

            var authorList = Builder<Author>.CreateListOfSize(3)
                .All()
                .With(c => c.MetadataProfileId = 1)
                .Build().ToList();

            var importLists = Builder<ImportListDefinition>.CreateListOfSize(2)
                .Random(1)
                .With(c => c.MetadataProfileId = profile.Id)
                .Build().ToList();

            var rootFolders = Builder<RootFolder>.CreateListOfSize(2)
                .All()
                .With(f => f.DefaultMetadataProfileId = 1)
                .BuildList();

            Mocker.GetMock<IAuthorService>().Setup(c => c.GetAllAuthors()).Returns(authorList);
            Mocker.GetMock<IImportListFactory>().Setup(c => c.All()).Returns(importLists);
            Mocker.GetMock<IRootFolderService>().Setup(c => c.All()).Returns(rootFolders);
            Mocker.GetMock<IMetadataProfileRepository>().Setup(c => c.Get(profile.Id)).Returns(profile);

            Assert.Throws<MetadataProfileInUseException>(() => Subject.Delete(profile.Id));

            Mocker.GetMock<IMetadataProfileRepository>().Verify(c => c.Delete(It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void should_not_be_able_to_delete_profile_if_assigned_to_root_folder()
        {
            var profile = Builder<MetadataProfile>.CreateNew()
                .With(p => p.Id = 2)
                .Build();

            var authorList = Builder<Author>.CreateListOfSize(3)
                .All()
                .With(c => c.MetadataProfileId = 1)
                .Build().ToList();

            var importLists = Builder<ImportListDefinition>.CreateListOfSize(2)
                .All()
                .With(c => c.MetadataProfileId = 1)
                .Build().ToList();

            var rootFolders = Builder<RootFolder>.CreateListOfSize(2)
                .Random(1)
                .With(f => f.DefaultMetadataProfileId = profile.Id)
                .BuildList();

            Mocker.GetMock<IAuthorService>().Setup(c => c.GetAllAuthors()).Returns(authorList);
            Mocker.GetMock<IImportListFactory>().Setup(c => c.All()).Returns(importLists);
            Mocker.GetMock<IRootFolderService>().Setup(c => c.All()).Returns(rootFolders);
            Mocker.GetMock<IMetadataProfileRepository>().Setup(c => c.Get(profile.Id)).Returns(profile);

            Assert.Throws<MetadataProfileInUseException>(() => Subject.Delete(profile.Id));

            Mocker.GetMock<IMetadataProfileRepository>().Verify(c => c.Delete(It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void should_delete_profile_if_not_assigned_to_author_import_list_or_root_folder()
        {
            var profile = Builder<MetadataProfile>.CreateNew()
                .With(p => p.Id = 1)
                .Build();

            var authorList = Builder<Author>.CreateListOfSize(3)
                                            .All()
                                            .With(c => c.MetadataProfileId = 2)
                                            .Build().ToList();

            var importLists = Builder<ImportListDefinition>.CreateListOfSize(2)
                .All()
                .With(c => c.MetadataProfileId = 2)
                .Build().ToList();

            var rootFolders = Builder<RootFolder>.CreateListOfSize(2)
                .All()
                .With(f => f.DefaultMetadataProfileId = 2)
                .BuildList();

            Mocker.GetMock<IAuthorService>().Setup(c => c.GetAllAuthors()).Returns(authorList);
            Mocker.GetMock<IImportListFactory>().Setup(c => c.All()).Returns(importLists);
            Mocker.GetMock<IRootFolderService>().Setup(c => c.All()).Returns(rootFolders);
            Mocker.GetMock<IMetadataProfileRepository>().Setup(c => c.Get(profile.Id)).Returns(profile);

            Subject.Delete(1);

            Mocker.GetMock<IMetadataProfileRepository>().Verify(c => c.Delete(1), Times.Once());
        }

        [Test]
        public void should_not_filter_books_by_popularity_when_author_has_no_rating_votes()
        {
            var profile = BuildMetadataProfile();
            var remoteBooks = new List<Book>
            {
                BuildBook("book-1", "First Book", votes: 0),
                BuildBook("book-2", "Second Book", votes: 0)
            };

            Mocker.GetMock<IMetadataProfileRepository>()
                .Setup(x => x.Get(profile.Id))
                .Returns(profile);
            Mocker.GetMock<IAuthorService>()
                .Setup(x => x.FindById("author-1"))
                .Returns((Author)null);
            Mocker.GetMock<IMediaFileService>()
                .Setup(x => x.GetFilesByAuthor(0))
                .Returns(new List<BookFile>());

            var result = Subject.FilterBooks(BuildAuthor("author-1", remoteBooks), profile.Id);

            Assert.That(result.Select(x => x.ForeignBookId), Is.EquivalentTo(remoteBooks.Select(x => x.ForeignBookId)));
        }

        [Test]
        public void should_still_filter_books_by_popularity_when_any_rating_votes_exist()
        {
            var profile = BuildMetadataProfile();
            var lowPopularity = BuildBook("book-1", "Low Popularity", votes: 0);
            var highPopularity = BuildBook("book-2", "High Popularity", votes: 100, value: 4.5m);
            var remoteBooks = new List<Book> { lowPopularity, highPopularity };

            Mocker.GetMock<IMetadataProfileRepository>()
                .Setup(x => x.Get(profile.Id))
                .Returns(profile);
            Mocker.GetMock<IAuthorService>()
                .Setup(x => x.FindById("author-1"))
                .Returns((Author)null);
            Mocker.GetMock<IMediaFileService>()
                .Setup(x => x.GetFilesByAuthor(0))
                .Returns(new List<BookFile>());

            var result = Subject.FilterBooks(BuildAuthor("author-1", remoteBooks), profile.Id);

            Assert.That(result.Select(x => x.ForeignBookId), Is.EquivalentTo(new[] { highPopularity.ForeignBookId }));
        }

        private static MetadataProfile BuildMetadataProfile()
        {
            return new MetadataProfile
            {
                Id = 1,
                Name = "Standard",
                MinPopularity = 350,
                SkipMissingDate = false,
                SkipMissingIsbn = false,
                SkipPartsAndSets = false,
                SkipSeriesSecondary = false,
                AllowedLanguages = string.Empty,
                MinPages = 0,
                Ignored = new List<string>()
            };
        }

        private static Author BuildAuthor(string foreignAuthorId, List<Book> books)
        {
            return new Author
            {
                Metadata = new LazyLoaded<AuthorMetadata>(new AuthorMetadata { ForeignAuthorId = foreignAuthorId }),
                Books = new LazyLoaded<List<Book>>(books),
                Series = new LazyLoaded<List<Series>>(new List<Series>())
            };
        }

        private static Book BuildBook(string foreignBookId, string title, int votes, decimal value = 0m)
        {
            var book = new Book
            {
                ForeignBookId = foreignBookId,
                Title = title,
                Ratings = new Ratings
                {
                    Votes = votes,
                    Value = value
                },
                ReleaseDate = new DateTime(2023, 1, 1),
                Editions = new LazyLoaded<List<Edition>>(new List<Edition>
                {
                    new Edition
                    {
                        ForeignEditionId = $"{foreignBookId}-edition-1",
                        Title = $"{title} Edition",
                        Isbn13 = "9781234567890",
                        PageCount = 250,
                        Ratings = new Ratings()
                    }
                })
            };

            return book;
        }
    }
}
