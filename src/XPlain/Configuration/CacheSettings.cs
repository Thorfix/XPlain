namespace XPlain.Configuration
{
    public class CacheSettings
    {
        public bool CacheEnabled { get; set; } = true;
        public string? CacheDirectory { get; set; }
        public int CacheExpirationHours { get; set; } = 24;
    }
}