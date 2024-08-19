# Santander Code Test

## Overview
This project is a test implementation of a web API controller (BestStoriesController) that fetches and presents data from the Hacker News API. It retrieves the best stories, along with their details like title, URL, score, and more.

## Usage
Clone the repository:
```bash
https://github.com/marcusvfcarvalho/SantanderCodeTest.git
```
Open the solution in your preferred IDE (e.g., Visual Studio, Visual Studio Code).
Build and run the project.

## Configuration
The cache expiration time is set to 1 minute by default (ExpirationInMinutes constant). You can adjust this value in the BestStoriesController class.

## Features
Retrieves the best stories from Hacker News.
Caches the fetched data for a configurable duration to improve performance.
Fetches individual story details asynchronously.
Converts Unix timestamps to human-readable dates.

## Background

In software architecture, it's generally not considered an ideal practice for a REST API to directly call another third-party API. This approach can introduce a variety of common problems, primarily centered around reliability, scalability, and maintainability. Firstly, relying on external APIs directly couples your application's functionality to the availability and performance of those external services. Any downtime or slowdowns in the third-party API can directly impact your application's performance and availability. Additionally, it can lead to cascading failures, where issues in the third-party service propagate to your own application, causing widespread disruption. Secondly, scalability becomes a concern as the usage of your API grows. Directly calling third-party APIs can create bottlenecks and limit your ability to scale horizontally to handle increased load.

In this test scenario, despite the recognized best practices advocating against direct REST API calls from within the controller, I opted for simplicity, understanding that this is a test environment with limited scope. However, I took measures to mitigate potential issues associated with this approach. Firstly, I implemented caching mechanisms to store the responses from the third-party API temporarily. By caching the data locally, subsequent requests for the same data can be served without needing to call the external API repeatedly, thus reducing reliance on its availability and improving performance. Secondly, I introduced fallback procedures to handle situations where the third-party API is unavailable or encounters errors. These fallback procedures allow the application to gracefully degrade its functionality, such as by returning cached data or providing alternative responses, ensuring that the application remains operational even in adverse conditions. While this approach may not adhere strictly to recommended architectural patterns, it strikes a balance between simplicity and mitigating potential risks associated with direct API calls, making it suitable for the context of this test.

## Ideal Solution

In an ideal scenario, when able to have different services available, I would opt for a more robust and scalable approach to handle interactions with the third-party API, such as Hacker News. Instead of directly calling the API from the controller, I would leverage a distributed cache solution like Redis to store and manage the retrieved data efficiently. By utilizing Redis or a similar caching system, we can offload the responsibility of storing and serving frequently accessed data from the controller, reducing latency and improving overall system performance.

Additionally, I would implement a background process or task to continuously pull data from the Hacker News API at regular intervals. This background process would run independently of the main application logic, ensuring that our application remains responsive and resilient even during high traffic or when the third-party API experiences downtime or slowdowns.

By decoupling the data retrieval process from the controller and introducing a distributed cache solution along with a background data fetching mechanism, we can achieve a more scalable, reliable, and maintainable architecture. This approach not only improves the performance of our application but also enhances its fault tolerance and adaptability to changes in the external API.

## Performace
Performance tests were conducted utilizing the Postman Runner feature, employing 20, 40, 80, and 100 virtual users. The average response time remained consistent, with outliers such as initial cache filling excluded from the analysis.
| Virtual Users | Total Requests | Requests/s | Resp. Time (Avg. ms) | Min | Max | 90th(ms) | Error % |
|---------------|----------------|------------|----------------------|-----|-----|----------|---------|
| 20            | 2119           | 15.90      | 5                    | 2   | 311 | 5        | 0.0     |
| 40            | 4241           | 31.82      | 6                    | 2   | 402 | 5        | 0.0     |
| 80            | 8283           | 61.51      | 6                    | 2   | 506 | 5        | 0.0     |
| 100           | 10098          | 75.76      | 6                    | 2   | 422 | 6        | 0.0     |

## Resilience
The solution was designed to be resilient. It should never fail if the Hacker News API becomes inaccessible. Even if an issue arises between the calls to the "best stories" endpoint and the "details" endpoint, the API should still provide a response using the most recent known data. If the local cache for details doesn't contain the ID of a story found in the "best stories" cache, it should gracefully handle the situation by skipping the missing detail. In this hypothetical scenario, once Hacker News is back online, subsequent requests will fetch up-to-date data.
