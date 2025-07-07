namespace ImageExtractor.Application.Interfaces;

public interface IVideoStorage
{
    Task<string> DownloadVideoAsync(string bucket, string key, string outputPath);
    Task UploadFramesAsync(string bucket, string prefix, string[] framePaths);
}
