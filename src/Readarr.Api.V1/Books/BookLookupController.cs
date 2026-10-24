using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Organizer;
using Readarr.Api.V1.Author;
using Readarr.Http;

namespace Readarr.Api.V1.Books
{
    [V1ApiController("book/lookup")]
    public class BookLookupController : Controller
    {
        private readonly ISearchForNewBook _searchProxy;
        private readonly IBuildFileNames _fileNameBuilder;
        private readonly IMapCoversToLocal _coverMapper;

        public BookLookupController(ISearchForNewBook searchProxy, IBuildFileNames fileNameBuilder, IMapCoversToLocal coverMapper)
        {
            _searchProxy = searchProxy;
            _fileNameBuilder = fileNameBuilder;
            _coverMapper = coverMapper;
        }

        [HttpGet]
        public object Search(string term)
        {
            var searchResults = _searchProxy.SearchForNewBook(term, null);
            return MapToResource(searchResults).ToList();
        }

        private IEnumerable<BookResource> MapToResource(IEnumerable<NzbDrone.Core.Books.Book> books)
        {
            foreach (var currentBook in books)
            {
                var resource = currentBook.ToResource();
                resource.Author = currentBook.Author.Value.ToResource();

                _coverMapper.ConvertToLocalUrls(resource.Id, MediaCoverEntity.Book, resource.Images);
                _coverMapper.ConvertToLocalUrls(resource.Author.Id, MediaCoverEntity.Author, resource.Author.Images);

                var cover = resource.Images.FirstOrDefault(c => c.CoverType == MediaCoverTypes.Cover);
                var poster = resource.Author.Images.FirstOrDefault(c => c.CoverType == MediaCoverTypes.Poster);

                if (cover != null)
                {
                    resource.RemoteCover = cover.RemoteUrl;
                }

                if (poster != null)
                {
                    resource.Author.RemotePoster = poster.RemoteUrl;
                }

                resource.Author.Folder = _fileNameBuilder.GetAuthorFolder(currentBook.Author);

                yield return resource;
            }
        }
    }
}
