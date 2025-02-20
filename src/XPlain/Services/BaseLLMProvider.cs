using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.CompilerServices;

namespace XPlain.Services;

public abstract class BaseLLMProvider : ILLMProvider
{
    protected readonly ICacheProvider _cacheProvider;
    protected readonly IRateLimitingService _rateLimitingService;

    protected BaseLLMProvider(
        ICacheProvider cacheProvider,
        IRateLimitingService rateLimitingService)
    {
        _cacheProvider = cacheProvider;
        _rateLimitingService = rateLimitingService;
    }

    public abstract string ProviderName { get; }
    public abstract string ModelName { get; }

    protected abstract Task<string> GetCompletionInternalAsync(string prompt);
    
    protected abstract IAsyncEnumerable<string> GetCompletionStreamInternalAsync(string prompt);

    public async Task<string> GetCompletionAsync(string prompt)
    {
        // Generate cache key from prompt
        var cacheKey = GenerateCacheKey(prompt);

        // Try to get from cache first
        var cachedResponse = await _cacheProvider.GetAsync<string>(cacheKey);
        if (cachedResponse != null)
        {
            return cachedResponse;
        }

        // Get new completion with rate limiting
        await _rateLimitingService.AcquirePermitAsync(ProviderName);
        try
        {
            var response = await GetCompletionInternalAsync(prompt);
            
            // Cache the response
            await _cacheProvider.SetAsync(cacheKey, response);
            
            return response;
        }
        finally
        {
            _rateLimitingService.ReleasePermit(ProviderName);
        }
    }


    public async Task<IAsyncEnumerable<string>> GetCompletionStreamAsync(
        string prompt,
        CancellationToken cancellationToken = default)
    {
        // Generate cache key from prompt
        var cacheKey = GenerateCacheKey(prompt);

        // Try to get from cache first
        var cachedResponse = await _cacheProvider.GetAsync<string>(cacheKey);
        if (cachedResponse != null)
        {
            // For cached responses, return as a single chunk
            return AsyncEnumerable.Singleton(cachedResponse);
        }

        // Get new completion with rate limiting
        await _rateLimitingService.AcquirePermitAsync(ProviderName, cancellationToken: cancellationToken);
        
        // Create a StringBuilder to accumulate the full response for caching
        var fullResponse = new StringBuilder();

        // Return an async stream that accumulates the response and caches it when complete
        async IAsyncEnumerable<string> StreamWithCaching(
            [EnumeratorCancellation] CancellationToken streamCancellationToken)
        {
            try
            {
                await foreach (var chunk in GetCompletionStreamInternalAsync(prompt)
                    .WithCancellation(streamCancellationToken))
                {
                    if (streamCancellationToken.IsCancellationRequested)
                    {
                        // Don't cache partial responses when cancelled
                        _rateLimitingService.ReleasePermit(ProviderName);
                        yield break;
                    }

                    fullResponse.Append(chunk);
                    yield return chunk;
                }

                // Only cache complete responses
                if (!streamCancellationToken.IsCancellationRequested)
                {
                    var completeResponse = fullResponse.ToString();
                    await _cacheProvider.SetAsync(cacheKey, completeResponse);
                }
            }
            finally
            {
                _rateLimitingService.ReleasePermit(ProviderName);
            }
        }

        return StreamWithCaching(cancellationToken);
    }

    private string GenerateCacheKey(string prompt)
    {
        // Include model info in cache key to ensure different models get different caches
        var keyInput = $"{ProviderName}:{ModelName}:{prompt}";

        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(keyInput));
        return Convert.ToBase64String(hashBytes);
    }
}