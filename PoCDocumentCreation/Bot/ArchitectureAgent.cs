using Microsoft.Agents.Builder;
using Microsoft.Agents.Builder.App;
using Microsoft.Agents.Builder.State;
using Microsoft.Agents.Core.Models;
using PoCDocumentCreation.Bot.Models;
using PoCDocumentCreation.Bot.Services;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace PoCDocumentCreation.Bot;

public class ArchitectureAgent : AgentApplication
{
    private static readonly HashSet<string> SupportedContentTypes =
    [
        "application/pdf",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "image/jpeg",
        "image/jpg",
        "image/png",
        "application/vnd.microsoft.teams.file.download.info"
    ];

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAzureFoundryAgentClient _foundryAgentClient;
    private readonly IDocumentSessionStore _sessionStore;
    private readonly ILogger<ArchitectureAgent> _logger;

    public ArchitectureAgent(
        AgentApplicationOptions options,
        IHttpClientFactory httpClientFactory,
        IAzureFoundryAgentClient foundryAgentClient,
        IDocumentSessionStore sessionStore,
        ILogger<ArchitectureAgent> logger) : base(options)
    {
        _httpClientFactory = httpClientFactory;
        _foundryAgentClient = foundryAgentClient;
        _sessionStore = sessionStore;
        _logger = logger;

        OnConversationUpdate(ConversationUpdateEvents.MembersAdded, WelcomeMessageAsync);
        OnActivity(ActivityTypes.Message, OnMessageAsync);
    }

    private async Task WelcomeMessageAsync(ITurnContext turnContext, ITurnState turnState, CancellationToken cancellationToken)
    {
        foreach (ChannelAccount member in turnContext.Activity.MembersAdded)
        {
            if (member.Id == turnContext.Activity.Recipient.Id)
            {
                continue;
            }

            string welcomeText = "Hello! Upload one or more solution documents (PDF, Word, JPG) and I will summarize them. Once you are ready, ask me to generate the draft architecture document.";
            await turnContext.SendActivityAsync(MessageFactory.Text(welcomeText), cancellationToken);
        }
    }

    private async Task OnMessageAsync(ITurnContext turnContext, ITurnState turnState, CancellationToken cancellationToken)
    {
        if (turnContext.Activity.Attachments?.Count > 0)
        {
            await HandleAttachmentsAsync(turnContext, cancellationToken);
            return;
        }

        string text = (turnContext.Activity.Text ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(text))
        {
            await turnContext.SendActivityAsync(MessageFactory.Text("I did not receive any text. Upload a document or tell me what to do next."), cancellationToken);
            return;
        }

        string conversationId = turnContext.Activity.Conversation?.Id ?? "";
        DocumentSessionState session = _sessionStore.GetOrCreate(conversationId);

        if (text.Equals("generate", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("generate", StringComparison.OrdinalIgnoreCase) && text.Contains("architecture", StringComparison.OrdinalIgnoreCase))
        {
            if (!session.Documents.Any())
            {
                await turnContext.SendActivityAsync(MessageFactory.Text("I need at least one processed document before drafting the architecture."), cancellationToken);
                return;
            }

            string architectureDocument;
            try
            {
                architectureDocument = await _foundryAgentClient.GenerateArchitectureDocumentAsync(session.Documents, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate architecture document");
                await turnContext.SendActivityAsync(MessageFactory.Text("I could not generate the architecture document because the Azure Foundry agent request failed."), cancellationToken);
                return;
            }

            session.Status = DocumentSessionStatus.ArchitectureGenerated;
            await turnContext.SendActivityAsync(MessageFactory.Text(architectureDocument), cancellationToken);
            return;
        }

        if (text.Contains("upload", StringComparison.OrdinalIgnoreCase) || text.Contains("more", StringComparison.OrdinalIgnoreCase))
        {
            await turnContext.SendActivityAsync(MessageFactory.Text("Please upload the next document whenever you are ready."), cancellationToken);
            session.Status = DocumentSessionStatus.AwaitingDocuments;
            return;
        }

        await turnContext.SendActivityAsync(MessageFactory.Text("Upload a document for analysis or ask me to generate the architecture draft when you are ready."), cancellationToken);
    }

    private async Task HandleAttachmentsAsync(ITurnContext turnContext, CancellationToken cancellationToken)
    {
        var attachments = turnContext.Activity.Attachments ?? new List<Attachment>();
        if (!attachments.Any())
        {
            await turnContext.SendActivityAsync(MessageFactory.Text("I could not find any attachments in your message."), cancellationToken);
            return;
        }

        string conversationId = turnContext.Activity.Conversation?.Id ?? "";
        DocumentSessionState session = _sessionStore.GetOrCreate(conversationId);
        var processedDocuments = new List<DocumentRecord>();

        foreach (Attachment attachment in attachments)
        {
            string? fileName = attachment.Name;
            if (!IsSupportedAttachment(attachment))
            {
                await turnContext.SendActivityAsync(MessageFactory.Text($"{fileName ?? "This attachment"} is not a supported file type."), cancellationToken);
                continue;
            }

            byte[]? data = await DownloadAttachmentAsync(attachment, cancellationToken);
            if (data == null || data.Length == 0)
            {
                await turnContext.SendActivityAsync(MessageFactory.Text($"I could not download {fileName ?? "the attachment"}."), cancellationToken);
                continue;
            }

            string extractedContent;
            try
            {
                extractedContent = await _foundryAgentClient.ExtractContentAsync(fileName ?? "document", data, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to extract document content");
                await turnContext.SendActivityAsync(MessageFactory.Text($"I could not process {fileName ?? "the attachment"} due to an Azure Foundry agent error."), cancellationToken);
                continue;
            }

            string summary;
            try
            {
                summary = await _foundryAgentClient.SummarizeDocumentAsync(fileName ?? "document", extractedContent, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to summarize document");
                await turnContext.SendActivityAsync(MessageFactory.Text($"I extracted content from {fileName ?? "the attachment"} but could not summarize it."), cancellationToken);
                summary = string.Empty;
            }

            var record = new DocumentRecord
            {
                FileName = fileName ?? "document",
                ExtractedContent = extractedContent,
                Summary = summary
            };
            session.Documents.Add(record);
            processedDocuments.Add(record);
        }

        if (!processedDocuments.Any())
        {
            await turnContext.SendActivityAsync(MessageFactory.Text("I was not able to process any of the attachments."), cancellationToken);
            return;
        }

        string combinedSummary;
        try
        {
            combinedSummary = await _foundryAgentClient.SummarizeDocumentsAsync(session.Documents, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to summarize combined documents");
            combinedSummary = BuildFallbackSummary(processedDocuments);
        }

        session.Status = DocumentSessionStatus.ReadyForDecision;
        string message = combinedSummary + "\n\nWould you like to upload more documents or should I generate the draft architecture document?";
        await turnContext.SendActivityAsync(MessageFactory.Text(message), cancellationToken);
    }

    private string BuildFallbackSummary(IEnumerable<DocumentRecord> documents)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Here is what I extracted:");
        foreach (DocumentRecord document in documents)
        {
            builder.AppendLine($"• {document.FileName}");
        }

        return builder.ToString();
    }

    private bool IsSupportedAttachment(Attachment attachment)
    {
        if (attachment == null)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(attachment.ContentType) && SupportedContentTypes.Contains(attachment.ContentType))
        {
            return true;
        }

        string extension = Path.GetExtension(attachment.Name ?? string.Empty).TrimStart('.');
        return extension.Equals("pdf", StringComparison.OrdinalIgnoreCase)
               || extension.Equals("doc", StringComparison.OrdinalIgnoreCase)
               || extension.Equals("docx", StringComparison.OrdinalIgnoreCase)
               || extension.Equals("jpg", StringComparison.OrdinalIgnoreCase)
               || extension.Equals("jpeg", StringComparison.OrdinalIgnoreCase)
               || extension.Equals("png", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<byte[]?> DownloadAttachmentAsync(Attachment attachment, CancellationToken cancellationToken)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(attachment.ContentUrl))
            {
                HttpClient client = _httpClientFactory.CreateClient("WebClient");
                return await client.GetByteArrayAsync(attachment.ContentUrl, cancellationToken);
            }

            TeamsFileDownloadInfo? info = TryParseDownloadInfo(attachment);
            if (info == null || string.IsNullOrWhiteSpace(info.DownloadUrl))
            {
                return null;
            }

            HttpClient downloadClient = _httpClientFactory.CreateClient("WebClient");
            using HttpRequestMessage request = new(HttpMethod.Get, info.DownloadUrl);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
            using HttpResponseMessage response = await downloadClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsByteArrayAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download attachment");
            return null;
        }
    }

    private static TeamsFileDownloadInfo? TryParseDownloadInfo(Attachment attachment)
    {
        if (attachment.Content is TeamsFileDownloadInfo info)
        {
            return info;
        }

        if (attachment.Content is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Object)
        {
            return JsonSerializer.Deserialize<TeamsFileDownloadInfo>(jsonElement.GetRawText());
        }

        if (attachment.Content != null)
        {
            string contentString = attachment.Content.ToString() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(contentString))
            {
                try
                {
                    return JsonSerializer.Deserialize<TeamsFileDownloadInfo>(contentString);
                }
                catch
                {
                    return null;
                }
            }
        }

        return null;
    }

    private sealed record TeamsFileDownloadInfo
    {
        public string? DownloadUrl { get; init; }
        public string? UniqueId { get; init; }
        public string? FileType { get; init; }
    }
}
