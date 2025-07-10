using ImageExtractor.Domain;
using ImageExtractor.Infrastructure.Repositories;
using MongoDB.Driver;
using Moq;

namespace ImageExtractor.Tests;

public class MongoJobStateRepositoryTests
{
    private readonly Mock<IMongoCollection<JobStateDocument>> _mockCollection;
    private readonly Mock<IAsyncCursor<JobStateDocument>> _mockCursor;
    private readonly MongoJobStateRepository _repository;

    public MongoJobStateRepositoryTests()
    {
        _mockCollection = new Mock<IMongoCollection<JobStateDocument>>();
        _mockCursor = new Mock<IAsyncCursor<JobStateDocument>>();

        _repository = new MongoJobStateRepository(_mockCollection.Object);
    }

    [Fact]
    public async Task GetJobStateAsync_WhenDocumentExists_ReturnsMappedJobState()
    {
        var jobId = "existing-job-123";
        var expectedDocument = new JobStateDocument { JobId = jobId, Status = "Running", CurrentStep = "Validating" };
        var documents = new List<JobStateDocument> { expectedDocument };

        var mockCursor = new Mock<IAsyncCursor<JobStateDocument>>();
        mockCursor.Setup(_ => _.Current).Returns(documents);
        mockCursor.SetupSequence(_ => _.MoveNext(It.IsAny<CancellationToken>())).Returns(true).Returns(false);
        mockCursor.SetupSequence(_ => _.MoveNextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true).ReturnsAsync(false);

        _mockCollection.Setup(c => c.FindAsync(
            It.IsAny<FilterDefinition<JobStateDocument>>(),
            It.IsAny<FindOptions<JobStateDocument, JobStateDocument>>(),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(mockCursor.Object);

        var result = await _repository.GetJobStateAsync(jobId);

        Assert.NotNull(result);
        Assert.Equal(jobId, result.JobId);
        Assert.Equal(JobStatus.Running, result.Status);
    }

    [Fact]
    public async Task GetJobStateAsync_WhenDocumentDoesNotExist_ReturnsNull()
    {
        var jobId = "non-existing-job";
        var emptyDocuments = new List<JobStateDocument>();

        _mockCursor.Setup(_ => _.Current).Returns(emptyDocuments);
        _mockCursor.Setup(_ => _.MoveNextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);

        _mockCollection.Setup(c => c.FindAsync(
            It.IsAny<FilterDefinition<JobStateDocument>>(),
            It.IsAny<FindOptions<JobStateDocument, JobStateDocument>>(),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(_mockCursor.Object);

        var result = await _repository.GetJobStateAsync(jobId);

        Assert.Null(result);
    }

    [Fact]
    public async Task SaveJobStateAsync_CallsReplaceOneAsync_WithUpsertOption()
    {
        var jobState = new JobState
        {
            JobId = "job-to-save",
            Status = JobStatus.Completed,
            CurrentStep = ProcessingStep.Zipping,
            Progress = 100
        };

        await _repository.SaveJobStateAsync(jobState);

        _mockCollection.Verify(
            c => c.ReplaceOneAsync(
                It.IsAny<FilterDefinition<JobStateDocument>>(),
                It.Is<JobStateDocument>(d => d.JobId == jobState.JobId && d.Status == "Completed"),
                It.Is<ReplaceOptions>(opt => opt.IsUpsert == true),
                It.IsAny<CancellationToken>()
            ),
            Times.Once
        );
    }
}