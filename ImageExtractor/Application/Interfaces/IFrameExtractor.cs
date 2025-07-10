using ImageExtractor.Domain;

namespace ImageExtractor.Application.Interfaces
{
    public interface IFrameExtractor
    {
        Task ExtractFramesAsync(string videoPath, string outputDir, int frameRate, TimeSpan startTime, int duration, int blockIndex, IAppLogger logger);
    }
}