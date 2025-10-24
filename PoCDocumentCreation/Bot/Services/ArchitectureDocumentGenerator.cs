using System.Collections.Generic;
using System.Linq;
using System.Text;
using PoCDocumentCreation.Bot.Models;

namespace PoCDocumentCreation.Bot.Services;

internal static class ArchitectureDocumentGenerator
{
    public static string Generate(IEnumerable<DocumentRecord> documents)
    {
        var records = documents.ToList();
        if (records.Count == 0)
        {
            return "# Architecture Draft\n\n_No source documents were provided._";
        }

        string combinedText = string.Join("\n", records.Select(r => r.ExtractedContent));
        IReadOnlyList<string> themes = DocumentSummarizer.ExtractKeywords(combinedText, 6);

        var builder = new StringBuilder();
        builder.AppendLine("# Architecture Draft");
        builder.AppendLine();

        builder.AppendLine("## 1. Project Scope");
        if (themes.Count > 0)
        {
            builder.AppendLine("The solution targets the following primary themes:");
            foreach (string theme in themes)
            {
                builder.AppendLine($"- {Capitalize(theme)}");
            }
        }
        else
        {
            builder.AppendLine("Document inputs highlight business capabilities, technical components, and delivery considerations.");
        }
        builder.AppendLine();

        builder.AppendLine("## 2. Solution Overview Diagram");
        builder.AppendLine("```mermaid");
        builder.AppendLine("graph TD");
        builder.AppendLine("    A[Business Goals] --> B[Target Solution]");
        for (int i = 0; i < records.Count; i++)
        {
            string nodeId = $"D{i + 1}";
            builder.AppendLine($"    {nodeId}[{EscapeForMermaid(records[i].FileName)} Insights] --> B");
        }
        builder.AppendLine("    B --> C[Value Outcomes]");
        builder.AppendLine("```");
        builder.AppendLine();

        builder.AppendLine("## 3. Component Descriptions");
        foreach (DocumentRecord record in records)
        {
            builder.AppendLine($"### {record.FileName}");
            string summary = string.IsNullOrWhiteSpace(record.Summary)
                ? DocumentSummarizer.SummarizeDocument(record.ExtractedContent)
                : record.Summary;

            if (string.IsNullOrWhiteSpace(summary))
            {
                builder.AppendLine("- No specific highlights were found in the document extract.");
            }
            else
            {
                builder.AppendLine(summary);
            }
            builder.AppendLine();
        }

        builder.AppendLine("## 4. Solution Flow Diagram");
        builder.AppendLine("```mermaid");
        builder.AppendLine("flowchart LR");
        builder.AppendLine("    Users[Stakeholder Inputs] --> Intake[Requirement Intake]");
        builder.AppendLine("    Intake --> Analysis[Architecture Analysis]");
        builder.AppendLine("    Analysis --> Design[Solution Design]");
        builder.AppendLine("    Design --> Delivery[Implementation Streams]");
        builder.AppendLine("    Delivery --> Value[Measured Outcomes]");
        builder.AppendLine("```");
        builder.AppendLine();

        builder.AppendLine("## 5. Solution Sequence Diagram");
        builder.AppendLine("```mermaid");
        builder.AppendLine("sequenceDiagram");
        builder.AppendLine("    participant Stakeholder");
        builder.AppendLine("    participant ArchitectureTeam as Architecture Team");
        builder.AppendLine("    participant Systems as Core Systems");
        builder.AppendLine("    participant Security as Security Services");
        builder.AppendLine("    Stakeholder->>ArchitectureTeam: Share business drivers and constraints");
        builder.AppendLine("    ArchitectureTeam->>Systems: Evaluate current capabilities and integration needs");
        builder.AppendLine("    Systems-->>ArchitectureTeam: Provide interface and performance data");
        builder.AppendLine("    ArchitectureTeam->>Security: Validate compliance and protection requirements");
        builder.AppendLine("    Security-->>ArchitectureTeam: Recommend controls and policies");
        builder.AppendLine("    ArchitectureTeam-->>Stakeholder: Present target architecture and roadmap");
        builder.AppendLine("```");
        builder.AppendLine();

        builder.AppendLine("## 6. Integration and Security Recommendations");
        builder.AppendLine("- Align integration patterns with the most critical business capabilities.");
        builder.AppendLine("- Establish observability and error-handling for all cross-system interfaces.");
        builder.AppendLine("- Apply zero-trust principles to user and service access, including strong identity and encryption controls.");
        builder.AppendLine("- Define operational guardrails for data protection, privacy, and regulatory adherence.");

        return builder.ToString().Trim();
    }

    private static string EscapeForMermaid(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Document";
        }

        return value.Replace("[", "(").Replace("]", ")");
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return char.ToUpperInvariant(value[0]) + value[1..];
    }
}
