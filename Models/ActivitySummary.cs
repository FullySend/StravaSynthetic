namespace Faker.Models;

public record ActivitySummary
{
    public long ActivityId { get; init; }
    public string ActivityName { get; set; } = "";
    public DateTime StartLocal { get; init; }
    public int ElapsedTimeSec { get; init; }
    public int MovingTimeSec { get; init; }
    public bool HasHeartRate { get; init; }
}
