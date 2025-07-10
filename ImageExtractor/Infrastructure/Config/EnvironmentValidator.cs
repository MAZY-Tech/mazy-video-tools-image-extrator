using ImageExtractor.Application.Interfaces;
using System.Runtime.InteropServices;

namespace ImageExtractor.Infrastructure.Config;

public class EnvironmentValidator : IEnvironmentValidator
{
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

        var architecture = RuntimeInformation.ProcessArchitecture;
        string ffmpegPath;
        string ffprobePath;

        Console.WriteLine($"[LOG] Detected runtime architecture: {architecture}");

        switch (architecture)
        {
            case Architecture.X64:
                ffmpegPath = "/opt/bin/x86_64/ffmpeg";
                ffprobePath = "/opt/bin/x86_64/ffprobe";
                Console.WriteLine($"[LOG] Set binary paths for x86_64.");
                break;

            case Architecture.Arm64:
                ffmpegPath = "/opt/bin/arm64/ffmpeg";
                ffprobePath = "/opt/bin/arm64/ffprobe";
                Console.WriteLine($"[LOG] Set binary paths for arm64.");
                break;

            default:
                throw new PlatformNotSupportedException($"Unsupported architecture for validation: {architecture}");
        }

        Console.WriteLine($"[LOG] Validating ffmpeg executable at: {ffmpegPath}");
        if (!IsExecutableAvailable(ffmpegPath))
        {
            throw new InvalidOperationException($"ffmpeg not found or is empty at the expected path: {ffmpegPath}");
        }
        Console.WriteLine("[LOG] ffmpeg validation passed.");


        Console.WriteLine($"[LOG] Validating ffprobe executable at: {ffprobePath}");
        if (!IsExecutableAvailable(ffprobePath))
        {
            throw new InvalidOperationException($"ffprobe not found or is empty at the expected path: {ffprobePath}");
        }
        Console.WriteLine("[LOG] ffprobe validation passed.");

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
}