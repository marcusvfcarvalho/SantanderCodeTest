namespace SantanderCodeTest;

public class HackerNewsApiSettings
{
    public string BaseUrl { get; set; } = string.Empty;
    public string BestStoriesEndpoint { get; set; } = string.Empty;
    public string StoryDetailsEndpoint { get; set; } = string.Empty;
    public int CacheExpirationInHours { get; set; } = 4; 
}


