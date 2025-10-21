using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Bogus;
using Faker.Models;

namespace StravaSynthetic
{
     

    public static class SyntheticGenerator
    {
        // Compute seconds above threshold similar to your StravaClient method
        public static int ComputeSecondsAbove(IList<int> times, IList<int> hr, int threshold)
        {
            if (times == null || hr == null) return 0;
            var count = 0;
            var n = Math.Min(times.Count, hr.Count);
            for (int i = 0; i < n; i++)
            {
                if (hr[i] > threshold) count++;
            }
            return count;
        }

        // Generate HR stream (list of ints) with basic warmup/intervals/variability
        public static (List<int> times, List<int> hr) GenerateStreams(
            int durationSeconds,
            int samplingSeconds = 1,
            int restingBpm = 55,
            int maxBpm = 180,
            int seed = 0,
            double intervalIntensity = 0.15,    // fraction of time spent in high-intensity bursts
            double dropoutProbability = 0.0     // fraction of samples to drop (simulate missing stream)
            )
        {
            var rand = new Random(seed);
            var times = new List<int>(durationSeconds / samplingSeconds + 1);
            var hr = new List<int>(durationSeconds / samplingSeconds + 1);

            // Simple model: warmup ramp, steady-state with occasional intervals, cooldown
            for (int t = 0; t <= durationSeconds; t += samplingSeconds)
            {
                times.Add(t);

                // normalized progress 0..1
                double p = (double)t / Math.Max(1, durationSeconds);

                // baseline ramp up first 10% then ramp down last 10%
                double warmup = Math.Clamp(p / 0.10, 0, 1); // 0..1 in first 10%
                double cooldown = Math.Clamp((1 - p) / 0.10, 0, 1); // 0..1 in last 10%
                double baseFactor = Math.Min(warmup, cooldown);
                baseFactor = Math.Max(baseFactor, cooldown); // keep baseline at least cooldown/warmup

                // steady-state HR around 60-70% of max, with intervals
                double steadyBpm = restingBpm + (maxBpm - restingBpm) * (0.5 * baseFactor + 0.35 * (1 - baseFactor));

                // occasional interval bursts
                bool isInterval = rand.NextDouble() < intervalIntensity;
                double burst = isInterval ? (0.15 + rand.NextDouble() * 0.25) : (rand.NextDouble() * 0.05);

                // small random noise
                double noise = (rand.NextDouble() - 0.5) * 6.0; // ±3 bpm typical noise

                var bpm = (int)Math.Round(Math.Clamp(steadyBpm * (1 + burst) + noise, restingBpm, maxBpm));

                // simulate dropout
                if (rand.NextDouble() < dropoutProbability)
                {
                    // use zero to signal missing data 
                    hr.Add(0);
                }
                else
                {
                    hr.Add(bpm);
                }
            }

            return (times, hr);
        }

        // Bogus faker for the activity summary (no streams here)
        public static Faker<ActivitySummary> CreateActivityFaker(int seed = 0)
        {
            Randomizer.Seed = new Random(seed);
            return new Faker<ActivitySummary>()
                .StrictMode(true)
                .RuleFor(a => a.ActivityId, f => 0L) // we'll overwrite with our own positive ID
                .RuleFor(a => a.ActivityName, (f, a) => $"Synthetic {f.Hacker.Verb()} {f.Hacker.Noun()}")
                .RuleFor(a => a.StartLocal, f => f.Date.RecentOffset(days: 30).DateTime)
                .RuleFor(a => a.ElapsedTimeSec, f => f.Random.Int(600, 3 * 60 * 60))
                .RuleFor(a => a.MovingTimeSec, (f, a) => Math.Max(0, a.ElapsedTimeSec - f.Random.Int(0, (int)(a.ElapsedTimeSec * 0.1))))
                .RuleFor(a => a.HasHeartRate, f => true);
        }

        // Generate N activities for an athlete and write:
        //   activity_{id}.json       (summary)
        //   streams_{id}.json        (time + heartRate + metrics)
        public static void ProduceAndWrite(long athleteId, int count = 5, int seed = 12345, string outputRoot = "./synthetic")
        {
            var faker = CreateActivityFaker(seed);

            Directory.CreateDirectory(outputRoot);
            var athleteDir = Path.Combine(outputRoot, athleteId.ToString());
            Directory.CreateDirectory(athleteDir);

            const long stride = 1_000_000L;
            var baseId = athleteId * stride;

            for (int i = 0; i < count; i++)
            {
                var summary = faker.Generate();

                // ✅ Always-positive, predictable
                var activityId = baseId + (i + 1);
                summary = summary with { ActivityId = activityId };

                // Build streams deterministically from seed + activityId
                var perActSeed = HashCode.Combine(seed, (int)(activityId & 0x7FFFFFFF));
                var sampling = summary.ElapsedTimeSec > 2 * 60 * 60 ? 5 : 1; // 1s, or 5s for very long
                var (times, hr) = GenerateStreams(
                    durationSeconds: summary.ElapsedTimeSec,
                    samplingSeconds: sampling,
                    restingBpm: 50,
                    maxBpm: 185,
                    seed: perActSeed,
                    intervalIntensity: 0.12,
                    dropoutProbability: 0.01);

                int threshold = 100;
                int secondsAbove = ComputeSecondsAbove(times, hr, threshold);

                var streams = new ActivityStreams
                {
                    ActivityId = activityId,
                    TimeSeconds = times,
                    HeartRate = hr,
                    ThresholdBpm = threshold,
                    SecondsAboveThreshold = secondsAbove
                };

                var jsonOpts = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };

                var activityPath = Path.Combine(athleteDir, $"activity_{activityId}.json");
                var streamsPath  = Path.Combine(athleteDir, $"streams_{activityId}.json");

                File.WriteAllText(activityPath, JsonSerializer.Serialize(summary, jsonOpts));
                File.WriteAllText(streamsPath,  JsonSerializer.Serialize(streams,  jsonOpts));

                Console.WriteLine($"Wrote {activityPath}");
                Console.WriteLine($"Wrote {streamsPath} (samples={times.Count}, above100={secondsAbove})");
            }
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            // Example usage
            SyntheticGenerator.ProduceAndWrite(athleteId: 10000, count: 5, seed: 40, outputRoot: "./synthetic");
            SyntheticGenerator.ProduceAndWrite(athleteId: 10001, count: 5, seed: 41, outputRoot: "./synthetic");
            SyntheticGenerator.ProduceAndWrite(athleteId: 10002, count: 5, seed: 42, outputRoot: "./synthetic");
            Console.WriteLine("Done.");
        }
    }
}