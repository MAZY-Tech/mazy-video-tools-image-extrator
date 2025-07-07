namespace ImageExtractor.Application.Interfaces;

public interface ICompletionNotifier
{
    Task NotifyCompletionAsync(string videoId, string zipBucket, string zipKey);
}