using System.Text;
using NPOI.HWPF.Extractor;
using NPOI.HWPF.UserModel;
using NPOI.POIFS.FileSystem;
using NPOI.XWPF.UserModel;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;

namespace PoCDocumentCreation.Bot.Services;

internal static class DocumentContentExtractor
{
    public static string ExtractContent(string fileName, byte[] fileBytes)
    {
        ArgumentNullException.ThrowIfNull(fileBytes);

        if (fileBytes.Length == 0)
        {
            return string.Empty;
        }

        string extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();

        try
        {
            return extension switch
            {
                ".pdf" => ExtractPdf(fileBytes),
                ".docx" => ExtractDocx(fileBytes),
                ".doc" => ExtractDoc(fileBytes),
                _ => ExtractAsText(fileBytes)
            };
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to extract content from {fileName}", ex);
        }
    }

    private static string ExtractPdf(byte[] fileBytes)
    {
        using var stream = new MemoryStream(fileBytes, writable: false);
        using var reader = new PdfReader(stream);
        using var document = new PdfDocument(reader);
        var builder = new StringBuilder();

        for (int pageNumber = 1; pageNumber <= document.GetNumberOfPages(); pageNumber++)
        {
            string? text = PdfTextExtractor.GetTextFromPage(document.GetPage(pageNumber));

            if (!string.IsNullOrWhiteSpace(text))
            {
                builder.AppendLine(text.Trim());
            }
        }

        return builder.ToString();
    }

    private static string ExtractDocx(byte[] fileBytes)
    {
        using var stream = new MemoryStream(fileBytes, writable: false);
        using var document = new XWPFDocument(stream);
        var builder = new StringBuilder();

        foreach (var element in document.BodyElements)
        {
            switch (element)
            {
                case XWPFParagraph paragraph when !string.IsNullOrWhiteSpace(paragraph.ParagraphText):
                    builder.AppendLine(paragraph.ParagraphText.Trim());
                    break;
                case XWPFTable table:
                    foreach (var row in table.Rows)
                    {
                        foreach (var cell in row.GetTableCells())
                        {
                            foreach (var paragraph in cell.Paragraphs)
                            {
                                if (!string.IsNullOrWhiteSpace(paragraph.ParagraphText))
                                {
                                    builder.AppendLine(paragraph.ParagraphText.Trim());
                                }
                            }
                        }
                    }
                    break;
            }
        }

        return builder.ToString();
    }

    private static string ExtractDoc(byte[] fileBytes)
    {
        using var stream = new MemoryStream(fileBytes, writable: false);
        using var fileSystem = new POIFSFileSystem(stream);
        using var document = new HWPFDocument(fileSystem);
        using var extractor = new WordExtractor(document);
        return extractor.Text ?? string.Empty;
    }

    private static string ExtractAsText(byte[] fileBytes)
    {
        return Encoding.UTF8.GetString(fileBytes);
    }
}
