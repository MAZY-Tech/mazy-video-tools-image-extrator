using ImageExtractor.Application.Interfaces;
using ImageExtractor.Infrastructure.VideoProcessing;
using Moq;
using System.Runtime.InteropServices;

namespace ImageExtractor.Tests;

public class FfprobeVideoAnalyzerTests
{
    private readonly Mock<IAppLogger> _mockLogger;
    private const string FakeFfprobeOutput = @"{
        ""streams"": [
            {
                ""codec_type"": ""video"",
                ""nb_frames"": ""1798""
            },
            {
                ""codec_type"": ""audio""
            }
        ],
        ""format"": {
            ""duration"": ""59.989000""
        }
    }";

    public FfprobeVideoAnalyzerTests()
    {
        _mockLogger = new Mock<IAppLogger>();
    }

    [Fact]
    public async Task AnalyzeAsync_ShouldParseFfprobeJsonOutput_Correctly()
    {
        string fakeFfprobePath = CreateFakeFfprobeScript(FakeFfprobeOutput, exitCode: 0);
        try
        {
            var analyzer = new FfprobeVideoAnalyzer(fakeFfprobePath);
            var dummyVideoPath = "/path/to/any/video.mp4";

            var metadata = await analyzer.AnalyzeAsync(dummyVideoPath, _mockLogger.Object);

            Assert.NotNull(metadata);
            Assert.Equal(59.989, metadata.DurationSeconds, precision: 5);
            Assert.Equal(1798, metadata.FrameCount);
        }
        finally
        {
            if (File.Exists(fakeFfprobePath))
            {
                File.Delete(fakeFfprobePath);
            }
        }
    }

    [Fact]
    public async Task AnalyzeAsync_ShouldThrowCorrectException_WhenBinaryDoesNotExist()
    {
        var nonExistentPath = Path.Combine(Path.GetTempPath(), "nonexistent-ffprobe");
        var analyzer = new FfprobeVideoAnalyzer(nonExistentPath);
        var dummyVideoPath = "dummy.mp4";

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => analyzer.AnalyzeAsync(dummyVideoPath, _mockLogger.Object)
        );

        Assert.Contains("Failed to analyze video", exception.Message);

        Assert.NotNull(exception.InnerException);

        Assert.IsType<FileNotFoundException>(exception.InnerException);
    }

    [Fact]
    public async Task AnalyzeAsync_ShouldThrowInvalidOperationException_WhenProcessFails()
    {
        var errorMessage = "This is a fake error message.";
        string fakeFfprobePath = CreateFakeFfprobeScript(errorMessage, exitCode: 1, toStdError: true);
        try
        {
            var analyzer = new FfprobeVideoAnalyzer(fakeFfprobePath);
            var dummyVideoPath = "/path/to/video.mp4";

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => analyzer.AnalyzeAsync(dummyVideoPath, _mockLogger.Object)
            );

            Assert.Contains(errorMessage, exception.Message);
            Assert.Contains("ffprobe failed with exit code 1", exception.Message);
        }
        finally
        {
            if (File.Exists(fakeFfprobePath))
            {
                File.Delete(fakeFfprobePath);
            }
        }
    }

    [Fact]
    public async Task AnalyzeAsync_ShouldThrowInvalidOperationException_ForInvalidJson()
    {
        var invalidJson = "this is not valid json";
        string fakeFfprobePath = CreateFakeFfprobeScript(invalidJson, exitCode: 0);
        try
        {
            var analyzer = new FfprobeVideoAnalyzer(fakeFfprobePath);
            var dummyVideoPath = "/path/to/video.mp4";

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => analyzer.AnalyzeAsync(dummyVideoPath, _mockLogger.Object)
            );

            Assert.Contains("Failed to parse ffprobe JSON output", exception.Message);
        }
        finally
        {
            if (File.Exists(fakeFfprobePath))
            {
                File.Delete(fakeFfprobePath);
            }
        }
    }

    [Fact]
    public async Task AnalyzeAsync_ShouldThrowInvalidOperationException_ForEmptyOutput()
    {
        string fakeFfprobePath = CreateFakeFfprobeScript(string.Empty, exitCode: 0);
        try
        {
            var analyzer = new FfprobeVideoAnalyzer(fakeFfprobePath);
            var dummyVideoPath = "/path/to/video.mp4";

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => analyzer.AnalyzeAsync(dummyVideoPath, _mockLogger.Object)
            );

            Assert.Equal("ffprobe returned empty output", exception.Message);
        }
        finally
        {
            if (File.Exists(fakeFfprobePath))
            {
                File.Delete(fakeFfprobePath);
            }
        }
    }

    private string CreateFakeFfprobeScript(string output, int exitCode, bool toStdError = false)
    {
        var scriptName = "fake-ffprobe-" + Guid.NewGuid();
        string scriptExtension = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".bat" : "";
        var scriptPath = Path.Combine(Path.GetTempPath(), scriptName + scriptExtension);

        string scriptContent;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var tempOutputPath = scriptPath + ".output.txt";
            File.WriteAllText(tempOutputPath, output);

            string redirect = toStdError ? "1>&2" : "";

            scriptContent = $"@echo off\r\n(type \"{tempOutputPath}\") {redirect}\r\ndel \"{tempOutputPath}\"\r\nexit /b {exitCode}";
        }
        else
        {
            string redirect = toStdError ? "1>&2" : "";
            scriptContent = $"""
            #!/bin/sh
            cat <<'FPROBE_EOF' {redirect}
            {output}
            FPROBE_EOF
            exit {exitCode}
            """;
        }

        File.WriteAllText(scriptPath, scriptContent);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            File.SetUnixFileMode(scriptPath, UnixFileMode.UserExecute | UnixFileMode.UserRead);
        }

        return scriptPath;
    }
}