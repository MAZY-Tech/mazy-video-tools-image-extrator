namespace ImageExtractor.Domain;

public class JobState
{
    public string JobId { get; set; } = default!;
    public JobStatus Status { get; set; }
    public ProcessingStep CurrentStep { get; set; }

    public int LastProcessedSecond { get; set; }
    public int CurrentBlock { get; set; }
    public int TotalBlocks { get; set; }
    public int ProcessedFrames { get; set; }
    public int TotalFrames { get; set; }
    public int Progress { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime LastHeartbeat { get; set; }

    public Dictionary<string, object> Metadata { get; set; } = [];
}