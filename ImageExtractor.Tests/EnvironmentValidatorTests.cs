using ImageExtractor.Infrastructure.Config;
using System.Runtime.InteropServices;

namespace ImageExtractor.Tests;

public class EnvironmentValidatorTests
{
    private readonly string _testDir;
    private readonly string _dummyFfmpegPath;
    private readonly string _dummyFfprobePath;

    public EnvironmentValidatorTests()
    {
        // Setup simplificado: não precisamos mais gerenciar um TEMP_FOLDER customizado.
        _testDir = Path.Combine(Path.GetTempPath(), "validator_tests_" + Guid.NewGuid());
        Directory.CreateDirectory(_testDir);

        _dummyFfmpegPath = Path.Combine(_testDir, "ffmpeg.exe");
        _dummyFfprobePath = Path.Combine(_testDir, "ffprobe.exe");
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

    // NOVO TESTE: Cobre o cenário de arquivo existente, mas vazio.
    [Fact]
    public void Validate_Throws_When_Ffmpeg_File_Is_Empty()
    {
        File.WriteAllText(_dummyFfmpegPath, string.Empty); // Arquivo vazio
        File.WriteAllText(_dummyFfprobePath, "content");
        var validator = new EnvironmentValidator(_dummyFfmpegPath, _dummyFfprobePath);

        var exception = Assert.Throws<InvalidOperationException>(() => validator.Validate());
        Assert.Contains("ffmpeg not found or is empty", exception.Message);
    }

    // NOVO TESTE: Cobre a lógica de GetPathForArch.
    [Fact]
    public void Validate_Calls_GetPathForArch_When_No_Paths_Are_Provided()
    {
        // Usa o construtor padrão para forçar a chamada de GetPathForArch.
        var validator = new EnvironmentValidator();

        // Este teste agora cobre ambas as branches do 'switch' em GetPathForArch.
        if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
        {
            // Em X64, esperamos falhar porque /opt/bin/x86_64/ffmpeg não existe.
            var exception = Assert.Throws<InvalidOperationException>(() => validator.Validate());
            Assert.Contains("/opt/bin/x86_64/ffmpeg", exception.Message);
        }
        else
        {
            // Em qualquer outra arquitetura, esperamos a exceção de plataforma não suportada.
            Assert.Throws<PlatformNotSupportedException>(() => validator.Validate());
        }
    }
}