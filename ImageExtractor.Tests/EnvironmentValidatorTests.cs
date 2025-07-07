using ImageExtractor.Infrastructure.Config;

namespace ImageExtractor.Tests;

public class EnvironmentValidatorTests
{
    [Fact]
    public void Validate_Throws_When_Temp_Not_Exist()
    {
        Environment.SetEnvironmentVariable("TEMP_FOLDER", "/path/does/not/exist");
        var validator = new EnvironmentValidator();

        Assert.Throws<InvalidOperationException>(() => validator.Validate());
    }

    [Fact]
    public void Validate_Throws_When_Ffmpeg_Missing()
    {
        var tmp = Path.GetTempPath();
        Environment.SetEnvironmentVariable("TEMP_FOLDER", tmp);

        var validator = new EnvironmentValidator();

        Assert.Throws<InvalidOperationException>(() => validator.Validate());
    }
}
