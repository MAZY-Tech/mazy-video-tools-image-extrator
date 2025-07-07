using ImageExtractor.Application.Interfaces;
using ImageExtractor.Domain;
using System.Diagnostics;

namespace ImageExtractor.Infrastructure.VideoProcessing;

public class FfmpegFrameExtractor(string ffmpegPath) : IFrameExtractor
{
    public async Task ExtractFramesAsync(string videoPath, string outputDir, VideoMetadata metadata, int frameRate, int blockSeconds, Action<int, int>? onProgress = null)
    {
        if (!Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);

        var totalDurationInSeconds = metadata.DurationSeconds;
        var totalBlocks = (int)Math.Ceiling(totalDurationInSeconds / blockSeconds);

        for (int i = 0; i < totalBlocks; i++)
        {
            var startTime = TimeSpan.FromSeconds(i * blockSeconds);
            var outputPattern = Path.Combine(outputDir, $"frame_{i:D4}_%03d.jpg");

            var args = $"-ss {startTime} -i \"{videoPath}\" -t {blockSeconds} -vf fps={frameRate} \"{outputPattern}\" -hide_banner -loglevel error";
            var processInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(processInfo);
            if (process == null) throw new InvalidOperationException("Failed to start ffmpeg process.");

            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync();
                throw new Exception($"ffmpeg error (block {i}): {error}");
            }

            onProgress?.Invoke(i + 1, totalBlocks);
        }
    }
}