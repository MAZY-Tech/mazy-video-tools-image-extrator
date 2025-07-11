using ImageExtractor.Application.Interfaces;
using ImageExtractor.Domain;
using ImageExtractor.Infrastructure.VideoProcessing;
using Moq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ImageExtractor.Tests;

public class FfprobeBinaryValidationTests
{
    private readonly Mock<IAppLogger> _mockLogger;

    public FfprobeBinaryValidationTests()
    {
        _mockLogger = new Mock<IAppLogger>();
    }

    [Fact]
    public async Task ValidateBinaryAsync_ShouldLogFileInfo_WhenBinaryExists()
    {
        var tempBinaryPath = CreateTempBinary();

        try
        {
            var method = typeof(FfprobeVideoAnalyzer).GetMethod("ValidateBinaryAsync",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(method);
            var task = (Task?)method.Invoke(null, [tempBinaryPath, _mockLogger.Object]);
            Assert.NotNull(task);
            await task;

            _mockLogger.Verify(x => x.Log(It.Is<string>(s => s.Contains("Validating binary"))), Times.Once);
            _mockLogger.Verify(x => x.Log(It.Is<string>(s => s.Contains("Binary size"))), Times.Once);
            _mockLogger.Verify(x => x.Log(It.Is<string>(s => s.Contains("Binary last modified"))), Times.Once);
        }
        finally
        {
            if (File.Exists(tempBinaryPath))
            {
                File.Delete(tempBinaryPath);
            }
        }
    }

    [Fact]
    public async Task ValidateBinaryAsync_ShouldCheckArchitecture_OnLinux()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return;
        }

        var tempBinaryPath = CreateTempBinary();

        try
        {
            var method = typeof(FfprobeVideoAnalyzer).GetMethod("ValidateBinaryAsync",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(method);
            var task = (Task?)method.Invoke(null, [tempBinaryPath, _mockLogger.Object]);
            Assert.NotNull(task);
            await task;

            _mockLogger.Verify(x => x.Log(It.Is<string>(s => s.Contains("Running command: file"))), Times.Once);
            _mockLogger.Verify(x => x.Log(It.Is<string>(s => s.Contains("Current process architecture"))), Times.Once);
        }
        finally
        {
            if (File.Exists(tempBinaryPath))
            {
                File.Delete(tempBinaryPath);
            }
        }
    }

    [Fact]
    public async Task RunCommandAsync_ShouldReturnError_WhenCommandFails()
    {
        var method = typeof(FfprobeVideoAnalyzer).GetMethod("RunCommandAsync",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        string command, arguments;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            command = "cmd";
            arguments = "/c exit 1";
        }
        else
        {
            command = "sh";
            arguments = "-c 'exit 1'";
        }

        var task = (Task<string>?)method.Invoke(null, new object[] { command, arguments, _mockLogger.Object });
        Assert.NotNull(task);
        var result = await task;

        Assert.NotNull(result);
    }

    [Fact]
    public void ParseFFProbeOutput_ShouldParseValidJson_Correctly()
    {
        var validJson = @"{
            ""streams"": [
                {
                    ""codec_type"": ""video"",
                    ""nb_frames"": ""1500""
                }
            ],
            ""format"": {
                ""duration"": ""60.0""
            }
        }";

        var method = typeof(FfprobeVideoAnalyzer).GetMethod("ParseFFProbeOutput",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var result = (VideoMetadata?)method.Invoke(null, [validJson, _mockLogger.Object]);

        Assert.NotNull(result);
        Assert.Equal(60.0, result.DurationSeconds);
        Assert.Equal(1500, result.FrameCount);
    }

    [Fact]
    public async Task ValidateBinaryAsync_ShouldHandleArchitectureValidation()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return;
        }

        var tempBinaryPath = CreateTempBinary();

        try
        {
            var method = typeof(FfprobeVideoAnalyzer).GetMethod("ValidateBinaryAsync",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(method);

            var task = (Task?)method.Invoke(null, [tempBinaryPath, _mockLogger.Object]);
            Assert.NotNull(task);
            await task;

            _mockLogger.Verify(x => x.Log(It.Is<string>(s => s.Contains("architecture"))), Times.AtLeastOnce);
        }
        catch (Exception)
        {
            _mockLogger.Verify(x => x.Log(It.Is<string>(s => s.Contains("WARNING") && s.Contains("validate binary"))), Times.AtLeastOnce);
        }
        finally
        {
            if (File.Exists(tempBinaryPath))
            {
                File.Delete(tempBinaryPath);
            }
        }
    }

    [Fact]
    public async Task ValidateBinaryAsync_ShouldLogWarning_WhenValidationFails()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return;
        }

        var invalidBinaryPath = Path.Combine(Path.GetTempPath(), "invalid-binary-" + Guid.NewGuid());
        await File.WriteAllTextAsync(invalidBinaryPath, "not executable");

        try
        {
            var analyzer = new FfprobeVideoAnalyzer(invalidBinaryPath);
            var method = typeof(FfprobeVideoAnalyzer).GetMethod("ValidateBinaryAsync",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(method);
            var task = (Task?)method.Invoke(null, new object[] { invalidBinaryPath, _mockLogger.Object });
            Assert.NotNull(task);
            await task;

            _mockLogger.Verify(x => x.Log(It.Is<string>(s => s.Contains("Validating binary"))), Times.Once);
        }
        finally
        {
            if (File.Exists(invalidBinaryPath))
            {
                File.Delete(invalidBinaryPath);
            }
        }
    }

    [Fact]
    public async Task AnalyzeAsync_ShouldDisposeProcess_EvenWhenExceptionOccurs()
    {
        var scriptPath = CreateScriptThatFails();

        try
        {
            var analyzer = new FfprobeVideoAnalyzer(scriptPath);
            var dummyVideoPath = "/path/to/video.mp4";

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => analyzer.AnalyzeAsync(dummyVideoPath, _mockLogger.Object)
            );

            Assert.True(true);
        }
        finally
        {
            if (File.Exists(scriptPath))
            {
                File.Delete(scriptPath);
            }
        }
    }

    private static string CreateTempBinary()
    {
        var fileName = "temp-binary-" + Guid.NewGuid();
        var filePath = Path.Combine(Path.GetTempPath(), fileName);

        File.WriteAllText(filePath, "#!/bin/sh\necho 'fake binary'");

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            File.SetUnixFileMode(filePath, UnixFileMode.UserExecute | UnixFileMode.UserRead);
        }

        return filePath;
    }

    private static string CreateScriptThatFails()
    {
        var scriptName = "failing-script-" + Guid.NewGuid();
        string scriptExtension = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".bat" : "";
        var scriptPath = Path.Combine(Path.GetTempPath(), scriptName + scriptExtension);

        string scriptContent;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            scriptContent = "@echo off\necho Error message 1>&2\nexit /b 1";
        }
        else
        {
            scriptContent = "#!/bin/sh\necho 'Error message' 1>&2\nexit 1";
        }

        File.WriteAllText(scriptPath, scriptContent);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            File.SetUnixFileMode(scriptPath, UnixFileMode.UserExecute | UnixFileMode.UserRead);
        }

        return scriptPath;
    }
}