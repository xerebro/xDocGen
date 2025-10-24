using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using PoCDocumentCreation.Bot.Models;

namespace PoCDocumentCreation.Bot.Services;

internal static class DocumentSummarizer
{
    private static readonly Regex SentenceSplitRegex = new("(?<=[.!?])\\s+(?=[A-Z0-9])", RegexOptions.Compiled);
    private static readonly Regex WordRegex = new("[\u00C0-\u024F\w']+", RegexOptions.Compiled);
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a","an","the","and","or","for","of","to","in","on","by","with","is","are","was","were","be","been","being","from","that","this","it","as","at","into","about","over","through","between","after","before","above","below","up","down","out","off","again","further","then","once","here","there","when","where","why","how","all","any","both","each","few","more","most","other","some","such","no","nor","not","only","own","same","so","than","too","very","can","will","just"
    };

    public static string SummarizeDocument(string content, int maxBulletPoints = 5)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        List<string> sentences = SplitSentences(content);
        if (sentences.Count == 0)
        {
            return string.Empty;
        }

        Dictionary<int, double> sentenceScores = ScoreSentences(sentences);
        IEnumerable<int> selectedIndexes = sentenceScores
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key)
            .Take(maxBulletPoints)
            .Select(pair => pair.Key)
            .OrderBy(index => index);

        var builder = new StringBuilder();
        foreach (int index in selectedIndexes)
        {
            string sentence = sentences[index].Trim();
            if (!string.IsNullOrWhiteSpace(sentence))
            {
                builder.AppendLine($"- {sentence}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    public static string SummarizeDocuments(IEnumerable<DocumentRecord> documents, int maxWords = 800)
    {
        var records = documents.ToList();
        if (records.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.AppendLine("## Combined Document Summary");
        builder.AppendLine();

        string combinedText = string.Join("\n", records.Select(r => r.ExtractedContent));
        IReadOnlyList<string> keyThemes = ExtractKeywords(combinedText, 8);
        if (keyThemes.Count > 0)
        {
            builder.AppendLine("### Key Themes");
            foreach (string theme in keyThemes)
            {
                builder.AppendLine($"- {theme}");
            }
            builder.AppendLine();
        }

        builder.AppendLine("### Document Insights");
        foreach (DocumentRecord record in records)
        {
            string summary = string.IsNullOrWhiteSpace(record.Summary)
                ? SummarizeDocument(record.ExtractedContent)
                : record.Summary;

            string condensed = CondenseSummary(summary, 3);
            builder.AppendLine($"- **{record.FileName}**: {condensed}");
        }
        builder.AppendLine();

        IReadOnlyList<string> riskStatements = BuildRiskStatements(combinedText, keyThemes);
        if (riskStatements.Count > 0)
        {
            builder.AppendLine("### Potential Risks and Gaps");
            foreach (string risk in riskStatements)
            {
                builder.AppendLine($"- {risk}");
            }
            builder.AppendLine();
        }

        builder.AppendLine("### Recommended Next Steps");
        builder.AppendLine("- Validate requirements with stakeholders to confirm shared understanding.");
        builder.AppendLine("- Prioritize solution components that deliver the highest business impact first.");
        builder.AppendLine("- Align integration and security workstreams with the implementation roadmap.");

        return LimitToWordCount(builder.ToString().Trim(), maxWords);
    }

    internal static IReadOnlyList<string> ExtractKeywords(string text, int maxKeywords)
    {
        var frequencies = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in WordRegex.Matches(text ?? string.Empty))
        {
            string word = match.Value.ToLowerInvariant();
            if (word.Length <= 2 || StopWords.Contains(word))
            {
                continue;
            }

            frequencies.TryGetValue(word, out int count);
            frequencies[word] = count + 1;
        }

        return frequencies
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key)
            .Take(maxKeywords)
            .Select(pair => pair.Key)
            .ToList();
    }

    private static Dictionary<int, double> ScoreSentences(List<string> sentences)
    {
        var wordFrequencies = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (string sentence in sentences)
        {
            foreach (Match match in WordRegex.Matches(sentence))
            {
                string word = match.Value.ToLowerInvariant();
                if (StopWords.Contains(word))
                {
                    continue;
                }

                wordFrequencies.TryGetValue(word, out int count);
                wordFrequencies[word] = count + 1;
            }
        }

        return sentences
            .Select((sentence, index) => new { sentence, index })
            .ToDictionary(
                item => item.index,
                item => ScoreSentence(item.sentence, wordFrequencies));
    }

    private static double ScoreSentence(string sentence, Dictionary<string, int> wordFrequencies)
    {
        if (string.IsNullOrWhiteSpace(sentence))
        {
            return 0;
        }

        double score = 0;
        foreach (Match match in WordRegex.Matches(sentence))
        {
            string word = match.Value.ToLowerInvariant();
            if (wordFrequencies.TryGetValue(word, out int frequency))
            {
                score += frequency;
            }
        }

        return score / Math.Max(sentence.Length, 1);
    }

    private static List<string> SplitSentences(string content)
    {
        string normalized = content.Replace("\r", " ").Replace("\n", " ");
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return new List<string>();
        }

        string[] parts = SentenceSplitRegex.Split(normalized);
        return parts.Select(part => part.Trim()).Where(part => part.Length > 0).ToList();
    }

    private static string CondenseSummary(string summary, int maxSegments)
    {
        if (string.IsNullOrWhiteSpace(summary))
        {
            return string.Empty;
        }

        string[] segments = summary.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join("; ", segments.Take(maxSegments).Select(CleanBullet));
    }

    private static string CleanBullet(string line)
    {
        line = line.TrimStart('-').Trim();
        if (string.IsNullOrEmpty(line))
        {
            return string.Empty;
        }

        return char.ToUpper(line[0]) + line[1..];
    }

    private static IReadOnlyList<string> BuildRiskStatements(string combinedText, IReadOnlyList<string> themes)
    {
        var results = new List<string>();
        if (string.IsNullOrWhiteSpace(combinedText))
        {
            return results;
        }

        if (themes.Any(theme => theme.Equals("security", StringComparison.OrdinalIgnoreCase)))
        {
            results.Add("Security requirements appear frequently; ensure controls are designed and validated early.");
        }

        if (themes.Any(theme => theme.Equals("integration", StringComparison.OrdinalIgnoreCase) || theme.Equals("api", StringComparison.OrdinalIgnoreCase)))
        {
            results.Add("Integration points need interface contracts and failure-handling strategies.");
        }

        if (!themes.Any())
        {
            results.Add("Clarify the primary business goals to focus the solution scope.");
        }

        if (!combinedText.Contains("testing", StringComparison.OrdinalIgnoreCase))
        {
            results.Add("Testing expectations are unclear; define validation and acceptance criteria.");
        }

        return results;
    }

    private static string LimitToWordCount(string text, int maxWords)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        string[] words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length <= maxWords)
        {
            return text;
        }

        return string.Join(' ', words.Take(maxWords)) + "…";
    }
}
