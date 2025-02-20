using System.Net;

namespace XPlain.Services;

/// <summary>
/// HTTP message handler that provides better stability for streaming responses
/// </summary>
public class StreamingHttpHandler : DelegatingHandler
{
    private readonly TimeSpan _timeout;
    private readonly int _maxRetries;
    private readonly TimeSpan _initialRetryDelay;

    public StreamingHttpHandler(StreamingSettings settings)
    {
        _timeout = TimeSpan.FromSeconds(settings.StreamingTimeoutSeconds);
        _maxRetries = settings.MaxStreamingRetries;
        _initialRetryDelay = TimeSpan.FromMilliseconds(settings.InitialRetryDelayMs);
        InnerHandler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var retryCount = 0;
        var delay = _initialRetryDelay;

        while (true)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(_timeout);

                var response = await base.SendAsync(request, cts.Token);
                
                if ((int)response.StatusCode >= 500 && retryCount < _maxRetries)
                {
                    await Task.Delay(delay, cancellationToken);
                    delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * 2);
                    retryCount++;
                    continue;
                }

                response.EnsureSuccessStatusCode();
                return response;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (retryCount >= _maxRetries)
                    throw new TimeoutException($"Request timed out after {_timeout.TotalSeconds} seconds");

                await Task.Delay(delay, cancellationToken);
                delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * 2);
                retryCount++;
            }
            catch (HttpRequestException ex) when (retryCount < _maxRetries)
            {
                await Task.Delay(delay, cancellationToken);
                delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * 2);
                retryCount++;
            }
        }
    }
}