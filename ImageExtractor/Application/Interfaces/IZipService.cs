namespace ImageExtractor.Application.Interfaces;

public interface IZipService
{
    Task<string> CreateZipAsync(string sourceDirectory, string zipPath, IAppLogger logger);
    Task UploadZipAsync(string bucket, string key, string zipPath, IAppLogger logger);
}
