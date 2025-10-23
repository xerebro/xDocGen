namespace PoCDocumentCreation.Bot.Models;

public class DocumentSessionState
{
    public List<DocumentRecord> Documents { get; } = new();
    public DocumentSessionStatus Status { get; set; } = DocumentSessionStatus.AwaitingDocuments;
}

public class DocumentRecord
{
    public string FileName { get; set; } = string.Empty;
    public string ExtractedContent { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
}

public enum DocumentSessionStatus
{
    AwaitingDocuments,
    ReadyForDecision,
    ArchitectureGenerated
}