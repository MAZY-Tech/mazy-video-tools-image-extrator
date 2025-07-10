namespace ImageExtractor.Application.Interfaces;

public interface IVideoStorage
{
    Task<string> DownloadVideoAsync(string bucket, string key, string outputPath, IAppLogger logger);
    Task UploadFramesAsync(string bucket, string prefix, string[] framePaths, IAppLogger logger);
    Task DownloadAllFramesAsync(string bucket, string prefix, string destinationDir, IAppLogger logger);
}
