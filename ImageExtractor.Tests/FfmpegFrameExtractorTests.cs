using ImageExtractor.Domain;
using ImageExtractor.Infrastructure.VideoProcessing;
using System.Runtime.InteropServices;

namespace ImageExtractor.Tests;

public class FfmpegFrameExtractorTests
{
    [Fact]
    public async Task ExtractFramesAsync_WhenSuccessful_InvokesProgressCallbackCorrectly()
    {
        var fakeFfmpegPath = CreateFakeFfmpegScript(shouldFail: false);
        var outputDir = Path.Combine(Path.GetTempPath(), "ffmpeg_test_output_" + Guid.NewGuid());
        var progressCalls = new List<(int current, int total)>();

        var metadata = new VideoMetadata { DurationSeconds = 25 };
        var extractor = new FfmpegFrameExtractor(fakeFfmpegPath);

        try
        {
            await extractor.ExtractFramesAsync(
                "/path/to/video.mp4",
                outputDir,
                metadata,
                frameRate: 1,
                blockSeconds: 10,
                onProgress: (current, total) => progressCalls.Add((current, total))
            );

            Assert.Equal(3, progressCalls.Count);
            Assert.Equal((1, 3), progressCalls[0]);
            Assert.Equal((2, 3), progressCalls[1]);
            Assert.Equal((3, 3), progressCalls[2]);
        }
        finally
        {
            if (File.Exists(fakeFfmpegPath)) File.Delete(fakeFfmpegPath);
            if (Directory.Exists(outputDir)) Directory.Delete(outputDir, true);
        }
    }

    [Fact]
    public async Task ExtractFramesAsync_WhenProcessFails_ThrowsExceptionWithError()
    {
        var errorMessage = "Invalid video stream";
        var fakeFfmpegPath = CreateFakeFfmpegScript(shouldFail: true, errorMessage: errorMessage);
        var outputDir = Path.Combine(Path.GetTempPath(), "ffmpeg_test_output_" + Guid.NewGuid());

        var metadata = new VideoMetadata { DurationSeconds = 25 };
        var extractor = new FfmpegFrameExtractor(fakeFfmpegPath);

        try
        {
            var exception = await Assert.ThrowsAsync<Exception>(() => extractor.ExtractFramesAsync(
                "/path/to/video.mp4",
                outputDir,
                metadata,
                frameRate: 1,
                blockSeconds: 10
            ));

            Assert.Contains(errorMessage, exception.Message);
        }
        finally
        {
            if (File.Exists(fakeFfmpegPath)) File.Delete(fakeFfmpegPath);
            if (Directory.Exists(outputDir)) Directory.Delete(outputDir, true);
        }
    }

    private string CreateFakeFfmpegScript(bool shouldFail, string errorMessage = "ffmpeg error")
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), "fake-ffmpeg" + (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".bat" : ""));

        string scriptContent;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            if (shouldFail)
            {
                scriptContent = $"@echo off\r\necho {errorMessage} >&2\r\nexit /b 1";
            }
            else
            {
                scriptContent = "@echo off\r\nexit /b 0";
            }
        }
        else
        {
            if (shouldFail)
            {
                scriptContent = $"#!/bin/sh\necho \"{errorMessage}\" 1>&2\nexit 1";
            }
            else
            {
                scriptContent = "#!/bin/sh\nexit 0";
            }
        }

        File.WriteAllText(scriptPath, scriptContent);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            File.SetUnixFileMode(scriptPath, UnixFileMode.UserExecute | UnixFileMode.UserRead);
        }

        return scriptPath;
    }
}