namespace ImageExtractor.Domain;

public enum JobStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Canceled,
    Interrupted
}