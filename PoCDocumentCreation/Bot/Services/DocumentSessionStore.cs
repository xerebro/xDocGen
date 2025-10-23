using System.Collections.Concurrent;
using PoCDocumentCreation.Bot.Models;

namespace PoCDocumentCreation.Bot.Services;

public interface IDocumentSessionStore
{
    DocumentSessionState GetOrCreate(string conversationId);
    void Reset(string conversationId);
}

public class DocumentSessionStore : IDocumentSessionStore
{
    private readonly ConcurrentDictionary<string, DocumentSessionState> _sessions = new();

    public DocumentSessionState GetOrCreate(string conversationId)
    {
        conversationId = conversationId ?? string.Empty;
        return _sessions.GetOrAdd(conversationId, _ => new DocumentSessionState());
    }

    public void Reset(string conversationId)
    {
        conversationId = conversationId ?? string.Empty;
        _sessions.TryRemove(conversationId, out _);
    }
}