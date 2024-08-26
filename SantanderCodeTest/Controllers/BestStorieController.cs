using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using SantanderCodeTest.DTO;
using System.Threading;

namespace SantanderCodeTest.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BestStoriesController : ControllerBase
    {
        private readonly IMemoryCache memoryCache;
        private readonly ILogger<BestStoriesController> logger;
        private readonly HttpClient httpClient;
        private readonly HackerNewsApiSettings apiSettings;

        public BestStoriesController(IMemoryCache memoryCache, ILogger<BestStoriesController> logger, HttpClient httpClient, IOptions<HackerNewsApiSettings> apiSettings)
        {
            this.memoryCache = memoryCache;
            this.logger = logger;
            this.httpClient = httpClient;
            this.apiSettings = apiSettings.Value;
        }

        private async Task PopulateCache()
        {
            try
            {
                string url = $"{apiSettings.BaseUrl}{apiSettings.BestStoriesEndpoint}";
                HttpResponseMessage response = await httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    string responseBody = await response.Content.ReadAsStringAsync();
                    var cacheIds = JsonConvert.DeserializeObject<List<int>>(responseBody)!;
                    memoryCache.Set("best-stories", cacheIds, new DateTimeOffset(DateTime.Now.AddHours(apiSettings.CacheExpirationInHours)));
                    memoryCache.Set("best-stories-backup", cacheIds);
                    return;
                }
                throw new HttpRequestException();
            }
            catch
            {
                // If something fails, use previous backup values or empty
                memoryCache.Set("best-stories", memoryCache.Get("best-stories-backup") ?? new List<int>());
                memoryCache.Set("best-stories-backup", memoryCache.Get("best-stories-backup") ?? new List<int>());
            }
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<StoryDetail>>> GetBestStoriesAsync([FromQuery] int pageSize = 10, int page = 1, CancellationToken cancellationToken = default)
        {
            if (pageSize < 1 || page < 1)
            {
                return BadRequest("PageSize and Page cannot be less than 1");
            }

            List<StoryDetail> bestStories = new();
            List<int> cachedStoryIds = await GetCachedStories();

            var pagedStoryIds = cachedStoryIds.Skip((page - 1) * pageSize)
                   .Take(pageSize)
                   .ToList();

            List<Task<StoryEntry?>> fetchStoryTasks = new();

            foreach (int storyId in pagedStoryIds)
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

            await Task.WhenAll(fetchStoryTasks);

            foreach (var task in fetchStoryTasks.Where(x => x.Result?.StoryDetail != null))
            {
                bestStories.Add(task.Result!.StoryDetail);
                memoryCache.Set(task.Result!.Id, task.Result!.StoryDetail, new DateTimeOffset(DateTime.Now.AddHours(apiSettings.CacheExpirationInHours)));
            }

            return bestStories.OrderByDescending(story => story.Score).ToList();
        }

        private async Task<List<int>> GetCachedStories()
        {
            if (!memoryCache.TryGetValue("best-stories", out List<int>? cachedStories)) // Cache empty or expired
            {
                await PopulateCache();
                cachedStories = (List<int>?)memoryCache.Get("best-stories");
            }

            return cachedStories!;
        }

        private async Task<StoryEntry?> FetchStoryDetailsAsync(int storyId, CancellationToken cancellationToken)
        {
            try
            {
                string url = $"{apiSettings.BaseUrl}{string.Format(apiSettings.StoryDetailsEndpoint, storyId)}";
                HttpResponseMessage response = await httpClient.GetAsync(url, cancellationToken);

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

                logger.LogError("Failed to fetch story with ID: {Id}. Status code: {StatusCode}", storyId, response.StatusCode);
                return null;
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
