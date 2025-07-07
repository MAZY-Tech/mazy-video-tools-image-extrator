using ImageExtractor.Application.Interfaces;

namespace ImageExtractor.Infrastructure.Config;

public class EnvironmentValidator : IEnvironmentValidator
{
    public void Validate()
    {
        var temp = Environment.GetEnvironmentVariable("TEMP_FOLDER") ?? "/tmp";
        if (!Directory.Exists(temp))
        {
            throw new InvalidOperationException($"Temporary folder does not exist: {temp}");
        }

        if (!IsExecutableAvailable("/opt/bin/ffmpeg"))
        {
            throw new InvalidOperationException("ffmpeg not found at /opt/bin/ffmpeg");
        }

        if (!IsExecutableAvailable("/opt/bin/ffprobe"))
        {
            throw new InvalidOperationException("ffprobe not found at /opt/bin/ffprobe");
        }
    }

    private static bool IsExecutableAvailable(string path)
    {
        return File.Exists(path) && new FileInfo(path).Length > 0;
    }
}
