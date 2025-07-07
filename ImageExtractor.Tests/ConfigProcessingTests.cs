using ImageExtractor.Infrastructure.Config;

namespace ImageExtractor.Tests;

public class ConfigProcessingTests
{
    [Fact]
    public void Defaults_Are_Set_Correctly()
    {
        var cfg = new ConfigProcessing();

        Assert.Equal("jpg", cfg.FrameExtension);
        Assert.Equal(1, cfg.FrameRate);
        Assert.Equal(30, cfg.BlockSize);
    }
}
