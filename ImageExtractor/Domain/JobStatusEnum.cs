namespace ImageExtractor.Domain;

public enum JobStatusEnum
{
    Pending,
    Running,
    Completed,
    Failed,
    Canceled,
    Interrupted
}