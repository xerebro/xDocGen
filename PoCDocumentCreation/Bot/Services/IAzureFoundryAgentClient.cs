using PoCDocumentCreation.Bot.Models;

namespace PoCDocumentCreation.Bot.Services;

public interface IAzureFoundryAgentClient
{
    Task<string> ExtractContentAsync(string fileName, byte[] fileBytes, CancellationToken cancellationToken);
    Task<string> SummarizeDocumentAsync(string fileName, string content, CancellationToken cancellationToken);
    Task<string> SummarizeDocumentsAsync(IEnumerable<DocumentRecord> documents, CancellationToken cancellationToken);
    Task<string> GenerateArchitectureDocumentAsync(IEnumerable<DocumentRecord> documents, CancellationToken cancellationToken);
}