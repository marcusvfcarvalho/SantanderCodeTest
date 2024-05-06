using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using SantanderCodeTest.DTO;

namespace SantanderCodeTest.Controllers;

[Route("api/[controller]")]
[ApiController]
public class BestStoriesController(IMemoryCache cache, 
    ILogger<BestStoriesController> logger,
    HttpClient httpClient) : ControllerBase
{
    private readonly IMemoryCache cache = cache;
    private readonly ILogger<BestStoriesController> logger = logger;
    private readonly HttpClient httpClient = httpClient;

    private const int ExpirationInMinutes = 1;
    private const int ExpirationInHours = 4;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<StoryDetail>>> BestStoriesAsync([FromQuery] int limit = 10)
    {
        List<StoryDetail> stories = [];

        List<int> idList = [];
        List<int> filtered = [];

        try
        {
            if (cache.TryGetValue("best-stories", out List<int>? cachedBestStories))
            {
                idList = cachedBestStories!;
                filtered = idList.Take(Math.Min(idList.Count, limit)).ToList();
            }
            else
            {
                HttpResponseMessage response = await httpClient.GetAsync("https://hacker-news.firebaseio.com/v0/beststories.json");
                if (response.IsSuccessStatusCode)
                {
                    string responseBody = await response.Content.ReadAsStringAsync();
                    idList = JsonConvert.DeserializeObject<List<int>>(responseBody)!;
                    cache.Set("best-stories", idList, new DateTimeOffset(DateTime.Now.AddMinutes(ExpirationInMinutes)));
                    cache.Set("best-stories-backup", idList);
                    filtered = idList.Take(Math.Min(idList.Count, limit)).ToList();

                }
            }
        }
        catch (HttpRequestException )
        {
            if(cache.TryGetValue("best-stories-backup", out List<int>? cachedBestStories))
            {
                cache.Set("best-stories", cachedBestStories, new DateTimeOffset(DateTime.Now.AddMinutes(ExpirationInMinutes)));
                filtered = cachedBestStories!.Take(Math.Min(cachedBestStories!.Count, limit)).ToList();
            } else
            {
                return stories;
            }
        }

        List<Task<StoryEntry?>> tasks = [];

        foreach (int id in filtered)
        {

            if (!cache.TryGetValue(id, out StoryDetail? cachedStory))
            {
                tasks.Add(CallEndpointAsync(id));
            }
            else
            {
                stories.Add(cachedStory!);
            }
        }

        if (tasks.Count!=0)
        {
            await Task.WhenAll(tasks);

            foreach (var task in tasks)
            {
                if (task.Result?.StoryDetail != null)
                {
                    stories.Add(task.Result!.StoryDetail);
                    cache.Set(task.Result!.Id, task.Result!.StoryDetail, new DateTimeOffset(DateTime.Now.AddHours(ExpirationInHours)));
                }
            }
        }

        if (stories.Count!=0)
        {
            return stories.OrderByDescending(x => x.Score).ToList();
        }

        return new List<StoryDetail>();
    }

    async Task<StoryEntry?> CallEndpointAsync(int id)
    {
        
        try
        {
            HttpResponseMessage response = await httpClient.GetAsync($"https://hacker-news.firebaseio.com/v0/item/{id}.json");

            if (response.IsSuccessStatusCode)
            {
                string responseBody = await response.Content.ReadAsStringAsync();
                var hackerNewsItem = JsonConvert.DeserializeObject<HackerNewsItem>(responseBody)!;

                return new StoryEntry(id, new StoryDetail()
                {
                    Uri = hackerNewsItem.Url,
                    Title = hackerNewsItem.Title,
                    CommentCount = hackerNewsItem.Descendants,
                    PostedBy = hackerNewsItem.By,
                    Score = hackerNewsItem.Score,
                    Time = hackerNewsItem!.Time != null ? UnixTimeStampToDateTime(hackerNewsItem.Time!.Value) : null
                });
            }
            else
            {
                logger.LogError("Failed to fetch data from {Id}. Status code: {StatusCode}", id, response.StatusCode);

            }
        }
        catch (HttpRequestException e)
        {
            logger.LogError("Request exception: {message}", e.Message);
        }

        return null;
    }

    public static DateTime UnixTimeStampToDateTime(long unixTimeStamp)
    {
        DateTime dateTime = new(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
        dateTime = dateTime.AddSeconds(unixTimeStamp).ToLocalTime();
        return dateTime;
    }
}
public record StoryEntry(int Id, StoryDetail StoryDetail);
