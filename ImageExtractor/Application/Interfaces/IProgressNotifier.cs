namespace ImageExtractor.Application.Interfaces;

public interface IProgressNotifier
{
    Task NotifyProgressAsync(string videoId, int progress, int currentBlock, int totalBlocks, IAppLogger logger);
}