using ImageExtractor.Application.Interfaces;
using ImageExtractor.Infrastructure.VideoProcessing;
using Moq;
using System.Runtime.InteropServices;

namespace ImageExtractor.Tests;

public class FfprobeVideoAnalyzerExtendedTests
{
    private readonly Mock<IAppLogger> _mockLogger;

    private const string JsonWithoutNbFrames = @"{
        ""streams"": [
            {
                ""codec_type"": ""video""
            }
        ],
        ""format"": {
            ""duration"": ""30.5""
        }
    }";

    private const string JsonWithEmptyNbFrames = @"{
        ""streams"": [
            {
                ""codec_type"": ""video"",
                ""nb_frames"": """"
            }
        ],
        ""format"": {
            ""duration"": ""45.123""
        }
    }";

    private const string JsonWithInvalidNbFrames = @"{
        ""streams"": [
            {
                ""codec_type"": ""video"",
                ""nb_frames"": ""not_a_number""
            }
        ],
        ""format"": {
            ""duration"": ""25.0""
        }
    }";

    private const string JsonWithoutVideoStream = @"{
        ""streams"": [
            {
                ""codec_type"": ""audio""
            }
        ],
        ""format"": {
            ""duration"": ""60.0""
        }
    }";

    private const string JsonWithoutFormat = @"{
        ""streams"": [
            {
                ""codec_type"": ""video"",
                ""nb_frames"": ""1000""
            }
        ]
    }";

    private const string JsonWithoutDuration = @"{
        ""streams"": [
            {
                ""codec_type"": ""video"",
                ""nb_frames"": ""1000""
            }
        ],
        ""format"": {}
    }";

    private const string JsonWithEmptyDuration = @"{
        ""streams"": [
            {
                ""codec_type"": ""video"",
                ""nb_frames"": ""1000""
            }
        ],
        ""format"": {
            ""duration"": """"
        }
    }";

    private const string JsonWithInvalidDuration = @"{
        ""streams"": [
            {
                ""codec_type"": ""video"",
                ""nb_frames"": ""1000""
            }
        ],
        ""format"": {
            ""duration"": ""not_a_number""
        }
    }";

    public FfprobeVideoAnalyzerExtendedTests()
    {
        _mockLogger = new Mock<IAppLogger>();
    }

    [Fact]
    public async Task AnalyzeAsync_ShouldParseSuccessfully_WhenNbFramesIsMissing()
    {
        string fakeFfprobePath = CreateFakeFfprobeScript(JsonWithoutNbFrames, exitCode: 0);
        try
        {
            var analyzer = new FfprobeVideoAnalyzer(fakeFfprobePath);
            var dummyVideoPath = "/path/to/video.mp4";

            var metadata = await analyzer.AnalyzeAsync(dummyVideoPath, _mockLogger.Object);

            Assert.NotNull(metadata);
            Assert.Equal(30.5, metadata.DurationSeconds, precision: 5);
            Assert.Equal(0, metadata.FrameCount);
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
    public async Task AnalyzeAsync_ShouldParseSuccessfully_WhenNbFramesIsEmpty()
    {
        string fakeFfprobePath = CreateFakeFfprobeScript(JsonWithEmptyNbFrames, exitCode: 0);
        try
        {
            var analyzer = new FfprobeVideoAnalyzer(fakeFfprobePath);
            var dummyVideoPath = "/path/to/video.mp4";

            var metadata = await analyzer.AnalyzeAsync(dummyVideoPath, _mockLogger.Object);

            Assert.NotNull(metadata);
            Assert.Equal(45.123, metadata.DurationSeconds, precision: 5);
            Assert.Equal(0, metadata.FrameCount);
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
    public async Task AnalyzeAsync_ShouldParseSuccessfully_WhenNbFramesIsInvalid()
    {
        string fakeFfprobePath = CreateFakeFfprobeScript(JsonWithInvalidNbFrames, exitCode: 0);
        try
        {
            var analyzer = new FfprobeVideoAnalyzer(fakeFfprobePath);
            var dummyVideoPath = "/path/to/video.mp4";

            var metadata = await analyzer.AnalyzeAsync(dummyVideoPath, _mockLogger.Object);

            Assert.NotNull(metadata);
            Assert.Equal(25.0, metadata.DurationSeconds, precision: 5);
            Assert.Equal(0, metadata.FrameCount);
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
    public async Task AnalyzeAsync_ShouldParseSuccessfully_WhenNoVideoStream()
    {
        string fakeFfprobePath = CreateFakeFfprobeScript(JsonWithoutVideoStream, exitCode: 0);
        try
        {
            var analyzer = new FfprobeVideoAnalyzer(fakeFfprobePath);
            var dummyVideoPath = "/path/to/video.mp4";

            var metadata = await analyzer.AnalyzeAsync(dummyVideoPath, _mockLogger.Object);

            Assert.NotNull(metadata);
            Assert.Equal(60.0, metadata.DurationSeconds, precision: 5);
            Assert.Equal(0, metadata.FrameCount);
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
    public async Task AnalyzeAsync_ShouldThrowInvalidOperationException_WhenFormatIsMissing()
    {
        string fakeFfprobePath = CreateFakeFfprobeScript(JsonWithoutFormat, exitCode: 0);
        try
        {
            var analyzer = new FfprobeVideoAnalyzer(fakeFfprobePath);
            var dummyVideoPath = "/path/to/video.mp4";

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => analyzer.AnalyzeAsync(dummyVideoPath, _mockLogger.Object)
            );

            Assert.Contains("ffprobe output missing 'format' property", exception.Message);
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
    public async Task AnalyzeAsync_ShouldThrowInvalidOperationException_WhenDurationIsMissing()
    {
        string fakeFfprobePath = CreateFakeFfprobeScript(JsonWithoutDuration, exitCode: 0);
        try
        {
            var analyzer = new FfprobeVideoAnalyzer(fakeFfprobePath);
            var dummyVideoPath = "/path/to/video.mp4";

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => analyzer.AnalyzeAsync(dummyVideoPath, _mockLogger.Object)
            );

            Assert.Contains("ffprobe output missing 'duration' property", exception.Message);
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
    public async Task AnalyzeAsync_ShouldThrowInvalidOperationException_WhenDurationIsEmpty()
    {
        string fakeFfprobePath = CreateFakeFfprobeScript(JsonWithEmptyDuration, exitCode: 0);
        try
        {
            var analyzer = new FfprobeVideoAnalyzer(fakeFfprobePath);
            var dummyVideoPath = "/path/to/video.mp4";

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => analyzer.AnalyzeAsync(dummyVideoPath, _mockLogger.Object)
            );

            Assert.Contains("ffprobe output has empty duration", exception.Message);
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
    public async Task AnalyzeAsync_ShouldThrowInvalidOperationException_WhenDurationIsInvalid()
    {
        string fakeFfprobePath = CreateFakeFfprobeScript(JsonWithInvalidDuration, exitCode: 0);
        try
        {
            var analyzer = new FfprobeVideoAnalyzer(fakeFfprobePath);
            var dummyVideoPath = "/path/to/video.mp4";

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => analyzer.AnalyzeAsync(dummyVideoPath, _mockLogger.Object)
            );

            Assert.Contains("Could not parse duration", exception.Message);
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
    public async Task AnalyzeAsync_ShouldThrowTimeoutException_WhenProcessHangs()
    {
        string fakeFfprobePath = CreateHangingScript();
        try
        {
            var analyzer = new FfprobeVideoAnalyzer(fakeFfprobePath, 100);
            var dummyVideoPath = "/path/to/video.mp4";

            var exception = await Assert.ThrowsAsync<TimeoutException>(
                () => analyzer.AnalyzeAsync(dummyVideoPath, _mockLogger.Object)
            );

            Assert.Contains("ffprobe process timed out", exception.Message);
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
    public async Task AnalyzeAsync_ShouldThrowInvalidOperationException_WhenProcessStartReturnsNull()
    {
        var invalidExecutablePath = CreateInvalidExecutable();
        try
        {
            var analyzer = new FfprobeVideoAnalyzer(invalidExecutablePath);
            var dummyVideoPath = "/path/to/video.mp4";

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => analyzer.AnalyzeAsync(dummyVideoPath, _mockLogger.Object)
            );

            Assert.Contains("Failed to analyze video", exception.Message);

            Assert.NotNull(exception.InnerException);
        }
        finally
        {
            if (File.Exists(invalidExecutablePath))
            {
                File.Delete(invalidExecutablePath);
            }
        }
    }

    [Fact]
    public async Task AnalyzeAsync_ShouldWrapUnexpectedExceptions()
    {
        var analyzer = new FfprobeVideoAnalyzer("/dev/null/invalid/path");
        var dummyVideoPath = "/path/to/video.mp4";

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => analyzer.AnalyzeAsync(dummyVideoPath, _mockLogger.Object)
        );

        Assert.Contains("Failed to analyze video", exception.Message);
        Assert.NotNull(exception.InnerException);
    }

    private static string CreateFakeFfprobeScript(string output, int exitCode, bool toStdError = false)
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

    private static string CreateHangingScript()
    {
        var scriptName = "hanging-ffprobe-" + Guid.NewGuid();
        string scriptExtension = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".bat" : "";
        var scriptPath = Path.Combine(Path.GetTempPath(), scriptName + scriptExtension);

        string scriptContent;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            scriptContent = "@echo off\r\n:loop\r\ntimeout /t 1 /nobreak >nul\r\ngoto loop";
        }
        else
        {
            scriptContent = "#!/bin/sh\nwhile true; do sleep 1; done";
        }

        File.WriteAllText(scriptPath, scriptContent);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            File.SetUnixFileMode(scriptPath, UnixFileMode.UserExecute | UnixFileMode.UserRead);
        }

        return scriptPath;
    }

    private static string CreateInvalidExecutable()
    {
        var fileName = "invalid-executable-" + Guid.NewGuid();
        var filePath = Path.Combine(Path.GetTempPath(), fileName);

        File.WriteAllText(filePath, "This is not an executable file");

        return filePath;
    }
}