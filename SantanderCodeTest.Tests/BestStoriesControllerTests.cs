using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Any;
using Moq;
using Moq.Protected;
using NUnit.Framework;
using NUnit.Framework.Internal;
using SantanderCodeTest.Controllers;
using SantanderCodeTest.DTO;

namespace SantanderCodeTest.Tests
{
    [TestFixture]
    public class BestStoriesControllerTests
    {

        [Test]
        public async Task BestStories_Returns_Cache_If_Http_If_Hackers_API_Fails_During_The_Call()
        {
            // Arrange
            var cache = new MemoryCache(new MemoryCacheOptions());

            cache.Set(1, new StoryDetail()
            {
                Title = "1",
                Time = new DateTime(2024, 12, 31)

            });
            cache.Set(2, new StoryDetail
            {
                Title = "2",
                Time = new DateTime(2024, 12, 30)
            });

            cache.Set(3, new StoryDetail
            {
                Title = "3",
                Time = new DateTime(2024, 12, 29)
            });


            var loggerMock = new Mock<ILogger<BestStoriesController>>();

            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            handlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
               .ReturnsAsync((HttpRequestMessage request, CancellationToken cancellationToken) =>
               {
                   if (request!.RequestUri!.AbsoluteUri == "https://hacker-news.firebaseio.com/v0/beststories.json")
                   {

                       return new HttpResponseMessage
                       {
                           StatusCode = HttpStatusCode.OK,
                           Content = new StringContent(@"[1, 2, 3, 4, 5]")
                       };
                   }
                   else if (IsDetailUrl(request.RequestUri.AbsoluteUri))
                   {

                       throw new HttpRequestException();
                   }

                   return new HttpResponseMessage
                   {
                       StatusCode = HttpStatusCode.InternalServerError
                   }; 
                  
               });


            var httpClient = new HttpClient(handlerMock.Object);


            var controller = new BestStoriesController(cache, loggerMock.Object, httpClient);

            // Act
            var result = await controller.BestStoriesAsync();
            Console.WriteLine(result);

            // Assert
            // 4 elements in cache: 1 list of BestStories, 1 for backup, 3 for Details
            Assert.That(cache.Count, Is.EqualTo(5));
        }

        [Test]
        public async Task BestStories_Returns_Cache_If_Http_If_Hackers_API_Is_Down()
        {
            var cache = new MemoryCache(new MemoryCacheOptions());

            cache.Set("best-stories-backup", new List<int>() { 1, 2, 3 });


            cache.Set(1, new StoryDetail()
            {
                Title = "1",
                Time = new DateTime(2024, 12, 31)

            }) ;
            cache.Set(2, new StoryDetail
            {
                Title = "2",
                 Time = new DateTime(2024, 12, 30)
            });

            cache.Set(3, new StoryDetail
            {
                Title = "3",
                Time = new DateTime(2024, 12, 29)
            });

            var loggerMock = new Mock<ILogger<BestStoriesController>>();

            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            handlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
               .ReturnsAsync((HttpRequestMessage request, CancellationToken cancellationToken) =>
               {
                   throw new HttpRequestException();
               });

            var httpClient = new HttpClient(handlerMock.Object);


            var controller = new BestStoriesController(cache, loggerMock.Object, httpClient);

            var result = await controller.BestStoriesAsync();

            Assert.That(result.Value!.Count, Is.EqualTo(3));
            Assert.That(result.Value!.First().Title, Is.EqualTo("1"));

        }
        [Test]
        public async Task BestStories_Returns_Cached_Stories_If_Available()
        {
            var cache = new MemoryCache(new MemoryCacheOptions());

            cache.Set("best-stories", new List<int>() { 1, 2, 3 });

            cache.Set(1, new StoryDetail() {
                Title = "1"
            });
            cache.Set(2, new StoryDetail { 
                Title = "2" 
            });

            cache.Set(3, new StoryDetail { 
                Title = "3" 
            });

            var loggerMock = new Mock<ILogger<BestStoriesController>>();

            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            handlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
               .ReturnsAsync((HttpRequestMessage request, CancellationToken cancellationToken) =>
               {
                   if (request!.RequestUri!.AbsoluteUri == "https://hacker-news.firebaseio.com/v0/beststories.json")
                   {

                       return new HttpResponseMessage
                       {
                           StatusCode = HttpStatusCode.OK,
                           Content = new StringContent(@"[1, 2, 3]")
                       };
                   }
                   else if (IsDetailUrl(request.RequestUri.AbsoluteUri))
                   {

                       return new HttpResponseMessage
                       {
                           StatusCode = HttpStatusCode.OK,
                           Content = new StringContent(@"
                                                {
                                                    ""title"": ""A uBlock Origin update was rejected from the Chrome Web Store"",
                                                    ""uri"": ""https://github.com/uBlockOrigin/uBlock-issues/issues/745"",
                                                    ""postedBy"": ""ismaildonmez"",
                                                    ""time"": 1570887781,
                                                    ""score"": 1716,
                                                    ""commentCount"": 572
                                                }")
                       };
                   }
                   else
                   {
                       // Handle other cases or throw an exception
                       throw new NotImplementedException($"Unhandled request URI: {request.RequestUri}");
                   }
               });


            var httpClient = new HttpClient(handlerMock.Object);


            var controller = new BestStoriesController(cache, loggerMock.Object, httpClient);

            // Act
            var result = await controller.BestStoriesAsync();
            Console.WriteLine(result);

            // Assert
            Assert.Multiple(() =>
            {
                // 4 elements in cache: 1 list of BestStories, 3 for Details
                Assert.That(cache.Count, Is.EqualTo(4));
                Assert.That(((StoryDetail)cache!.Get(1)!).Title, Is.EqualTo("1"));
                Assert.That(((StoryDetail)cache!.Get(2)!).Title, Is.EqualTo("2"));
                Assert.That(((StoryDetail)cache!.Get(3)!).Title, Is.EqualTo("3"));
            });
        }


        [Test]
        public async Task BestStories_Makes_API_Call_If_Stories_Not_Cached()
        {
            // Arrange
            var cache = new MemoryCache(new MemoryCacheOptions());

            var loggerMock = new Mock<ILogger<BestStoriesController>>();

            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            handlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
               .ReturnsAsync((HttpRequestMessage request, CancellationToken cancellationToken) =>
               {
                   if (request!.RequestUri!.AbsoluteUri == "https://hacker-news.firebaseio.com/v0/beststories.json")
                   {

                       return new HttpResponseMessage
                       {
                           StatusCode = HttpStatusCode.OK,
                           Content = new StringContent(@"[1, 2, 3]")
                       };
                   }
                   else if (IsDetailUrl(request.RequestUri.AbsoluteUri))
                   {

                       return new HttpResponseMessage
                       {
                           StatusCode = HttpStatusCode.OK,
                           Content = new StringContent(@"
                                                {
                                                    ""title"": ""A uBlock Origin update was rejected from the Chrome Web Store"",
                                                    ""uri"": ""https://github.com/uBlockOrigin/uBlock-issues/issues/745"",
                                                    ""postedBy"": ""ismaildonmez"",
                                                    ""time"": 1570887781,
                                                    ""score"": 1716,
                                                    ""commentCount"": 572
                                                }")
                       };
                   }
                   else
                   {
                       // Handle other cases or throw an exception
                       throw new NotImplementedException($"Unhandled request URI: {request.RequestUri}");
                   }
               });


            var httpClient = new HttpClient(handlerMock.Object);


            var controller = new BestStoriesController(cache, loggerMock.Object, httpClient);

            // Act
            var result = await controller.BestStoriesAsync();
            Console.WriteLine(result);

            // Assert
            // 4 elements in cache: 1 list of BestStories, 1 for backup, 3 for Details
            Assert.That(cache.Count, Is.EqualTo(5));
        }

        private static bool IsDetailUrl(string absoluteUri)
        {
            string urlPattern = @"^https:\/\/hacker-news\.firebaseio\.com\/v0\/item\/\d+\.json$";
            return Regex.IsMatch(absoluteUri, urlPattern);
        }

    }
}
