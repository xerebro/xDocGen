using System.Text;
using System.Text.Json;
using System.Linq;
using Microsoft.Extensions.Options;
using PoCDocumentCreation.Bot.Models;

namespace PoCDocumentCreation.Bot.Services;

public class AzureFoundryAgentClient : IAzureFoundryAgentClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AzureFoundryAgentOptions _options;
    private readonly ILogger<AzureFoundryAgentClient> _logger;

    public AzureFoundryAgentClient(IHttpClientFactory httpClientFactory, IOptions<AzureFoundryAgentOptions> options, ILogger<AzureFoundryAgentClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> ExtractContentAsync(string fileName, byte[] fileBytes, CancellationToken cancellationToken)
    {
        var payload = new AgentRequest
        {
            Instructions = "Extract the raw textual content from the provided file. Preserve the order of the content and omit explanations.",
            Input = $"Document name: {fileName}",
            Files =
            [
                new AgentFilePayload
                {
                    FileName = fileName,
                    Base64Content = Convert.ToBase64String(fileBytes)
                }
            ]
        };

        return await SendAgentRequestAsync(payload, cancellationToken);
    }

    public async Task<string> SummarizeDocumentAsync(string fileName, string content, CancellationToken cancellationToken)
    {
        var payload = new AgentRequest
        {
            Instructions = "Summarize the document content in no more than five bullet points, focusing on architecture-relevant details.",
            Input = $"Document name: {fileName}\n\nContent:\n{content}"
        };

        return await SendAgentRequestAsync(payload, cancellationToken);
    }

    public async Task<string> SummarizeDocumentsAsync(IEnumerable<DocumentRecord> documents, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        foreach (DocumentRecord document in documents)
        {
            builder.AppendLine($"Document: {document.FileName}");
            builder.AppendLine(document.Summary);
            builder.AppendLine();
        }

        var payload = new AgentRequest
        {
            Instructions = "Provide a cohesive summary of the uploaded documents, highlighting overlaps, conflicts, and missing information.",
            Input = builder.ToString()
        };

        return await SendAgentRequestAsync(payload, cancellationToken);
    }

    public async Task<string> GenerateArchitectureDocumentAsync(IEnumerable<DocumentRecord> documents, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        builder.AppendLine("You are preparing a draft architecture document in Markdown.");
        builder.AppendLine("The final document must be written in English.");
        builder.AppendLine("Include sections: 1) Project Scope, 2) Solution Overview Diagram, 3) Component Descriptions, 4) Solution Flow Diagram, 5) Solution Sequence Diagram, 6) Integration and Security Recommendations.");
        builder.AppendLine("Each diagram must be formatted as a Mermaid diagram inside fenced code blocks.");
        builder.AppendLine("Reference the extracted knowledge below.");
        builder.AppendLine();
        foreach (DocumentRecord document in documents)
        {
            builder.AppendLine($"Document: {document.FileName}");
            builder.AppendLine(document.ExtractedContent);
            builder.AppendLine();
        }

        var payload = new AgentRequest
        {
            Instructions = "Generate the requested architecture document using the provided context. Ensure all Mermaid diagrams compile and that recommendations include integration and security considerations.",
            Input = builder.ToString()
        };

        return await SendAgentRequestAsync(payload, cancellationToken);
    }

    private async Task<string> SendAgentRequestAsync(AgentRequest request, CancellationToken cancellationToken)
    {
        if (!_options.IsConfigured)
        {
            throw new InvalidOperationException("Azure Foundry agent configuration is incomplete.");
        }

        HttpClient client = _httpClientFactory.CreateClient("WebClient");
        var requestUri = BuildRequestUri();
        using HttpRequestMessage httpRequest = new(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(JsonSerializer.Serialize(request, SerializerOptions), Encoding.UTF8, "application/json")
        };

        httpRequest.Headers.TryAddWithoutValidation("api-key", _options.ApiKey);
        if (!string.IsNullOrWhiteSpace(_options.ProjectName))
        {
            httpRequest.Headers.TryAddWithoutValidation("x-azure-ai-project-name", _options.ProjectName);
        }

        using HttpResponseMessage response = await client.SendAsync(httpRequest, cancellationToken);
        string responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Azure Foundry agent request failed with status {StatusCode}: {Body}", response.StatusCode, responseContent);
            throw new HttpRequestException($"Azure Foundry agent returned {(int)response.StatusCode}");
        }

        return ParseAgentResponse(responseContent);
    }

    private Uri BuildRequestUri()
    {
        string endpoint = _options.Endpoint!.TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(_options.AgentId))
        {
            return new Uri($"{endpoint}/agents/{_options.AgentId}/runs:submit");
        }

        return new Uri(endpoint);
    }

    private string ParseAgentResponse(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        using JsonDocument document = JsonDocument.Parse(content);
        JsonElement root = document.RootElement;
        if (root.TryGetProperty("output", out JsonElement outputElement) && outputElement.ValueKind == JsonValueKind.String)
        {
            return outputElement.GetString() ?? string.Empty;
        }

        if (root.TryGetProperty("response", out JsonElement responseElement))
        {
            if (responseElement.ValueKind == JsonValueKind.String)
            {
                return responseElement.GetString() ?? string.Empty;
            }

            if (responseElement.ValueKind == JsonValueKind.Array)
            {
                return string.Join(Environment.NewLine, responseElement.EnumerateArray().Select(item => item.ToString()));
            }
        }

        if (root.TryGetProperty("result", out JsonElement resultElement))
        {
            return resultElement.ToString();
        }

        return content;
    }

    private sealed class AgentRequest
    {
        public string? Instructions { get; set; }
        public string? Input { get; set; }
        public List<AgentFilePayload>? Files { get; set; }
    }

    private sealed class AgentFilePayload
    {
        public string? FileName { get; set; }
        public string? Base64Content { get; set; }
    }
}