namespace SantanderCodeTest.DTO;

using System.Collections.Generic;

public class HackerNewsItem
{
    public string? By { get; set; }
    public int Descendants { get; set; }
    public int Id { get; set; }
    public List<int>? Kids { get; set; }
    public int Score { get; set; }
    public long? Time { get; set; }
    public string? Title { get; set; }
    public string? Type { get; set; }
    public string? Url { get; set; }
}
