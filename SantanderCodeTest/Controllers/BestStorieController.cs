using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SantanderCodeTest.DTO;

namespace SantanderCodeTest.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BestStoriesController : ControllerBase
    {
        private readonly IMemoryCache memoryCache;
        private readonly ILogger<BestStoriesController> logger;
        private readonly HttpClient httpClient;

        private const int CacheExpirationInMinutes = 1;
        private const int CacheExpirationInHours = 4;

        public BestStoriesController(IMemoryCache memoryCache, ILogger<BestStoriesController> logger, HttpClient httpClient)
        {
            this.memoryCache = memoryCache;
            this.logger = logger;
            this.httpClient = httpClient;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<StoryDetail>>> GetBestStoriesAsync([FromQuery] int limit = 10, CancellationToken cancellationToken = default)
        {
            List<StoryDetail> bestStories = [];
            List<int> storyIds = [];
            List<int> limitedStoryIds = [];

            try
            {
                if (memoryCache.TryGetValue("best-stories", out List<int>? cachedStoryIds))
                {
                    storyIds = cachedStoryIds!;
                    limitedStoryIds = storyIds.Take(Math.Min(storyIds.Count, limit)).ToList();
                }
                else
                {
                    HttpResponseMessage response = await httpClient.GetAsync("https://hacker-news.firebaseio.com/v0/beststories.json", cancellationToken);
                    if (response.IsSuccessStatusCode)
                    {
                        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                        storyIds = JsonConvert.DeserializeObject<List<int>>(responseBody)!;
                        memoryCache.Set("best-stories", storyIds, new DateTimeOffset(DateTime.Now.AddMinutes(CacheExpirationInMinutes)));
                        memoryCache.Set("best-stories-backup", storyIds);
                        limitedStoryIds = storyIds.Take(Math.Min(storyIds.Count, limit)).ToList();
                    }
                }
            }
            catch (HttpRequestException)
            {
                if (memoryCache.TryGetValue("best-stories-backup", out List<int>? backupStoryIds))
                {
                    memoryCache.Set("best-stories", backupStoryIds, new DateTimeOffset(DateTime.Now.AddMinutes(CacheExpirationInMinutes)));
                    limitedStoryIds = backupStoryIds!.Take(Math.Min(backupStoryIds!.Count, limit)).ToList();
                }
                else
                {
                    return bestStories;
                }
            }

            List<Task<StoryEntry?>> fetchStoryTasks = new();

            foreach (int storyId in limitedStoryIds)
            {
                if (!memoryCache.TryGetValue(storyId, out StoryDetail? cachedStory))
                {
                    fetchStoryTasks.Add(FetchStoryDetailsAsync(storyId, cancellationToken));
                }
                else
                {
                    bestStories.Add(cachedStory!);
                }
            }

            if (fetchStoryTasks.Count != 0)
            {
                await Task.WhenAll(fetchStoryTasks);

                foreach (var task in fetchStoryTasks)
                {
                    if (task.Result?.StoryDetail != null)
                    {
                        bestStories.Add(task.Result!.StoryDetail);
                        memoryCache.Set(task.Result!.Id, task.Result!.StoryDetail, new DateTimeOffset(DateTime.Now.AddHours(CacheExpirationInHours)));
                    }
                }
            }

            if (bestStories.Count != 0)
            {
                return bestStories.OrderByDescending(story => story.Score).ToList();
            }

            return new List<StoryDetail>();
        }

        private async Task<StoryEntry?> FetchStoryDetailsAsync(int storyId, CancellationToken cancellationToken)
        {
            try
            {
                HttpResponseMessage response = await httpClient.GetAsync($"https://hacker-news.firebaseio.com/v0/item/{storyId}.json", cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    var hackerNewsItem = JsonConvert.DeserializeObject<HackerNewsItem>(responseBody)!;

                    return new StoryEntry(storyId, new StoryDetail()
                    {
                        Uri = hackerNewsItem.Url,
                        Title = hackerNewsItem.Title,
                        CommentCount = hackerNewsItem.Descendants,
                        PostedBy = hackerNewsItem.By,
                        Score = hackerNewsItem.Score,
                        Time = hackerNewsItem.Time != null ? UnixTimeStampToDateTime(hackerNewsItem.Time!.Value) : null
                    });
                }
                else
                {
                    logger.LogError("Failed to fetch story with ID: {Id}. Status code: {StatusCode}", storyId, response.StatusCode);
                }
            }
            catch (HttpRequestException e)
            {
                logger.LogError("Request exception: {Message}", e.Message);
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation("Request was canceled for story with ID: {Id}", storyId);
            }

            return null;
        }

        public static DateTime UnixTimeStampToDateTime(long unixTimeStamp)
        {
            DateTime epochStart = new(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            return epochStart.AddSeconds(unixTimeStamp).ToLocalTime();
        }
    }

    public record StoryEntry(int Id, StoryDetail StoryDetail);
}
