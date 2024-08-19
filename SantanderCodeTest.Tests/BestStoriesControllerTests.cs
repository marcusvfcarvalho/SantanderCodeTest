using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using SantanderCodeTest.Controllers;
using SantanderCodeTest.DTO;
using System.Net;
using System.Text.RegularExpressions;

namespace SantanderCodeTest.Tests
{
    [TestFixture]
    public class BestStoriesControllerTests
    {
        // Test: Verifies that the cache is returned when the Hacker News API fails during a request.
        [Test]
        public async Task BestStories_Returns_Cache_If_HackerNews_API_Fails_During_Call()
        {
            // Arrange
            var memoryCache = new MemoryCache(new MemoryCacheOptions());

            // Setting up stories in cache
            memoryCache.Set(1, new StoryDetail { Title = "Story 1", Time = new DateTime(2024, 12, 31) });
            memoryCache.Set(2, new StoryDetail { Title = "Story 2", Time = new DateTime(2024, 12, 30) });
            memoryCache.Set(3, new StoryDetail { Title = "Story 3", Time = new DateTime(2024, 12, 29) });

            var loggerMock = new Mock<ILogger<BestStoriesController>>();

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
                            Content = new StringContent(@"[1, 2, 3, 4, 5]") // Simulate story ID response
                        };
                    }
                    else if (IsStoryDetailUrl(request.RequestUri.AbsoluteUri))
                    {
                        throw new HttpRequestException(); // Simulate API failure for story details
                    }

                    return new HttpResponseMessage(HttpStatusCode.InternalServerError);
                });

            var httpClient = new HttpClient(httpMessageHandlerMock.Object);
            var controller = new BestStoriesController(memoryCache, loggerMock.Object, httpClient);

            // Act
            var result = await controller.GetBestStoriesAsync();

            // Assert
            // 5 items in cache: 1 for BestStories list, 1 for backup, 3 individual stories
            Assert.That(memoryCache.Count, Is.EqualTo(5));
        }

        // Test: Verifies that the cache is used when the Hacker News API is completely down.
        [Test]
        public async Task BestStories_Returns_Cache_If_HackerNews_API_Is_Down()
        {
            // Arrange
            var memoryCache = new MemoryCache(new MemoryCacheOptions());

            // Setup backup cache for best stories and individual story details
            memoryCache.Set("best-stories-backup", new List<int> { 1, 2, 3 });
            memoryCache.Set(1, new StoryDetail { Title = "Story 1", Time = new DateTime(2024, 12, 31) });
            memoryCache.Set(2, new StoryDetail { Title = "Story 2", Time = new DateTime(2024, 12, 30) });
            memoryCache.Set(3, new StoryDetail { Title = "Story 3", Time = new DateTime(2024, 12, 29) });

            var loggerMock = new Mock<ILogger<BestStoriesController>>();

            var httpMessageHandlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            httpMessageHandlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                ).ThrowsAsync(new HttpRequestException());

            var httpClient = new HttpClient(httpMessageHandlerMock.Object);
            var controller = new BestStoriesController(memoryCache, loggerMock.Object, httpClient);

            // Act
            var result = await controller.GetBestStoriesAsync();

            // Assert: Verify the cache returns 3 cached stories
            Assert.That(result.Value!.Count, Is.EqualTo(3));
            Assert.That(result.Value!.First().Title, Is.EqualTo("Story 1"));
        }

        // Test: Verifies that cached stories are returned if available in cache.
        [Test]
        public async Task BestStories_Returns_Cached_Stories_If_Available()
        {
            // Arrange
            var memoryCache = new MemoryCache(new MemoryCacheOptions());

            // Setup cache with story IDs and their details
            memoryCache.Set("best-stories", new List<int> { 1, 2, 3 });
            memoryCache.Set(1, new StoryDetail { Title = "Story 1" });
            memoryCache.Set(2, new StoryDetail { Title = "Story 2" });
            memoryCache.Set(3, new StoryDetail { Title = "Story 3" });

            var loggerMock = new Mock<ILogger<BestStoriesController>>();

            var httpMessageHandlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            httpMessageHandlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

            var httpClient = new HttpClient(httpMessageHandlerMock.Object);
            var controller = new BestStoriesController(memoryCache, loggerMock.Object, httpClient);

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

        // Test: Verifies that API is called when stories are not cached.
        [Test]
        public async Task BestStories_Makes_API_Call_If_Stories_Not_Cached()
        {
            // Arrange
            var memoryCache = new MemoryCache(new MemoryCacheOptions());

            var loggerMock = new Mock<ILogger<BestStoriesController>>();

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
            var controller = new BestStoriesController(memoryCache, loggerMock.Object, httpClient);

            // Act
            var result = await controller.GetBestStoriesAsync();

            // Assert: Verify that cache is populated after API call
            Assert.That(memoryCache.Count, Is.EqualTo(5)); // 1 list, 1 backup, 3 stories
        }

        // Helper method to identify story detail URLs
        private static bool IsStoryDetailUrl(string absoluteUri)
        {
            string urlPattern = @"^https:\/\/hacker-news\.firebaseio\.com\/v0\/item\/\d+\.json$";
            return Regex.IsMatch(absoluteUri, urlPattern);
        }

        [Test]
        public async Task BestStories_Returns_Empty_List_If_API_Is_Down_And_Cache_Is_Empty()
        {
            // Arrange
            var memoryCache = new MemoryCache(new MemoryCacheOptions());

            // Logger mock
            var loggerMock = new Mock<ILogger<BestStoriesController>>();

            // HttpMessageHandler mock to simulate API being down
            var httpMessageHandlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            httpMessageHandlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ThrowsAsync(new HttpRequestException());

            var httpClient = new HttpClient(httpMessageHandlerMock.Object);
            var controller = new BestStoriesController(memoryCache, loggerMock.Object, httpClient);

            // Act
            var result = await controller.GetBestStoriesAsync();

            // Assert
            Assert.Multiple(() =>
            {        
                Assert.That(result.Value, Is.Empty); // Ensure that the result is an empty list
                Assert.That(memoryCache.Count, Is.EqualTo(0)); // Ensure that no items are cached
            });
        }
    }
}
