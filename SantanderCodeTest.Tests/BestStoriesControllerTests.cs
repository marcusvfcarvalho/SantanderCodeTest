using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Newtonsoft.Json;
using SantanderCodeTest.Controllers;
using SantanderCodeTest.DTO;
using System.Net;
using System.Text.RegularExpressions;

namespace SantanderCodeTest.Tests;

[TestFixture]
public class BestStoriesControllerTests
{
    private static IOptions<HackerNewsApiSettings> CreateApiSettings(int cacheExpirationInHours)
    {
        var apiSettings = new HackerNewsApiSettings
        {
            BaseUrl = "https://hacker-news.firebaseio.com/",
            BestStoriesEndpoint = "v0/beststories.json",
            StoryDetailsEndpoint = "v0/item/{0}.json",
            CacheExpirationInHours = cacheExpirationInHours
        };
        return Options.Create(apiSettings);
    }

    private static BestStoriesController CreateController(IOptions<HackerNewsApiSettings> options)
    {
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var loggerMock = new Mock<ILogger<BestStoriesController>>();
        var httpClient = new HttpClient(new Mock<HttpMessageHandler>(MockBehavior.Strict).Object);
        return new BestStoriesController(memoryCache, loggerMock.Object, httpClient, options);
    }

    private static Mock<HttpMessageHandler> CreateHttpMessageHandlerMock(HttpStatusCode statusCode, string content = "")
    {
        var httpMessageHandlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = new StringContent(content)
            });
        return httpMessageHandlerMock;
    }

    [Test]
    public async Task BestStories_Returns_Cache_If_HackerNews_API_Fails_During_Call()
    {
        // Arrange
        var apiSettings = CreateApiSettings(4);
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        memoryCache.Set(1, new StoryDetail { Title = "Story 1", Time = new DateTime(2024, 12, 31) });
        memoryCache.Set(2, new StoryDetail { Title = "Story 2", Time = new DateTime(2024, 12, 30) });
        memoryCache.Set(3, new StoryDetail { Title = "Story 3", Time = new DateTime(2024, 12, 29) });

        var httpMessageHandlerMock = CreateHttpMessageHandlerMock(HttpStatusCode.OK, @"[1, 2, 3, 4, 5]");
        httpMessageHandlerMock
          .Protected()
          .Setup<Task<HttpResponseMessage>>(
              "SendAsync",
              ItExpr.IsAny<HttpRequestMessage>(),
              ItExpr.IsAny<CancellationToken>()
          )
          .ThrowsAsync(new HttpRequestException());


        var httpClient = new HttpClient(httpMessageHandlerMock.Object);
        var controller = new BestStoriesController(memoryCache, new Mock<ILogger<BestStoriesController>>().Object, httpClient, apiSettings);

        // Act
        var result = await controller.GetBestStoriesAsync();

        // Assert
        Assert.That(memoryCache.Count, Is.EqualTo(5)); // 5 items in cache
    }

    [Test]
    public async Task BestStories_Returns_Cache_If_HackerNews_API_Is_Down()
    {
        // Arrange
        var apiSettings = CreateApiSettings(4);
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        memoryCache.Set("best-stories-backup", new List<int> { 1, 2, 3 });
        memoryCache.Set(1, new StoryDetail { Title = "Story 1", Time = new DateTime(2024, 12, 31) });
        memoryCache.Set(2, new StoryDetail { Title = "Story 2", Time = new DateTime(2024, 12, 30) });
        memoryCache.Set(3, new StoryDetail { Title = "Story 3", Time = new DateTime(2024, 12, 29) });

        var httpMessageHandlerMock = CreateHttpMessageHandlerMock(HttpStatusCode.InternalServerError);
        var httpClient = new HttpClient(httpMessageHandlerMock.Object);
        var controller = new BestStoriesController(memoryCache, new Mock<ILogger<BestStoriesController>>().Object, httpClient, apiSettings);

        // Act
        var result = await controller.GetBestStoriesAsync();

        // Assert: Verify the cache returns 3 cached stories
        Assert.That(result.Value!.Count, Is.EqualTo(3));
        Assert.That(result.Value!.First().Title, Is.EqualTo("Story 1"));
    }

    [Test]
    public async Task BestStories_Returns_Cached_Stories_If_Available()
    {
        // Arrange
        var apiSettings = CreateApiSettings(4);
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        memoryCache.Set("best-stories", new List<int> { 1, 2, 3 });
        memoryCache.Set(1, new StoryDetail { Title = "Story 1" });
        memoryCache.Set(2, new StoryDetail { Title = "Story 2" });
        memoryCache.Set(3, new StoryDetail { Title = "Story 3" });

        var httpMessageHandlerMock = CreateHttpMessageHandlerMock(HttpStatusCode.OK);
        var httpClient = new HttpClient(httpMessageHandlerMock.Object);
        var controller = new BestStoriesController(memoryCache, new Mock<ILogger<BestStoriesController>>().Object, httpClient, apiSettings);

        // Act
        var result = await controller.GetBestStoriesAsync();

        // Assert: Check cache count and titles
        Assert.Multiple(() =>
        {
            Assert.That(memoryCache.Count, Is.EqualTo(4)); // 1 list + 3 stories
            Assert.That(((StoryDetail)memoryCache.Get(1)!).Title, Is.EqualTo("Story 1"));
            Assert.That(((StoryDetail)memoryCache.Get(2)!).Title, Is.EqualTo("Story 2"));
            Assert.That(((StoryDetail)memoryCache.Get(3)!).Title, Is.EqualTo("Story 3"));
        });
    }

    [Test]
    public async Task BestStories_Makes_API_Call_If_Stories_Not_Cached()
    {
        // Arrange
        var apiSettings = CreateApiSettings(4);
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var httpMessageHandlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync((HttpRequestMessage request, CancellationToken cancellationToken) =>
            {
                if (request.RequestUri!.AbsoluteUri == "https://hacker-news.firebaseio.com/v0/beststories.json")
                {
                    return new HttpResponseMessage
                    {
                        StatusCode = HttpStatusCode.OK,
                        Content = new StringContent(@"[1, 2, 3]")
                    };
                }
                else if (IsStoryDetailUrl(request.RequestUri.AbsoluteUri))
                {
                    return new HttpResponseMessage
                    {
                        StatusCode = HttpStatusCode.OK,
                        Content = new StringContent(@"
                                {
                                    ""title"": ""Story from API"",
                                    ""uri"": ""https://example.com/story"",
                                    ""postedBy"": ""user123"",
                                    ""time"": 1570887781,
                                    ""score"": 1500,
                                    ""commentCount"": 500
                                }")
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            });

        var httpClient = new HttpClient(httpMessageHandlerMock.Object);
        var controller = new BestStoriesController(memoryCache, new Mock<ILogger<BestStoriesController>>().Object, httpClient, apiSettings);

        // Act
        var result = await controller.GetBestStoriesAsync();

        // Assert: Verify that cache is populated after API call
        Assert.That(memoryCache.Count, Is.EqualTo(5)); // 1 list, 1 backup, 3 stories
    }

    [Test]
    public async Task BestStories_Returns_Empty_List_If_API_Is_Down_And_Cache_Is_Empty()
    {
        // Arrange
        var apiSettings = CreateApiSettings(4);
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var httpMessageHandlerMock = CreateHttpMessageHandlerMock(HttpStatusCode.InternalServerError);
        var httpClient = new HttpClient(httpMessageHandlerMock.Object);
        var controller = new BestStoriesController(memoryCache, new Mock<ILogger<BestStoriesController>>().Object, httpClient, apiSettings);

        // Act
        var result = await controller.GetBestStoriesAsync();

        // Assert: Verify that an empty list is returned if API fails and no cache
        Assert.That(result.Value, Is.Empty);
    }

    [Test]
    public async Task BestStories_Returns_Limited_Stories_And_Correct_Page()
    {
        // Arrange
        var apiSettings = CreateApiSettings(4);
        var memoryCache = new MemoryCache(new MemoryCacheOptions());

        var storyIds = Enumerable.Range(1, 100).ToList();
        memoryCache.Set("best-stories", storyIds);

        foreach (var id in storyIds)
        {
            memoryCache.Set(id, new StoryDetail { Title = $"Story {id}", Score = id });
        }

        var httpClient = new HttpClient(new Mock<HttpMessageHandler>().Object);
        var controller = new BestStoriesController(memoryCache, new Mock<ILogger<BestStoriesController>>().Object, httpClient, apiSettings);

        // Act
        var result = (await controller.GetBestStoriesAsync(pageSize: 10, page: 2)).Value!.OrderBy(x => x.Score);

        // Assert
        Assert.That(result.Count, Is.EqualTo(10));
        Assert.That(result.First().Title, Is.EqualTo("Story 11"));
        Assert.That(result.Last().Title, Is.EqualTo("Story 20"));
    }

    [Test]
    public async Task BestStories_Returns_BadRequest_For_Negative_Page_Or_PageSize()
    {
        // Arrange
        var apiSettings = CreateApiSettings(4);
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var httpClient = new HttpClient(new Mock<HttpMessageHandler>().Object);
        var controller = new BestStoriesController(memoryCache, new Mock<ILogger<BestStoriesController>>().Object, httpClient, apiSettings);

        // Act & Assert
        // Negative page size
        var negativePageSizeResult = await controller.GetBestStoriesAsync(pageSize: -10, page: 1);
        Assert.That(negativePageSizeResult.Result, Is.InstanceOf<BadRequestObjectResult>());

        // Negative page number
        var negativePageNumberResult = await controller.GetBestStoriesAsync(pageSize: 10, page: -1);
        Assert.That(negativePageNumberResult.Result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task BestStories_Returns_Empty_List_If_Page_Out_Of_Bounds()
    {
        // Arrange
        var apiSettings = CreateApiSettings(4);
        var memoryCache = new MemoryCache(new MemoryCacheOptions());

        var storyIds = Enumerable.Range(1, 15).ToList(); // Only 15 stories
        memoryCache.Set("best-stories", storyIds);

        foreach (var id in storyIds)
        {
            memoryCache.Set(id, new StoryDetail { Title = $"Story {id}", Score = id });
        }

        var httpClient = new HttpClient(new Mock<HttpMessageHandler>().Object);
        var controller = new BestStoriesController(memoryCache, new Mock<ILogger<BestStoriesController>>().Object, httpClient, apiSettings);

        // Act
        var result = await controller.GetBestStoriesAsync(pageSize: 10, page: 3);

        // Assert
        Assert.That(result.Value, Is.Empty); // Page 3 should be empty as there are only 15 stories
    }

    private static bool IsStoryDetailUrl(string url)
    {
        return Regex.IsMatch(url, @"https://hacker-news.firebaseio.com/v0/item/\d+\.json");
    }
}
