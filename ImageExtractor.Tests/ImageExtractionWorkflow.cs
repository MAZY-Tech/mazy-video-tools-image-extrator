using ImageExtractor.Application.Interfaces;
using ImageExtractor.Application.Workflow;
using ImageExtractor.Domain;
using ImageExtractor.Infrastructure.Config;
using Moq;

namespace ImageExtractor.Tests
{
    public class ImageExtractionWorkflowTests
    {
        private readonly Mock<IJobStateRepository> _mockJobRepo;
        private readonly Mock<IVideoStorage> _mockVideoStorage;
        private readonly Mock<IVideoAnalyzer> _mockAnalyzer;
        private readonly Mock<IFrameExtractor> _mockFrameExtractor;
        private readonly Mock<IZipService> _mockZipService;
        private readonly Mock<IProgressNotifier> _mockProgressNotifier;
        private readonly Mock<ICompletionNotifier> _mockCompletionNotifier;
        private readonly Mock<IAppLogger> _mockLogger;
        private readonly ConfigProcessing _config;
        private readonly ImageExtractionWorkflow _sut;

        public ImageExtractionWorkflowTests()
        {
            _mockJobRepo = new Mock<IJobStateRepository>();
            _mockVideoStorage = new Mock<IVideoStorage>();
            _mockAnalyzer = new Mock<IVideoAnalyzer>();
            _mockFrameExtractor = new Mock<IFrameExtractor>();
            _mockZipService = new Mock<IZipService>();
            _mockProgressNotifier = new Mock<IProgressNotifier>();
            _mockCompletionNotifier = new Mock<ICompletionNotifier>();
            _mockLogger = new Mock<IAppLogger>();

            _config = new ConfigProcessing
            {
                FramesBucket = "test-frames-bucket",
                ZipBucket = "test-zip-bucket",
                TempFolder = "/tmp",
                BlockSize = 30,
                FrameRate = 1,
                FrameExtension = "jpg"
            };

            _sut = new ImageExtractionWorkflow(
                _mockJobRepo.Object,
                _mockVideoStorage.Object,
                _mockAnalyzer.Object,
                _mockFrameExtractor.Object,
                _mockZipService.Object,
                _mockProgressNotifier.Object,
                _mockCompletionNotifier.Object,
                _config
            );

            SetupBasicMocks();
        }

        #region Helper Methods
        private ProcessingMessage CreateTestMessage()
        {
            return new ProcessingMessage
            {
                JobId = "test-job-123",
                SourceBucket = "test-source-bucket",
                SourceKey = "test-video.mp4"
            };
        }

        private void SetupBasicMocks()
        {
            _mockVideoStorage.Setup(s => s.DownloadVideoAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync("fake/local/path/video.mp4");

            _mockAnalyzer.Setup(a => a.AnalyzeAsync(It.IsAny<string>()))
                .ReturnsAsync(new VideoMetadata { DurationSeconds = 150, FrameCount = 150 });

            _mockFrameExtractor.Setup(f => f.ExtractFramesAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<VideoMetadata>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<Action<int, int>?>()))
                .Returns(Task.CompletedTask);

            _mockVideoStorage.Setup(s => s.UploadFramesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string[]>()))
                .Returns(Task.CompletedTask);

            _mockZipService.Setup(z => z.CreateZipAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync("fake/zip/path.zip");
            _mockZipService.Setup(z => z.UploadZipAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(Task.CompletedTask);

            _mockJobRepo.Setup(r => r.SaveJobStateAsync(It.IsAny<JobState>()))
                .Returns(Task.CompletedTask);

            _mockProgressNotifier.Setup(n => n.NotifyProgressAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
                .Returns(Task.CompletedTask);

            _mockCompletionNotifier.Setup(n => n.NotifyCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(Task.CompletedTask);
        }
        #endregion

        #region Happy Path Tests
        [Fact]
        public async Task ExecuteAsync_WhenJobIsNew_ShouldCompleteSuccessfully()
        {
            var message = CreateTestMessage();
            var messages = new List<ProcessingMessage> { message };
            _mockJobRepo.Setup(r => r.GetJobStateAsync(message.JobId)).ReturnsAsync((JobState?)null);

            await _sut.ExecuteAsync(messages, _mockLogger.Object);

            _mockJobRepo.Verify(r => r.SaveJobStateAsync(It.Is<JobState>(j => j.Status == JobStatusEnum.Completed)), Times.AtLeastOnce);
            _mockCompletionNotifier.Verify(n => n.NotifyCompletionAsync(message.JobId, _config.ZipBucket, It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task ExecuteAsync_WhenJobAlreadyCompleted_ShouldSkipProcessing()
        {
            var message = CreateTestMessage();
            var messages = new List<ProcessingMessage> { message };
            var completedJob = new JobState { JobId = message.JobId, Status = JobStatusEnum.Completed };
            _mockJobRepo.Setup(r => r.GetJobStateAsync(message.JobId)).ReturnsAsync(completedJob);

            await _sut.ExecuteAsync(messages, _mockLogger.Object);

            _mockVideoStorage.Verify(s => s.DownloadVideoAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            _mockFrameExtractor.Verify(f => f.ExtractFramesAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<VideoMetadata>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<Action<int, int>?>()), Times.Never);
        }

        [Fact]
        public async Task ExecuteAsync_WhenJobIsInterrupted_ShouldResumeFromCorrectStep()
        {
            var message = CreateTestMessage();
            var messages = new List<ProcessingMessage> { message };
            var interruptedJob = new JobState
            {
                JobId = message.JobId,
                Status = JobStatusEnum.Interrupted,
                CurrentStep = ProcessingStepEnum.Extracting,
                TotalBlocks = 5,
                CurrentBlock = 2 // Já processou 2 blocos
            };
            _mockJobRepo.Setup(r => r.GetJobStateAsync(message.JobId)).ReturnsAsync(interruptedJob);

            await _sut.ExecuteAsync(messages, _mockLogger.Object);

            _mockVideoStorage.Verify(s => s.DownloadVideoAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            _mockAnalyzer.Verify(a => a.AnalyzeAsync(It.IsAny<string>()), Times.Never);

            // Verifica se a extração foi chamada o número correto de vezes para os blocos restantes
            _mockFrameExtractor.Verify(f => f.ExtractFramesAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<VideoMetadata>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<Action<int, int>?>()), Times.Exactly(3));
        }
        #endregion

        #region Error Handling Tests
        [Fact]
        public async Task ExecuteAsync_WhenFrameExtractionFails_ShouldMarkJobAsFailedAndThrow()
        {
            var message = CreateTestMessage();
            var messages = new List<ProcessingMessage> { message };
            _mockJobRepo.Setup(r => r.GetJobStateAsync(message.JobId)).ReturnsAsync((JobState?)null);

            _mockFrameExtractor.Setup(f => f.ExtractFramesAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<VideoMetadata>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<Action<int, int>?>()))
                .ThrowsAsync(new Exception("Frame extraction failed"));

            await Assert.ThrowsAsync<Exception>(() => _sut.ExecuteAsync(messages, _mockLogger.Object));

            // Verifica que o estado foi salvo pelo menos uma vez com status Failed
            _mockJobRepo.Verify(r => r.SaveJobStateAsync(It.Is<JobState>(j => j.Status == JobStatusEnum.Failed)), Times.AtLeastOnce);
            // Verifica que SaveJobStateAsync foi chamado múltiplas vezes (incluindo estados intermediários)
            _mockJobRepo.Verify(r => r.SaveJobStateAsync(It.IsAny<JobState>()), Times.AtLeast(2));
        }

        [Fact]
        public async Task ExecuteAsync_WhenVideoDownloadFails_ShouldMarkJobAsFailedAndThrow()
        {
            var message = CreateTestMessage();
            var messages = new List<ProcessingMessage> { message };
            _mockJobRepo.Setup(r => r.GetJobStateAsync(message.JobId)).ReturnsAsync((JobState?)null);

            _mockVideoStorage.Setup(s => s.DownloadVideoAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new Exception("Video download failed"));

            await Assert.ThrowsAsync<Exception>(() => _sut.ExecuteAsync(messages, _mockLogger.Object));

            // Verifica que o estado foi salvo pelo menos uma vez com status Failed
            _mockJobRepo.Verify(r => r.SaveJobStateAsync(It.Is<JobState>(j => j.Status == JobStatusEnum.Failed)), Times.AtLeastOnce);
            // Verifica que SaveJobStateAsync foi chamado (incluindo estado inicial e failed)
            _mockJobRepo.Verify(r => r.SaveJobStateAsync(It.IsAny<JobState>()), Times.AtLeast(1));
        }

        [Fact]
        public async Task ExecuteAsync_WhenZipCreationFails_ShouldMarkJobAsFailedAndThrow()
        {
            var message = CreateTestMessage();
            var messages = new List<ProcessingMessage> { message };
            _mockJobRepo.Setup(r => r.GetJobStateAsync(message.JobId)).ReturnsAsync((JobState?)null);

            _mockZipService.Setup(z => z.CreateZipAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new Exception("Zip creation failed"));

            await Assert.ThrowsAsync<Exception>(() => _sut.ExecuteAsync(messages, _mockLogger.Object));

            // Verifica que o estado foi salvo pelo menos uma vez com status Failed
            _mockJobRepo.Verify(r => r.SaveJobStateAsync(It.Is<JobState>(j => j.Status == JobStatusEnum.Failed)), Times.AtLeastOnce);
            // Verifica que SaveJobStateAsync foi chamado múltiplas vezes (incluindo estados de progresso)
            _mockJobRepo.Verify(r => r.SaveJobStateAsync(It.IsAny<JobState>()), Times.AtLeast(2));
        }

        [Fact]
        public async Task ExecuteAsync_WhenExceptionOccurs_ShouldEnsureJobEndsInFailedState()
        {
            var message = CreateTestMessage();
            var messages = new List<ProcessingMessage> { message };
            _mockJobRepo.Setup(r => r.GetJobStateAsync(message.JobId)).ReturnsAsync((JobState?)null);

            _mockAnalyzer.Setup(a => a.AnalyzeAsync(It.IsAny<string>()))
                .ThrowsAsync(new Exception("Analysis failed"));

            await Assert.ThrowsAsync<Exception>(() => _sut.ExecuteAsync(messages, _mockLogger.Object));

            // Verifica que pelo menos uma chamada foi feita com status Failed
            _mockJobRepo.Verify(r => r.SaveJobStateAsync(It.Is<JobState>(j => j.Status == JobStatusEnum.Failed)), Times.AtLeastOnce);
        }
        #endregion

        #region Progress Notification Tests
        [Fact]
        public async Task ExecuteAsync_ShouldNotifyProgressDuringExecution()
        {
            var message = CreateTestMessage();
            var messages = new List<ProcessingMessage> { message };
            _mockJobRepo.Setup(r => r.GetJobStateAsync(message.JobId)).ReturnsAsync((JobState?)null);

            await _sut.ExecuteAsync(messages, _mockLogger.Object);

            _mockProgressNotifier.Verify(n => n.NotifyProgressAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<int>()), Times.AtLeastOnce);
        }
        #endregion

        #region State Management Tests
        [Fact]
        public async Task ExecuteAsync_ShouldSaveJobStateAtEachStep()
        {
            var message = CreateTestMessage();
            var messages = new List<ProcessingMessage> { message };
            _mockJobRepo.Setup(r => r.GetJobStateAsync(message.JobId)).ReturnsAsync((JobState?)null);

            await _sut.ExecuteAsync(messages, _mockLogger.Object);

            // Verifica se o estado foi salvo múltiplas vezes durante o processamento
            _mockJobRepo.Verify(r => r.SaveJobStateAsync(It.IsAny<JobState>()), Times.AtLeast(2));
        }
        #endregion
    }
}