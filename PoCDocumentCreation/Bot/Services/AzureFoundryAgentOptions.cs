namespace PoCDocumentCreation.Bot.Services;

public class AzureFoundryAgentOptions
{
    public string? Endpoint { get; set; }
    public string? ProjectName { get; set; }
    public string? AgentId { get; set; }
    public string? ApiKey { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Endpoint) &&
        !string.IsNullOrWhiteSpace(AgentId) &&
        !string.IsNullOrWhiteSpace(ApiKey);
}