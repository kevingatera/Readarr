using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Test.Framework;
using GoodreadsAuthorizationHeader = NzbDrone.Core.ImportLists.Goodreads.AuthorizationHeader;
using GoodreadsBookshelfNotificationSettings = NzbDrone.Core.Notifications.Goodreads.GoodreadsBookshelfNotificationSettings;
using GoodreadsNotificationBookshelf = NzbDrone.Core.Notifications.Goodreads.GoodreadsBookshelf;

namespace NzbDrone.Core.Test.NotificationTests
{
    [TestFixture]
    public class GoodreadsBookshelfFixture : CoreTest<GoodreadsNotificationBookshelf>
    {
        private int _pageOneRequests;

        [SetUp]
        public void SetUp()
        {
            _pageOneRequests = 0;

            Subject.Definition = new NotificationDefinition
            {
                Settings = new GoodreadsBookshelfNotificationSettings
                {
                    AccessToken = "token",
                    AccessTokenSecret = "secret",
                    UserId = "12345",
                    UserName = "tester",
                    RemoveIds = new[] { "to-read" },
                    AddIds = new string[] { }
                }
            };

            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Post<GoodreadsAuthorizationHeader>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request => AuthResponse(request));

            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Execute(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request => GoodreadsResponse(request));
        }

        [Test]
        public void should_continue_to_later_shelf_pages_when_removing_books()
        {
            var author = new Author
            {
                Metadata = new LazyLoaded<AuthorMetadata>(new AuthorMetadata { Name = "Some Author" })
            };

            var book = new Book
            {
                Title = "Target Book",
                ForeignBookId = "222",
                Author = new LazyLoaded<Author>(author)
            };

            Subject.OnBookDelete(new BookDeleteMessage(book, true));

            Mocker.GetMock<IHttpClient>()
                .Verify(c => c.Execute(It.Is<HttpRequest>(r => r.Url.Path.Contains("review/list.xml") && r.Url.Query.Contains("page=1"))), Times.Once());

            Mocker.GetMock<IHttpClient>()
                .Verify(c => c.Execute(It.Is<HttpRequest>(r => r.Url.Path.Contains("review/list.xml") && r.Url.Query.Contains("page=2"))), Times.Once());

            Mocker.GetMock<IHttpClient>()
                .Verify(c => c.Execute(It.Is<HttpRequest>(r => r.Url.Path.Contains("shelf/add_to_shelf.xml"))), Times.Once());
        }

        private HttpResponse<GoodreadsAuthorizationHeader> AuthResponse(HttpRequest request)
        {
            var response = new HttpResponse(
                request,
                new HttpHeader { ContentType = "application/json" },
                "{\"authorization\":\"OAuth test\"}");

            return new HttpResponse<GoodreadsAuthorizationHeader>(response);
        }

        private HttpResponse GoodreadsResponse(HttpRequest request)
        {
            if (request.Url.Path.Contains("review/list.xml") && request.Url.Query.Contains("page=1"))
            {
                _pageOneRequests += 1;
                if (_pageOneRequests > 1)
                {
                    throw new System.InvalidOperationException("Repeated page 1 request");
                }

                return new HttpResponse(request, new HttpHeader { ContentType = "application/xml" }, FirstPageResponse());
            }

            if (request.Url.Path.Contains("review/list.xml") && request.Url.Query.Contains("page=2"))
            {
                return new HttpResponse(request, new HttpHeader { ContentType = "application/xml" }, SecondPageResponse());
            }

            if (request.Url.Path.Contains("shelf/add_to_shelf.xml"))
            {
                return new HttpResponse(request, new HttpHeader { ContentType = "application/xml" }, "<GoodreadsResponse />");
            }

            Assert.Fail($"Unexpected Goodreads request: {request.Url.FullUri}");
            return null;
        }

        private string FirstPageResponse()
        {
            return @"
<GoodreadsResponse>
  <reviews start='1' end='1' total='2'>
    <review>
      <id>1</id>
      <book>
        <id>11</id>
        <work>
          <id>111</id>
        </work>
        <title>Other Book</title>
        <title_without_series>Other Book</title_without_series>
        <authors>
          <author>
            <id>7</id>
            <name>Some Author</name>
          </author>
        </authors>
      </book>
    </review>
  </reviews>
</GoodreadsResponse>";
        }

        private string SecondPageResponse()
        {
            return @"
<GoodreadsResponse>
  <reviews start='2' end='2' total='2'>
    <review>
      <id>2</id>
      <book>
        <id>55</id>
        <work>
          <id>222</id>
        </work>
        <title>Target Book</title>
        <title_without_series>Target Book</title_without_series>
        <authors>
          <author>
            <id>7</id>
            <name>Some Author</name>
          </author>
        </authors>
      </book>
    </review>
  </reviews>
</GoodreadsResponse>";
        }
    }
}
