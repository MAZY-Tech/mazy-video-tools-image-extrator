namespace ImageExtractor.Application.Interfaces;

public interface IZipService
{
    Task<string> CreateZipAsync(string sourceDirectory, string zipPath);
    Task UploadZipAsync(string bucket, string key, string zipPath);
}
