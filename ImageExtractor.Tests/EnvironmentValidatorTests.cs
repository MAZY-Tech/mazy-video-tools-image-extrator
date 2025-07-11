using ImageExtractor.Infrastructure.Config;

namespace ImageExtractor.Tests;

public class EnvironmentValidatorTests
{
    private readonly string _testDir;
    private readonly string _tempFolder;
    private readonly string _dummyFfmpegPath;
    private readonly string _dummyFfprobePath;

    public EnvironmentValidatorTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "validator_tests_" + Guid.NewGuid());
        Directory.CreateDirectory(_testDir);

        _tempFolder = Path.Combine(_testDir, "temp");
        Directory.CreateDirectory(_tempFolder);

        _dummyFfmpegPath = Path.Combine(_testDir, "ffmpeg.exe");
        _dummyFfprobePath = Path.Combine(_testDir, "ffprobe.exe");

        Environment.SetEnvironmentVariable("TEMP_FOLDER", _tempFolder);
    }

    [Fact]
    public void Validate_Succeeds_When_All_Files_Exist()
    {
        File.WriteAllText(_dummyFfmpegPath, "content");
        File.WriteAllText(_dummyFfprobePath, "content");

        var validator = new EnvironmentValidator(_dummyFfmpegPath, _dummyFfprobePath);

        var exception = Record.Exception(() => validator.Validate());
        Assert.Null(exception);
    }

    [Fact]
    public void Validate_Throws_When_Temp_Folder_Does_Not_Exist()
    {
        Environment.SetEnvironmentVariable("TEMP_FOLDER", Path.Combine(_testDir, "non_existent_temp"));
        var validator = new EnvironmentValidator();

        Assert.Throws<InvalidOperationException>(() => validator.Validate());
    }

    [Fact]
    public void Validate_Throws_When_Ffmpeg_Is_Missing()
    {
        File.WriteAllText(_dummyFfprobePath, "content");

        var validator = new EnvironmentValidator(_dummyFfmpegPath, _dummyFfprobePath);

        var exception = Assert.Throws<InvalidOperationException>(() => validator.Validate());
        Assert.Contains("ffmpeg not found", exception.Message);
    }

    [Fact]
    public void Validate_Throws_When_Ffprobe_Is_Missing()
    {
        File.WriteAllText(_dummyFfmpegPath, "content");

        var validator = new EnvironmentValidator(_dummyFfmpegPath, _dummyFfprobePath);

        var exception = Assert.Throws<InvalidOperationException>(() => validator.Validate());
        Assert.Contains("ffprobe not found", exception.Message);
    }
}