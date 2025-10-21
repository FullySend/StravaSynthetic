namespace Faker.Models;

public record ActivityStreams
{
    public long ActivityId { get; init; }
    public List<int> TimeSeconds { get; init; } = new();
    public List<int> HeartRate   { get; init; } = new();
    public int ThresholdBpm { get; init; } = 100;
    public int SecondsAboveThreshold { get; init; }
}
