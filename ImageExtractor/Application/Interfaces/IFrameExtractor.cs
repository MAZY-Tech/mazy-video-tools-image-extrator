using ImageExtractor.Domain;

namespace ImageExtractor.Application.Interfaces
{
    public interface IFrameExtractor
    {
        Task ExtractFramesAsync(string videoPath, string outputDir, VideoMetadata metadata, int frameRate, int blockSeconds, Action<int, int>? onProgress = null);
    }
}