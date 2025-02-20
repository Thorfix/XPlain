namespace XPlain.Configuration;

public class StreamingSettings
{
    /// <summary>
    /// Whether streaming is enabled by default
    /// </summary>
    public bool EnableStreamingByDefault { get; set; } = false;

    /// <summary>
    /// Maximum timeout for streaming connections in seconds
    /// </summary>
    public int StreamingTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Number of retry attempts for failed streaming connections
    /// </summary>
    public int MaxStreamingRetries { get; set; } = 3;

    /// <summary>
    /// Initial delay between retries in milliseconds
    /// </summary>
    public int InitialRetryDelayMs { get; set; } = 1000;
}