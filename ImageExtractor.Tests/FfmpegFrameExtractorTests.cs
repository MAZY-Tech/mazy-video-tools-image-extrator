using Amazon.Lambda.Core;
using ImageExtractor.Infrastructure.Adapters;
using ImageExtractor.Infrastructure.VideoProcessing;
using Moq;
using System.Globalization;
using System.Runtime.InteropServices;

namespace ImageExtractor.Tests;

public class FfmpegFrameExtractorTests
{
    [Fact]
    public async Task ExtractFramesAsync_WhenSuccessful_ConstructsCorrectFfmpegCommand()
    {
        var appLogger = new LambdaContextLogger(new Mock<ILambdaLogger>().Object);
        var argsCapturePath = Path.GetTempFileName();
        var fakeFfmpegPath = CreateFakeFfmpegScript(shouldFail: false, argsCapturePath: argsCapturePath);
        var outputDir = Path.Combine(Path.GetTempPath(), "ffmpeg_test_output_" + Guid.NewGuid());

        var videoPath = "/path/to/video.mp4";
        var startTime = TimeSpan.FromSeconds(65);
        var duration = 10;
        var blockIndex = 5;
        var frameRate = 15;

        var extractor = new FfmpegFrameExtractor(fakeFfmpegPath);

        try
        {
            await extractor.ExtractFramesAsync(
                videoPath,
                outputDir,
                frameRate,
                startTime,
                duration,
                blockIndex,
                appLogger
            );

            var capturedArgs = await File.ReadAllTextAsync(argsCapturePath);

            Assert.Contains($"-ss {startTime.ToString("c", CultureInfo.InvariantCulture)}", capturedArgs);
            Assert.Contains($"-t {duration}", capturedArgs);
            Assert.Contains($"-vf fps={frameRate}", capturedArgs);
            Assert.Contains(videoPath, capturedArgs);
            Assert.Contains($"block{blockIndex:D4}_frame%04d.jpg", capturedArgs);
        }
        finally
        {
            if (File.Exists(fakeFfmpegPath)) File.Delete(fakeFfmpegPath);
            if (File.Exists(argsCapturePath)) File.Delete(argsCapturePath);
            if (Directory.Exists(outputDir)) Directory.Delete(outputDir, true);
        }
    }

    [Fact]
    public async Task ExtractFramesAsync_WhenProcessFails_ThrowsExceptionWithError()
    {
        var appLogger = new LambdaContextLogger(new Mock<ILambdaLogger>().Object);
        var errorMessage = "Invalid video stream";
        var fakeFfmpegPath = CreateFakeFfmpegScript(shouldFail: true, errorMessage: errorMessage);
        var outputDir = Path.Combine(Path.GetTempPath(), "ffmpeg_test_output_" + Guid.NewGuid());
        var extractor = new FfmpegFrameExtractor(fakeFfmpegPath);

        try
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => extractor.ExtractFramesAsync(
                "/path/to/video.mp4",
                outputDir,
                frameRate: 1,
                startTime: TimeSpan.Zero,
                duration: 10,
                blockIndex: 0,
                logger: appLogger
            ));

            Assert.Contains(errorMessage, exception.Message);
        }
        finally
        {
            if (File.Exists(fakeFfmpegPath)) File.Delete(fakeFfmpegPath);
            if (Directory.Exists(outputDir)) Directory.Delete(outputDir, true);
        }
    }

    private string CreateFakeFfmpegScript(bool shouldFail, string errorMessage = "ffmpeg error", string? argsCapturePath = null)
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), "fake-ffmpeg" + (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".bat" : ""));
        var captureCommand = string.Empty;

        if (!string.IsNullOrEmpty(argsCapturePath))
        {
            captureCommand = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? $"echo %* > \"{argsCapturePath}\""
                : $"echo \"$@\" > \"{argsCapturePath}\"";
        }

        string scriptContent;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            scriptContent = shouldFail
                ? $"@echo off\r\n{captureCommand}\r\necho {errorMessage} >&2\r\nexit /b 1"
                : $"@echo off\r\n{captureCommand}\r\nexit /b 0";
        }
        else
        {
            scriptContent = shouldFail
                ? $"#!/bin/sh\n{captureCommand}\necho \"{errorMessage}\" 1>&2\nexit 1"
                : $"#!/bin/sh\n{captureCommand}\nexit 0";
        }

        File.WriteAllText(scriptPath, scriptContent);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            File.SetUnixFileMode(scriptPath, UnixFileMode.UserExecute | UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        return scriptPath;
    }
}