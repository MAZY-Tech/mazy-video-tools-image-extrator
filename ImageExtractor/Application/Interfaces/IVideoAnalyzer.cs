using ImageExtractor.Domain;

namespace ImageExtractor.Application.Interfaces;

public interface IVideoAnalyzer
{
    Task<VideoMetadata> AnalyzeAsync(string videoPath);
}
