using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace ImageExtractor.Tests;

public class JobStateDocumentTests
{
    [Fact]
    public void Serialization_RoundTrip_ShouldPreserveAllProperties()
    {
        var originalDocument = new JobStateDocument
        {
            JobId = "job-12345",
            Status = "InProgress",
            CurrentStep = "FrameExtraction",
            LastProcessedSecond = 120,
            CurrentBlock = 2,
            TotalBlocks = 5,
            ProcessedFrames = 240,
            TotalFrames = 600,
            Progress = 40,
            CreatedAt = new DateTime(2025, 7, 7, 10, 0, 0, DateTimeKind.Utc),
            StartedAt = new DateTime(2025, 7, 7, 10, 5, 0, DateTimeKind.Utc),
            CompletedAt = null,
            LastHeartbeat = new DateTime(2025, 7, 7, 10, 10, 0, DateTimeKind.Utc),
            Metadata = new Dictionary<string, object>
            {
                { "source_file", "video.mp4" },
                { "frame_rate", 29.97 },
                { "is_premium_user", true }
            }
        };

        var bsonDocument = originalDocument.ToBsonDocument();

        var deserializedDocument = BsonSerializer.Deserialize<JobStateDocument>(bsonDocument);

        Assert.Equal(originalDocument.JobId, deserializedDocument.JobId);
        Assert.Equal(originalDocument.Status, deserializedDocument.Status);
        Assert.Equal(originalDocument.CurrentStep, deserializedDocument.CurrentStep);
        Assert.Equal(originalDocument.LastProcessedSecond, deserializedDocument.LastProcessedSecond);
        Assert.Equal(originalDocument.CurrentBlock, deserializedDocument.CurrentBlock);
        Assert.Equal(originalDocument.TotalBlocks, deserializedDocument.TotalBlocks);
        Assert.Equal(originalDocument.ProcessedFrames, deserializedDocument.ProcessedFrames);
        Assert.Equal(originalDocument.TotalFrames, deserializedDocument.TotalFrames);
        Assert.Equal(originalDocument.Progress, deserializedDocument.Progress);
        Assert.Equal(originalDocument.CreatedAt, deserializedDocument.CreatedAt);
        Assert.Equal(originalDocument.StartedAt, deserializedDocument.StartedAt);
        Assert.Equal(originalDocument.CompletedAt, deserializedDocument.CompletedAt);
        Assert.Equal(originalDocument.LastHeartbeat, deserializedDocument.LastHeartbeat);

        Assert.Equal(originalDocument.Metadata.Count, deserializedDocument.Metadata.Count);
        Assert.Equal(originalDocument.Metadata["source_file"], deserializedDocument.Metadata["source_file"]);
        Assert.Equal(originalDocument.Metadata["frame_rate"], deserializedDocument.Metadata["frame_rate"]);
        Assert.Equal(originalDocument.Metadata["is_premium_user"], deserializedDocument.Metadata["is_premium_user"]);
    }
}