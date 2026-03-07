using NzbDrone.Common.Cloud;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.MetadataSource
{
    public interface IMetadataRequestBuilder
    {
        IHttpRequestBuilderFactory GetRequestBuilder();
    }

    public class MetadataRequestBuilder : IMetadataRequestBuilder
    {
        private readonly IConfigService _configService;

        private readonly IReadarrCloudRequestBuilder _defaultRequestFactory;

        public MetadataRequestBuilder(IConfigService configService, IReadarrCloudRequestBuilder defaultRequestBuilder)
        {
            _configService = configService;
            _defaultRequestFactory = defaultRequestBuilder;
        }

        public IHttpRequestBuilderFactory GetRequestBuilder()
        {
            HttpRequestBuilder builder;

            if (_configService.MetadataSource.IsNotNullOrWhiteSpace())
            {
                builder = new HttpRequestBuilder(_configService.MetadataSource.TrimEnd("/") + "/{route}").KeepAlive();
            }
            else
            {
                builder = _defaultRequestFactory.Metadata.Create();
            }

            return builder
                .SetHeader("Accept-Encoding", "gzip")
                .CreateFactory();
        }
    }
}
