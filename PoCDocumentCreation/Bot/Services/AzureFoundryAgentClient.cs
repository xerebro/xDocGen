using PoCDocumentCreation.Bot.Models;

namespace PoCDocumentCreation.Bot.Services;

public class AzureFoundryAgentClient : IAzureFoundryAgentClient
{
    private readonly ILogger<AzureFoundryAgentClient> _logger;

    public AzureFoundryAgentClient(ILogger<AzureFoundryAgentClient> logger)
    {
        _logger = logger;
    }

    public Task<string> ExtractContentAsync(string fileName, byte[] fileBytes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            string content = DocumentContentExtractor.ExtractContent(fileName, fileBytes);
            return Task.FromResult(content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract content from {FileName}", fileName);
            throw;
        }
    }

    public Task<string> SummarizeDocumentAsync(string fileName, string content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            string summary = DocumentSummarizer.SummarizeDocument(content);
            if (string.IsNullOrWhiteSpace(summary))
            {
                summary = $"- No significant insights were detected in {fileName}.";
            }

            return Task.FromResult(summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to summarize document {FileName}", fileName);
            throw;
        }
    }

    public Task<string> SummarizeDocumentsAsync(IEnumerable<DocumentRecord> documents, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            string summary = DocumentSummarizer.SummarizeDocuments(documents);
            return Task.FromResult(summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to summarize combined documents");
            throw;
        }
    }

    public Task<string> GenerateArchitectureDocumentAsync(IEnumerable<DocumentRecord> documents, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            string architecture = ArchitectureDocumentGenerator.Generate(documents);
            return Task.FromResult(architecture);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate architecture document");
            throw;
        }
    }
}
