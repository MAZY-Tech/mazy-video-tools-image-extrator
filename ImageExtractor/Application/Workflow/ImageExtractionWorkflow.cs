using ImageExtractor.Application.Interfaces;
using ImageExtractor.Domain;
using ImageExtractor.Infrastructure.Config;

namespace ImageExtractor.Application.Workflow;

public class ImageExtractionWorkflow(
    IJobStateRepository jobRepo,
    IVideoStorage videoStorage,
    IVideoAnalyzer analyzer,
    IFrameExtractor frameExtractor,
    IZipService zipService,
    IProgressNotifier progressNotifier,
    ICompletionNotifier completionNotifier,
    ConfigProcessing config) : IImageExtractionWorkflow
{
    public async Task ExecuteAsync(IEnumerable<ProcessingMessage> messages, IAppLogger logger)
    {
        var message = messages.Single();
        await ProcessJobAsync(message, logger);
    }

    private async Task ProcessJobAsync(ProcessingMessage msg, IAppLogger logger)
    {
        ConfigureSentryScope(msg.JobId);
        logger.Log($"[Workflow] Starting processing for job '{msg.JobId}'");

        var job = await jobRepo.GetJobStateAsync(msg.JobId) ?? new JobState { JobId = msg.JobId };

        if (ShouldSkipJob(job, msg.JobId, logger))
        {
            return;
        }

        var tempPaths = CreateTempPaths(msg);

        try
        {
            await ExecuteJobStepsAsync(msg, job, tempPaths, logger);
            await CompleteJobAsync(job, msg.JobId, logger);
        }
        catch (Exception ex)
        {
            await HandleJobFailureAsync(job, msg, ex, logger);
            throw;
        }
        finally
        {
            CleanupTempFiles(tempPaths, logger);
        }
    }

    private async Task ExecuteJobStepsAsync(ProcessingMessage msg, JobState job, TempPaths paths, IAppLogger logger)
    {
        await EnsureJobStartedAsync(job);

        await ExecuteDownloadStepAsync(msg, job, paths.VideoPath, logger);
        var metadata = await ExecuteAnalysisStepAsync(job, paths.VideoPath, logger);
        await ExecuteExtractionStepAsync(msg, job, paths, metadata, logger);
        var zipKey = await ExecuteZippingStepAsync(msg, job, paths, logger);

        job.Metadata["ZipBucket"] = config.ZipBucket;
        job.Metadata["ZipKey"] = zipKey;
    }

    private static bool ShouldSkipJob(JobState job, string jobId, IAppLogger logger)
    {
        if (job.Status == JobStatus.Completed)
        {
            logger.Log($"[Workflow] Job '{jobId}' is already complete. Skipping.");
            return true;
        }

        if (job.Status == JobStatus.Running || job.Status == JobStatus.Interrupted)
        {
            logger.Log($"[Workflow] Resuming job '{jobId}' from step '{job.CurrentStep}' with status '{job.Status}'.");
        }
        else
        {
            job.Status = JobStatus.Pending;
            job.CurrentStep = ProcessingStep.Validating;
            logger.Log($"[Workflow] Starting new job '{jobId}' with status '{job.Status}'.");
        }

        return false;
    }

    private async Task EnsureJobStartedAsync(JobState job)
    {
        if (job.StartedAt == null)
            job.StartedAt = DateTime.UtcNow;

        job.Status = JobStatus.Running;
        await jobRepo.SaveJobStateAsync(job);
    }

    private async Task ExecuteDownloadStepAsync(ProcessingMessage msg, JobState job, string tempVideoPath, IAppLogger logger)
    {
        bool isExtractionComplete = job.TotalBlocks > 0 && job.CurrentBlock >= job.TotalBlocks;

        if (isExtractionComplete)
        {
            logger.Log($"[Workflow] Extraction complete (processed {job.CurrentBlock}/{job.TotalBlocks} blocks). Video download not required. Skipping.");
            return;
        }

        job.CurrentStep = ProcessingStep.Downloading;
        logger.Log($"[Workflow] Downloading video from s3://{msg.SourceBucket}/{msg.SourceKey} to {tempVideoPath}");

        await videoStorage.DownloadVideoAsync(msg.SourceBucket, msg.SourceKey, tempVideoPath, logger);
        await jobRepo.SaveJobStateAsync(job);
    }

    private async Task<VideoMetadata> ExecuteAnalysisStepAsync(JobState job, string tempVideoPath, IAppLogger logger)
    {
        if (job.TotalFrames > 0)
        {
            logger.Log($"[Workflow] Analysis data already exists for job '{job.JobId}'. Skipping analysis step.");
            return new VideoMetadata
            {
                FrameCount = job.TotalFrames,
                DurationSeconds = job.TotalBlocks * config.BlockSize
            };
        }

        job.CurrentStep = ProcessingStep.Analyzing;
        var metadata = await analyzer.AnalyzeAsync(tempVideoPath, logger);

        job.TotalFrames = metadata.FrameCount;
        job.TotalBlocks = (int)Math.Ceiling(metadata.DurationSeconds / config.BlockSize);

        logger.Log($"[Workflow] Analysis complete. Duration: {metadata.DurationSeconds:F2}s, Total Frames: {metadata.FrameCount}, Total Blocks: {job.TotalBlocks}");
        await jobRepo.SaveJobStateAsync(job);

        return metadata;
    }

    private async Task ExecuteExtractionStepAsync(ProcessingMessage msg, JobState job, TempPaths paths, VideoMetadata metadata, IAppLogger logger)
    {
        if (job.CurrentStep > ProcessingStep.Extracting) return;

        job.CurrentStep = ProcessingStep.Extracting;
        await ExtractFramesInBlocksAsync(msg, job, paths.VideoPath, paths.FramesDir, metadata, logger);
    }

    private async Task<string> ExecuteZippingStepAsync(ProcessingMessage msg, JobState job, TempPaths paths, IAppLogger logger)
    {
        if (job.CurrentStep > ProcessingStep.Zipping)
        {
            return job.Metadata["ZipKey"]?.ToString() ?? "";
        }

        job.CurrentStep = ProcessingStep.Zipping;
        var zipPath = Path.Combine(config.TempFolder, $"{msg.JobId}.zip");

        logger.Log($"[Workflow] Downloading all frames for job {msg.JobId} to create zip file...");
        var jobFramePrefix = $"{msg.JobId}/";
        await videoStorage.DownloadAllFramesAsync(config.FramesBucket, jobFramePrefix, paths.FramesDir, logger);


        logger.Log($"[Workflow] Creating zip file at {zipPath}...");

        await zipService.CreateZipAsync(paths.FramesDir, zipPath, logger);

        var zipKey = $"{msg.JobId}/{Path.GetFileName(zipPath)}";
        await zipService.UploadZipAsync(config.ZipBucket, zipKey, zipPath, logger);

        logger.Log($"[Workflow] Zip file uploaded to s3://{config.ZipBucket}/{zipKey}");
        await jobRepo.SaveJobStateAsync(job);

        return zipKey;
    }

    private async Task ExtractFramesInBlocksAsync(ProcessingMessage msg, JobState job, string videoPath, string framesDir, VideoMetadata metadata, IAppLogger logger)
    {
        logger.Log($"[Workflow] Starting extraction in {job.TotalBlocks} blocks. Beginning from block {job.CurrentBlock + 1}.");

        for (int i = job.CurrentBlock; i < job.TotalBlocks; i++)
        {
            await ProcessSingleBlockAsync(msg, job, videoPath, framesDir, metadata, i, logger);
        }
    }

    private async Task ProcessSingleBlockAsync(ProcessingMessage msg, JobState job, string videoPath, string framesDir, VideoMetadata metadata, int blockIndex, IAppLogger logger)
    {
        var blockDuration = CalculateBlockDuration(metadata.DurationSeconds, blockIndex);
        if (blockDuration <= 0) return;

        LogBlockProgress(blockIndex, job.TotalBlocks, logger);

        var blockStartTime = DateTime.UtcNow;

        var extractionStartTime = TimeSpan.FromSeconds(blockIndex * config.BlockSize);

        await frameExtractor.ExtractFramesAsync(videoPath, framesDir, config.FrameRate, extractionStartTime, blockDuration, blockIndex, logger);

        await ProcessExtractedFramesAsync(msg, job, framesDir, blockIndex, logger);

        await UpdateBlockProgress(job, blockIndex, blockDuration);

        var blockEndTime = DateTime.UtcNow;
        var processingTime = blockEndTime - blockStartTime;

        LogBlockCompletion(blockIndex, job.TotalBlocks, job.ProcessedFrames, processingTime, logger);

        await NotifyBlockProgress(msg.JobId, job, logger);
    }

    private async Task ProcessExtractedFramesAsync(ProcessingMessage msg, JobState job, string framesDir, int blockIndex, IAppLogger logger)
    {
        var framePaths = Directory.GetFiles(framesDir, $"*.{config.FrameExtension}");
        if (framePaths.Length == 0) return;

        var targetPrefix = $"{msg.JobId}/block_{blockIndex + 1}";
        logger.Log($"[Workflow] Uploading {framePaths.Length} frames to s3://{config.FramesBucket}/{targetPrefix}");

        await videoStorage.UploadFramesAsync(
            bucket: config.FramesBucket,
            prefix: targetPrefix,
            framePaths,
            logger
        );

        job.ProcessedFrames += framePaths.Length;

        foreach (var framePath in framePaths)
            File.Delete(framePath);
    }

    private async Task UpdateBlockProgress(JobState job, int blockIndex, int blockDuration)
    {
        job.CurrentBlock = blockIndex + 1;
        job.LastProcessedSecond += blockDuration;
        job.LastHeartbeat = DateTime.UtcNow;

        if (job.TotalFrames > 0)
        {
            job.Progress = (int)Math.Round((double)job.ProcessedFrames / job.TotalFrames * 100);
        }

        await jobRepo.SaveJobStateAsync(job);
    }

    private async Task NotifyBlockProgress(string jobId, JobState job, IAppLogger logger)
    {
        var progressPct = (int)((double)job.CurrentBlock / job.TotalBlocks * 100);
        await progressNotifier.NotifyProgressAsync(jobId, progressPct, job.CurrentBlock, job.TotalBlocks, logger);
    }

    private async Task CompleteJobAsync(JobState job, string jobId, IAppLogger logger)
    {
        job.Status = JobStatus.Completed;
        job.CurrentStep = ProcessingStep.Done;
        job.CompletedAt = DateTime.UtcNow;
        job.LastHeartbeat = DateTime.UtcNow;

        await jobRepo.SaveJobStateAsync(job);

        var zipBucket = job.Metadata["ZipBucket"].ToString();
        var zipKey = job.Metadata["ZipKey"].ToString();

        await completionNotifier.NotifyCompletionAsync(jobId, zipBucket!, zipKey!, logger);
        logger.Log($"[Workflow] Job '{jobId}' completed successfully.");
    }

    private async Task HandleJobFailureAsync(JobState job, ProcessingMessage msg, Exception ex, IAppLogger logger)
    {
        logger.Log($"[Workflow] [ERROR] Job processing failed for '{msg.JobId}': {ex.Message}");
        SentrySdk.CaptureException(ex, scope => scope.SetExtra("ProcessingMessage", msg));

        job.Status = JobStatus.Failed;
        job.LastHeartbeat = DateTime.UtcNow;
        await jobRepo.SaveJobStateAsync(job);
    }

    private static void CleanupTempFiles(TempPaths paths, IAppLogger logger)
    {
        try
        {
            if (File.Exists(paths.VideoPath)) File.Delete(paths.VideoPath);
            if (Directory.Exists(paths.FramesDir)) Directory.Delete(paths.FramesDir, true);

            logger.Log("[Workflow] Temporary file cleanup complete.");
        }
        catch (Exception ex)
        {
            logger.Log($"[Workflow] [ERROR] Error during temporary file cleanup: {ex.Message}");
        }
    }

    private static void ConfigureSentryScope(string jobId)
    {
        SentrySdk.ConfigureScope(scope => scope.SetTag("JobId", jobId));
        SentrySdk.AddBreadcrumb("Workflow starting.", "workflow.life-cycle", level: BreadcrumbLevel.Info);
    }

    private static void LogBlockProgress(int blockIndex, int totalBlocks, IAppLogger logger)
    {
        var blockLoggerMsg = $"[Workflow] Processing block {blockIndex + 1}/{totalBlocks}";
        logger.Log(blockLoggerMsg);
        SentrySdk.AddBreadcrumb($"Processing block {blockIndex + 1}/{totalBlocks}", "video.extract", level: BreadcrumbLevel.Debug);
    }

    private static void LogBlockCompletion(int blockIndex, int totalBlocks, int totalProcessedFrames, TimeSpan processingTime, IAppLogger logger)
    {
        var completionMessage = $"[Workflow] Block {blockIndex + 1}/{totalBlocks} completed in {processingTime.TotalSeconds:F2}s. Total frames processed: {totalProcessedFrames}";
        logger.Log(completionMessage);

        SentrySdk.AddBreadcrumb(
            $"Block {blockIndex + 1}/{totalBlocks} completed in {processingTime.TotalSeconds:F2}s",
            "video.extract.complete",
            level: BreadcrumbLevel.Info
        );
    }

    private int CalculateBlockDuration(double totalDurationSeconds, int blockIndex)
    {
        var blockStart = blockIndex * config.BlockSize;
        var remainingSeconds = totalDurationSeconds - blockStart;
        return (int)Math.Min(remainingSeconds, config.BlockSize);
    }

    private TempPaths CreateTempPaths(ProcessingMessage msg)
    {
        var tempVideoPath = Path.Combine(config.TempFolder, $"{msg.JobId}_video{Path.GetExtension(msg.SourceKey)}");
        var framesDir = Path.Combine(config.TempFolder, $"{msg.JobId}_frames");

        Directory.CreateDirectory(framesDir);

        return new TempPaths(tempVideoPath, framesDir);
    }
}

public record TempPaths(string VideoPath, string FramesDir);