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
            _mockVideoStorage.Setup(s => s.DownloadVideoAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), _mockLogger.Object))
                .ReturnsAsync("fake/local/path/video.mp4");

            _mockVideoStorage.Setup(s => s.DownloadAllFramesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), _mockLogger.Object))
                .Returns(Task.CompletedTask);

            _mockAnalyzer.Setup(a => a.AnalyzeAsync(It.IsAny<string>(), _mockLogger.Object))
                .ReturnsAsync(new VideoMetadata { DurationSeconds = 150, FrameCount = 150 });

            _mockFrameExtractor.Setup(f => f.ExtractFramesAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                _mockLogger.Object))
                .Returns(Task.CompletedTask);

            _mockVideoStorage.Setup(s => s.UploadFramesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string[]>(), _mockLogger.Object))
                .Returns(Task.CompletedTask);

            _mockZipService.Setup(z => z.CreateZipAsync(It.IsAny<string>(), It.IsAny<string>(), _mockLogger.Object))
                .ReturnsAsync("fake/zip/path.zip");
            _mockZipService.Setup(z => z.UploadZipAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), _mockLogger.Object))
                .Returns(Task.CompletedTask);

            _mockJobRepo.Setup(r => r.SaveJobStateAsync(It.IsAny<JobState>()))
                .Returns(Task.CompletedTask);

            _mockProgressNotifier.Setup(n => n.NotifyProgressAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), _mockLogger.Object))
                .Returns(Task.CompletedTask);

            _mockCompletionNotifier.Setup(n => n.NotifyCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), _mockLogger.Object))
                .Returns(Task.CompletedTask);
        }

        [Fact]
        public async Task ExecuteAsync_WhenJobIsNew_ShouldCompleteSuccessfully()
        {
            var message = CreateTestMessage();
            var messages = new List<ProcessingMessage> { message };
            _mockJobRepo.Setup(r => r.GetJobStateAsync(message.JobId)).ReturnsAsync((JobState?)null);

            await _sut.ExecuteAsync(messages, _mockLogger.Object);

            _mockVideoStorage.Verify(s => s.DownloadAllFramesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), _mockLogger.Object), Times.Once);
            _mockJobRepo.Verify(r => r.SaveJobStateAsync(It.Is<JobState>(j => j.Status == JobStatus.Completed)), Times.AtLeastOnce);
            _mockCompletionNotifier.Verify(n => n.NotifyCompletionAsync(message.JobId, _config.ZipBucket, It.IsAny<string>(), _mockLogger.Object), Times.Once);
        }

        [Fact]
        public async Task ExecuteAsync_WhenJobAlreadyCompleted_ShouldSkipProcessing()
        {
            var message = CreateTestMessage();
            var messages = new List<ProcessingMessage> { message };
            var completedJob = new JobState { JobId = message.JobId, Status = JobStatus.Completed };
            _mockJobRepo.Setup(r => r.GetJobStateAsync(message.JobId)).ReturnsAsync(completedJob);

            await _sut.ExecuteAsync(messages, _mockLogger.Object);

            _mockVideoStorage.Verify(s => s.DownloadVideoAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), _mockLogger.Object), Times.Never);
            _mockFrameExtractor.Verify(f => f.ExtractFramesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<int>(), It.IsAny<int>(), _mockLogger.Object), Times.Never);
        }

        [Fact]
        public async Task ExecuteAsync_WhenFrameExtractionFails_ShouldMarkJobAsFailedAndThrow()
        {
            var message = CreateTestMessage();
            var messages = new List<ProcessingMessage> { message };
            _mockJobRepo.Setup(r => r.GetJobStateAsync(message.JobId)).ReturnsAsync((JobState?)null);

            _mockFrameExtractor.Setup(f => f.ExtractFramesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<int>(), It.IsAny<int>(), _mockLogger.Object))
                .ThrowsAsync(new Exception("Frame extraction failed"));

            await Assert.ThrowsAsync<Exception>(() => _sut.ExecuteAsync(messages, _mockLogger.Object));

            _mockJobRepo.Verify(r => r.SaveJobStateAsync(It.Is<JobState>(j => j.Status == JobStatus.Failed)), Times.AtLeastOnce);
            _mockJobRepo.Verify(r => r.SaveJobStateAsync(It.IsAny<JobState>()), Times.AtLeast(2));
        }

        [Fact]
        public async Task ExecuteAsync_WhenZipCreationFails_ShouldMarkJobAsFailedAndThrow()
        {
            var message = CreateTestMessage();
            var messages = new List<ProcessingMessage> { message };
            _mockJobRepo.Setup(r => r.GetJobStateAsync(message.JobId)).ReturnsAsync((JobState?)null);

            _mockZipService.Setup(z => z.CreateZipAsync(It.IsAny<string>(), It.IsAny<string>(), _mockLogger.Object))
                .ThrowsAsync(new Exception("Zip creation failed"));

            await Assert.ThrowsAsync<Exception>(() => _sut.ExecuteAsync(messages, _mockLogger.Object));

            _mockJobRepo.Verify(r => r.SaveJobStateAsync(It.Is<JobState>(j => j.Status == JobStatus.Failed)), Times.AtLeastOnce);
        }

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
                It.IsAny<int>(),
                _mockLogger.Object), Times.AtLeastOnce);
        }

        [Fact]
        public async Task ExecuteAsync_ShouldSaveJobStateAtEachStep()
        {
            var message = CreateTestMessage();
            var messages = new List<ProcessingMessage> { message };
            _mockJobRepo.Setup(r => r.GetJobStateAsync(message.JobId)).ReturnsAsync((JobState?)null);

            await _sut.ExecuteAsync(messages, _mockLogger.Object);

            _mockJobRepo.Verify(r => r.SaveJobStateAsync(It.IsAny<JobState>()), Times.AtLeast(2));
        }
    }
}