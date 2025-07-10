using ImageExtractor.Application.Interfaces;
using System.Diagnostics;

namespace ImageExtractor.Infrastructure.VideoProcessing;

public class FfmpegFrameExtractor(string ffmpegPath) : IFrameExtractor
{
    public async Task ExtractFramesAsync(string videoPath, string outputDir, int frameRate, TimeSpan startTime, int duration, int blockIndex, IAppLogger logger)
    {
        if (!Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);

        var outputPattern = Path.Combine(outputDir, $"block{blockIndex:D4}_frame%04d.jpg");

        var args = $"-threads 0 -hwaccel auto -ss {startTime} -i \"{videoPath}\" -t {duration} -vf fps={frameRate} -q:v 3 -f image2 -y \"{outputPattern}\" -hide_banner -loglevel error";

        logger.Log($"[FfmpegFrameExtractor] Executing command: '{ffmpegPath} {args}'");

        var processInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(processInfo) ?? throw new InvalidOperationException("Failed to start ffmpeg process.");

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync();
            logger.Log($"[FfmpegFrameExtractor] [ERROR] ffmpeg process exited with code {process.ExitCode}. Error: {error}");
            throw new InvalidOperationException($"ffmpeg error: {error}");
        }
    }
}
