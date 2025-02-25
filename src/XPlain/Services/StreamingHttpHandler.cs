using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using XPlain.Configuration;

namespace XPlain.Services
{
    public class StreamingHttpHandler : DelegatingHandler
    {
        private readonly StreamingSettings _settings;

        public StreamingHttpHandler(StreamingSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            InnerHandler = new HttpClientHandler();
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, 
            CancellationToken cancellationToken)
        {
            // Set timeout for streaming requests
            var timeoutToken = new CancellationTokenSource(
                TimeSpan.FromSeconds(_settings.StreamingTimeoutSeconds)).Token;
            
            // Create a linked token that will cancel if either the original token
            // or the timeout token is cancelled
            using var linkedCts = 
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutToken);

            try
            {
                return await base.SendAsync(request, linkedCts.Token);
            }
            catch (OperationCanceledException) when (timeoutToken.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"The request timed out after {_settings.StreamingTimeoutSeconds} seconds");
            }
        }
    }
}