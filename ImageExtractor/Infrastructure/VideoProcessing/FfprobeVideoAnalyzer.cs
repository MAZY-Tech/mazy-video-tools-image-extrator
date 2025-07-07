using ImageExtractor.Application.Interfaces;
using ImageExtractor.Domain;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace ImageExtractor.Infrastructure.VideoProcessing;

public class FfprobeVideoAnalyzer(string ffprobePath) : IVideoAnalyzer
{
    public async Task<VideoMetadata> AnalyzeAsync(string videoPath)
    {
        var args = $"-v quiet -print_format json -show_format -show_streams \"{videoPath}\"";

        var psi = new ProcessStartInfo
        {
            FileName = ffprobePath,
            Arguments = args,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
            throw new InvalidOperationException("Failed to start ffprobe.");

        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        using var jsonDoc = JsonDocument.Parse(output);

        var root = jsonDoc.RootElement;

        double duration = double.Parse(root.GetProperty("format").GetProperty("duration").GetString()!,
                                       CultureInfo.InvariantCulture);

        var videoStream = root.GetProperty("streams")
            .EnumerateArray()
            .FirstOrDefault(s => s.GetProperty("codec_type").GetString() == "video");

        int nbFrames = 0;
        if (videoStream.TryGetProperty("nb_frames", out var nbFramesProp))
        {
            _ = int.TryParse(nbFramesProp.GetString(), out nbFrames);
        }

        return new VideoMetadata
        {
            DurationSeconds = duration,
            FrameCount = nbFrames
        };
    }
}
