using ImageExtractor.Infrastructure.VideoProcessing;
using System.Runtime.InteropServices;

namespace ImageExtractor.Tests;

public class FfprobeVideoAnalyzerTests
{
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

    [Fact]
    public async Task AnalyzeAsync_ShouldParseFfprobeJsonOutput_Correctly()
    {
        string fakeFfprobePath = CreateFakeFfprobeScript();

        try
        {
            var analyzer = new FfprobeVideoAnalyzer(fakeFfprobePath);
            var dummyVideoPath = "/path/to/any/video.mp4";

            var metadata = await analyzer.AnalyzeAsync(dummyVideoPath);

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

    private string CreateFakeFfprobeScript()
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), "fake-ffprobe" + (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".bat" : ""));

        string scriptContent;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var tempJsonPath = scriptPath + ".json";
            File.WriteAllText(tempJsonPath, FakeFfprobeOutput);

            scriptContent = $"@echo off\r\ntype \"{tempJsonPath}\"";
        }
        else
        {
            scriptContent = "#!/bin/sh\n" +
                            "cat <<EOF\n" +
                            FakeFfprobeOutput +
                            "\nEOF";
        }

        File.WriteAllText(scriptPath, scriptContent);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            File.SetUnixFileMode(scriptPath, UnixFileMode.UserExecute | UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        return scriptPath;
    }
}