using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

public class JobStateDocument
{
    [BsonId]
    public string JobId { get; set; } = default!;

    [BsonRepresentation(BsonType.String)]
    public string Status { get; set; } = default!;

    [BsonRepresentation(BsonType.String)]
    public string CurrentStep { get; set; } = default!;

    public int LastProcessedSecond { get; set; }

    public int CurrentBlock { get; set; }

    public int TotalBlocks { get; set; }

    public int ProcessedFrames { get; set; }

    public int TotalFrames { get; set; }

    public int Progress { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public DateTime LastHeartbeat { get; set; }

    public Dictionary<string, object> Metadata { get; set; } = [];
}