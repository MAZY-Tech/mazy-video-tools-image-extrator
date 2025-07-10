using ImageExtractor.Application.Interfaces;
using ImageExtractor.Domain;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace ImageExtractor.Infrastructure.VideoProcessing;

public class FfprobeVideoAnalyzer(string ffprobePath) : IVideoAnalyzer
{
    public async Task<VideoMetadata> AnalyzeAsync(string videoPath, IAppLogger logger)
    {
        logger.Log($"[FfprobeVideoAnalyzer] Starting analysis for video: {videoPath}");

        // Validate binary before attempting to use it
        await ValidateBinaryAsync(ffprobePath, logger);

        var args = $"-v quiet -print_format json -show_format -show_streams \"{videoPath}\"";
        logger.Log($"[FfprobeVideoAnalyzer] Executing command: '{ffprobePath} {args}'");

        var psi = new ProcessStartInfo
        {
            FileName = ffprobePath,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        Process? process = null;
        try
        {
            process = Process.Start(psi);
            if (process == null)
            {
                logger.Log("[FfprobeVideoAnalyzer] [ERROR] Failed to start ffprobe process - Process.Start returned null");
                throw new InvalidOperationException("Failed to start ffprobe process - Process.Start returned null");
            }

            // Read both outputs simultaneously
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            // Add timeout to prevent hanging
            var timeoutTask = Task.Delay(TimeSpan.FromMinutes(5));
            var processTask = process.WaitForExitAsync();

            var completedTask = await Task.WhenAny(processTask, timeoutTask);

            if (completedTask == timeoutTask)
            {
                logger.Log("[FfprobeVideoAnalyzer] [ERROR] ffprobe process timed out");
                process.Kill();
                throw new TimeoutException("ffprobe process timed out after 5 minutes");
            }

            // If exit code is not 0, something went wrong.
            if (process.ExitCode != 0)
            {
                var errorMessage = await errorTask;
                logger.Log($"[FfprobeVideoAnalyzer] [ERROR] ffprobe process exited with code {process.ExitCode}. Error: {errorMessage}");
                throw new InvalidOperationException($"ffprobe failed with exit code {process.ExitCode}: {errorMessage}");
            }

            var output = await outputTask;

            if (string.IsNullOrWhiteSpace(output))
            {
                logger.Log("[FfprobeVideoAnalyzer] [ERROR] ffprobe returned empty output");
                throw new InvalidOperationException("ffprobe returned empty output");
            }

            logger.Log("[FfprobeVideoAnalyzer] Analysis completed successfully.");
            logger.Log($"[FfprobeVideoAnalyzer] Output length: {output.Length} characters");

            return ParseFFProbeOutput(output, logger);
        }
        catch (Exception ex) when (ex is not InvalidOperationException && ex is not TimeoutException)
        {
            logger.Log($"[FfprobeVideoAnalyzer] [ERROR] Unexpected error: {ex.Message}");
            logger.Log($"[FfprobeVideoAnalyzer] [ERROR] Stack trace: {ex.StackTrace}");
            throw new InvalidOperationException($"Failed to analyze video: {ex.Message}", ex);
        }
        finally
        {
            process?.Dispose();
        }
    }

    private async Task ValidateBinaryAsync(string binaryPath, IAppLogger logger)
    {
        logger.Log($"[FfprobeVideoAnalyzer] Validating binary: {binaryPath}");

        if (!File.Exists(binaryPath))
        {
            logger.Log($"[FfprobeVideoAnalyzer] [ERROR] Binary not found: {binaryPath}");
            throw new FileNotFoundException($"ffprobe binary not found at: {binaryPath}");
        }

        var fileInfo = new FileInfo(binaryPath);
        logger.Log($"[FfprobeVideoAnalyzer] Binary size: {fileInfo.Length} bytes");
        logger.Log($"[FfprobeVideoAnalyzer] Binary last modified: {fileInfo.LastWriteTime}");

        // Check if running on Linux (Lambda environment)
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            try
            {
                // Check file type using 'file' command
                var fileTypeResult = await RunCommandAsync("file", binaryPath, logger);
                logger.Log($"[FfprobeVideoAnalyzer] File type: {fileTypeResult}");

                // Check if it's an ELF executable
                if (!fileTypeResult.Contains("ELF") && !fileTypeResult.Contains("executable"))
                {
                    logger.Log($"[FfprobeVideoAnalyzer] [WARNING] Binary may not be a valid executable: {fileTypeResult}");
                }

                // Check architecture compatibility
                var currentArch = RuntimeInformation.ProcessArchitecture;
                logger.Log($"[FfprobeVideoAnalyzer] Current process architecture: {currentArch}");

                if (currentArch == Architecture.X64 && !fileTypeResult.Contains("x86-64"))
                {
                    logger.Log($"[FfprobeVideoAnalyzer] [WARNING] Architecture mismatch detected. Expected x86-64, got: {fileTypeResult}");
                }
                else if (currentArch == Architecture.Arm64 && !fileTypeResult.Contains("aarch64"))
                {
                    logger.Log($"[FfprobeVideoAnalyzer] [WARNING] Architecture mismatch detected. Expected aarch64, got: {fileTypeResult}");
                }
            }
            catch (Exception ex)
            {
                logger.Log($"[FfprobeVideoAnalyzer] [WARNING] Could not validate binary architecture: {ex.Message}");
            }
        }
    }

    private async Task<string> RunCommandAsync(string command, string arguments, IAppLogger logger)
    {
        var psi = new ProcessStartInfo
        {
            FileName = command,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
            throw new InvalidOperationException($"Failed to start process: {command}");

        await process.WaitForExitAsync();
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();

        return process.ExitCode == 0 ? output.Trim() : error.Trim();
    }

    private VideoMetadata ParseFFProbeOutput(string output, IAppLogger logger)
    {
        try
        {
            using var jsonDoc = JsonDocument.Parse(output);
            var root = jsonDoc.RootElement;

            if (!root.TryGetProperty("format", out var format))
            {
                throw new InvalidOperationException("ffprobe output missing 'format' property");
            }

            if (!format.TryGetProperty("duration", out var durationProp))
            {
                throw new InvalidOperationException("ffprobe output missing 'duration' property");
            }

            var durationString = durationProp.GetString();
            if (string.IsNullOrWhiteSpace(durationString))
            {
                throw new InvalidOperationException("ffprobe output has empty duration");
            }

            if (!double.TryParse(durationString, NumberStyles.Float, CultureInfo.InvariantCulture, out var duration))
            {
                throw new InvalidOperationException($"Could not parse duration: {durationString}");
            }

            var videoStream = root.GetProperty("streams").EnumerateArray()
                .FirstOrDefault(s => s.GetProperty("codec_type").GetString() == "video");

            int nbFrames = 0;
            if (videoStream.TryGetProperty("nb_frames", out var nbFramesProp))
            {
                var nbFramesString = nbFramesProp.GetString();
                if (!string.IsNullOrWhiteSpace(nbFramesString))
                {
                    int.TryParse(nbFramesString, out nbFrames);
                }
            }

            logger.Log($"[FfprobeVideoAnalyzer] Parsed video metadata - Duration: {duration}s, Frames: {nbFrames}");

            return new VideoMetadata
            {
                DurationSeconds = duration,
                FrameCount = nbFrames
            };
        }
        catch (JsonException ex)
        {
            logger.Log($"[FfprobeVideoAnalyzer] [ERROR] Failed to parse JSON output: {ex.Message}");
            logger.Log($"[FfprobeVideoAnalyzer] [ERROR] Raw output: {output}");
            throw new InvalidOperationException($"Failed to parse ffprobe JSON output: {ex.Message}", ex);
        }
    }
}