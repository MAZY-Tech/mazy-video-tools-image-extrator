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
        LogJobStart(msg.JobId, logger);

        var job = await GetOrCreateJobAsync(msg.JobId);

        if (ShouldSkipJob(job, msg.JobId, logger))
            return;

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

    private async Task<JobState> GetOrCreateJobAsync(string jobId)
    {
        return await jobRepo.GetJobStateAsync(jobId) ?? new JobState { JobId = jobId };
    }

    private bool ShouldSkipJob(JobState job, string jobId, IAppLogger logger)
    {
        if (job.Status == JobStatusEnum.Completed)
        {
            logger.Log($"Job '{jobId}' já está completo. Pulando.");
            return true;
        }

        if (job.Status == JobStatusEnum.Running || job.Status == JobStatusEnum.Interrupted)
        {
            logger.Log($"Retomando job '{jobId}' a partir da etapa '{job.CurrentStep}'.");
        }
        else
        {
            job.Status = JobStatusEnum.Pending;
            job.CurrentStep = ProcessingStepEnum.Validating;
        }

        return false;
    }

    private async Task EnsureJobStartedAsync(JobState job)
    {
        if (job.StartedAt == null)
            job.StartedAt = DateTime.UtcNow;

        job.Status = JobStatusEnum.Running;
        await jobRepo.SaveJobStateAsync(job);
    }

    private async Task ExecuteDownloadStepAsync(ProcessingMessage msg, JobState job, string tempVideoPath, IAppLogger logger)
    {
        if (job.CurrentStep > ProcessingStepEnum.Downloading)
            return;

        job.CurrentStep = ProcessingStepEnum.Downloading;
        logger.Log($"Baixando vídeo: s3://{msg.SourceBucket}/{msg.SourceKey}");

        await videoStorage.DownloadVideoAsync(msg.SourceBucket, msg.SourceKey, tempVideoPath);
        await jobRepo.SaveJobStateAsync(job);
    }

    private async Task<VideoMetadata> ExecuteAnalysisStepAsync(JobState job, string tempVideoPath, IAppLogger logger)
    {
        if (job.CurrentStep > ProcessingStepEnum.Analyzing)
        {
            return new VideoMetadata
            {
                FrameCount = job.TotalFrames,
                DurationSeconds = job.TotalBlocks * config.BlockSize
            };
        }

        job.CurrentStep = ProcessingStepEnum.Analyzing;
        var metadata = await analyzer.AnalyzeAsync(tempVideoPath);

        job.TotalFrames = metadata.FrameCount;
        job.TotalBlocks = (int)Math.Ceiling(metadata.DurationSeconds / config.BlockSize);

        logger.Log($"Análise concluída. Duração: {metadata.DurationSeconds:F2}s");
        await jobRepo.SaveJobStateAsync(job);

        return metadata;
    }

    private async Task ExecuteExtractionStepAsync(ProcessingMessage msg, JobState job, TempPaths paths, VideoMetadata metadata, IAppLogger logger)
    {
        if (job.CurrentStep > ProcessingStepEnum.Extracting)
            return;

        job.CurrentStep = ProcessingStepEnum.Extracting;
        await ExtractFramesInBlocksAsync(msg, job, paths.VideoPath, paths.FramesDir, metadata, logger);
    }

    private async Task<string> ExecuteZippingStepAsync(ProcessingMessage msg, JobState job, TempPaths paths, IAppLogger logger)
    {
        if (job.CurrentStep > ProcessingStepEnum.Zipping)
        {
            return job.Metadata["ZipKey"]?.ToString() ?? "";
        }

        job.CurrentStep = ProcessingStepEnum.Zipping;
        logger.Log("Criando arquivo ZIP...");

        var zipPath = Path.Combine(config.TempFolder, $"{msg.JobId}.zip");
        await zipService.CreateZipAsync(paths.FramesDir, zipPath);

        var zipKey = $"{msg.JobId}/{Path.GetFileName(zipPath)}";
        await zipService.UploadZipAsync(config.ZipBucket, zipKey, zipPath);

        logger.Log($"Arquivo ZIP enviado para s3://{config.ZipBucket}/{zipKey}");
        await jobRepo.SaveJobStateAsync(job);

        return zipKey;
    }

    private async Task ExtractFramesInBlocksAsync(ProcessingMessage msg, JobState job, string videoPath, string framesDir, VideoMetadata metadata, IAppLogger logger)
    {
        logger.Log($"Iniciando extração em {job.TotalBlocks} blocos. Começando do bloco {job.CurrentBlock + 1}.");

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

        await frameExtractor.ExtractFramesAsync(videoPath, framesDir, metadata, config.FrameRate, blockDuration, null);

        await ProcessExtractedFramesAsync(msg, job, framesDir, blockIndex);
        await UpdateBlockProgress(job, blockIndex, blockDuration);
        await NotifyBlockProgress(msg.JobId, job);
    }

    private async Task ProcessExtractedFramesAsync(ProcessingMessage msg, JobState job, string framesDir, int blockIndex)
    {
        var framePaths = Directory.GetFiles(framesDir, $"*.{config.FrameExtension}");
        if (framePaths.Length == 0) return;

        await videoStorage.UploadFramesAsync(
            bucket: config.FramesBucket,
            prefix: $"{msg.JobId}/block_{blockIndex + 1}",
            framePaths
        );

        job.ProcessedFrames += framePaths.Length;

        // Limpa frames do bloco atual
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

    private async Task NotifyBlockProgress(string jobId, JobState job)
    {
        var progressPct = (int)((double)job.CurrentBlock / job.TotalBlocks * 100);
        await progressNotifier.NotifyProgressAsync(jobId, progressPct, job.CurrentBlock, job.TotalBlocks);
    }

    private async Task CompleteJobAsync(JobState job, string jobId, IAppLogger logger)
    {
        job.Status = JobStatusEnum.Completed;
        job.CurrentStep = ProcessingStepEnum.Done;
        job.CompletedAt = DateTime.UtcNow;
        job.LastHeartbeat = DateTime.UtcNow;

        await jobRepo.SaveJobStateAsync(job);

        var zipBucket = job.Metadata["ZipBucket"].ToString();
        var zipKey = job.Metadata["ZipKey"].ToString();

        await completionNotifier.NotifyCompletionAsync(jobId, zipBucket!, zipKey!);
        logger.Log($"Processo concluído com sucesso para o Job '{jobId}'.");
    }

    private async Task HandleJobFailureAsync(JobState job, ProcessingMessage msg, Exception ex, IAppLogger logger)
    {
        logger.Log($"Falha no processamento do Job '{msg.JobId}': {ex.Message}");
        SentrySdk.CaptureException(ex, scope => scope.SetExtra("ProcessingMessage", msg));

        job.Status = JobStatusEnum.Failed;
        job.LastHeartbeat = DateTime.UtcNow;
        await jobRepo.SaveJobStateAsync(job);
    }

    private void CleanupTempFiles(TempPaths paths, IAppLogger logger)
    {
        try
        {
            if (File.Exists(paths.VideoPath))
                File.Delete(paths.VideoPath);

            if (Directory.Exists(paths.FramesDir))
                Directory.Delete(paths.FramesDir, true);

            logger.Log("Limpeza de arquivos temporários concluída.");
        }
        catch (Exception ex)
        {
            logger.Log($"Erro na limpeza de arquivos temporários: {ex.Message}");
        }
    }

    private static void ConfigureSentryScope(string jobId)
    {
        SentrySdk.ConfigureScope(scope => scope.SetTag("JobId", jobId));
        SentrySdk.AddBreadcrumb("Workflow iniciado.", "workflow.life-cycle", level: BreadcrumbLevel.Info);
    }

    private static void LogJobStart(string jobId, IAppLogger logger)
    {
        logger.Log($"Iniciando processamento para o job '{jobId}'");
    }

    private static void LogBlockProgress(int blockIndex, int totalBlocks, IAppLogger logger)
    {
        var blockLoggerMsg = $"Processando bloco {blockIndex + 1}/{totalBlocks}";
        logger.Log(blockLoggerMsg);
        SentrySdk.AddBreadcrumb(blockLoggerMsg, "video.extract", level: BreadcrumbLevel.Debug);
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

    public void ExecuteAsync(IEnumerable<IMessageParser> enumerable, IAppLogger appLogger)
    {
        throw new NotImplementedException();
    }
}

public record TempPaths(string VideoPath, string FramesDir);