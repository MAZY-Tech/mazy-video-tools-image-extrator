using ImageExtractor.Application.Interfaces;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ImageExtractor.Infrastructure.Config;

public class EnvironmentValidator : IEnvironmentValidator
{
    private readonly string? _ffmpegPath;
    private readonly string? _ffprobePath;

    public EnvironmentValidator() { }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public EnvironmentValidator(string ffmpegPath, string ffprobePath)
    {
        _ffmpegPath = ffmpegPath;
        _ffprobePath = ffprobePath;
    }

    public void Validate()
    {
        Console.WriteLine("[LOG] Starting environment validation...");

        var temp = Environment.GetEnvironmentVariable("TEMP_FOLDER") ?? "/tmp";
        Console.WriteLine($"[LOG] Validating temporary folder path: {temp}");
        if (!Directory.Exists(temp))
        {
            throw new InvalidOperationException($"Temporary folder does not exist: {temp}");
        }
        Console.WriteLine("[LOG] Temporary folder validation passed.");

        string ffmpegPath = _ffmpegPath ?? GetPathForArch("ffmpeg");
        string ffprobePath = _ffprobePath ?? GetPathForArch("ffprobe");

        Console.WriteLine($"[LOG] Validating ffmpeg executable at: {ffmpegPath}");
        if (!IsExecutableAvailable(ffmpegPath))
        {
            throw new InvalidOperationException($"ffmpeg not found or is empty at the expected path: {ffmpegPath}");
        }

        Console.WriteLine($"[LOG] Validating ffprobe executable at: {ffprobePath}");
        if (!IsExecutableAvailable(ffprobePath))
        {
            throw new InvalidOperationException($"ffprobe not found or is empty at the expected path: {ffprobePath}");
        }

        Console.WriteLine("[LOG] Environment validation completed successfully.");
    }

    /// <summary>
    /// Checks if a file exists at the given path and is not empty.
    /// </summary>
    /// <param name="path">The full path to the file.</param>
    /// <returns>True if the file exists and has a size greater than 0.</returns>
    private static bool IsExecutableAvailable(string path)
    {
        return File.Exists(path) && new FileInfo(path).Length > 0;
    }

    private static string GetPathForArch(string binaryName)
    {
        var architecture = RuntimeInformation.ProcessArchitecture;
        Console.WriteLine($"[LOG] Detected runtime architecture: {architecture}");

        return architecture switch
        {
            Architecture.X64 => $"/opt/bin/x86_64/{binaryName}",
            _ => throw new PlatformNotSupportedException($"Unsupported architecture for validation: {architecture}")
        };
    }

}