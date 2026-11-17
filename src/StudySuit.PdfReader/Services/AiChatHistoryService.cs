using System.IO;
using System.Text.Json;
using StudySuit.PdfReader.Models;

namespace StudySuit.PdfReader.Services;

public interface IAiChatHistoryService
{
    Task<IReadOnlyList<AiChatSession>> LoadSessionsAsync(CancellationToken cancellationToken = default);
    Task<AiChatSession?> LoadSessionAsync(string sessionId, CancellationToken cancellationToken = default);
    Task SaveSessionAsync(AiChatSession session, CancellationToken cancellationToken = default);
    Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default);
    Task ClearAllSessionsAsync(CancellationToken cancellationToken = default);
}

public sealed class AiChatHistoryService : IAiChatHistoryService
{
    private readonly string _historyDirectory;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public AiChatHistoryService(string? customDirectory = null)
    {
        if (!string.IsNullOrWhiteSpace(customDirectory))
        {
            _historyDirectory = customDirectory;
        }
        else
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _historyDirectory = Path.Combine(appData, "StudySuit.PdfReader", "chat_history");
        }

        Directory.CreateDirectory(_historyDirectory);
    }

    public async Task<IReadOnlyList<AiChatSession>> LoadSessionsAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<AiChatSession>();
        if (!Directory.Exists(_historyDirectory))
        {
            return list;
        }

        var files = Directory.GetFiles(_historyDirectory, "*.json");
        foreach (var file in files)
        {
            try
            {
                string json = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
                var session = JsonSerializer.Deserialize<AiChatSession>(json, JsonOptions);
                if (session is not null && !string.IsNullOrWhiteSpace(session.Id))
                {
                    bool hasUser = session.Messages.Any(m => m.IsUser && !m.IsWelcome && !string.IsNullOrWhiteSpace(m.Content));
                    bool hasAssistant = session.Messages.Any(m => !m.IsUser && !m.IsError && !m.IsWelcome && !string.IsNullOrWhiteSpace(m.Content));

                    // Filter out dummy or failed-only sessions that don't have a real two-way conversation
                    if (!hasUser || !hasAssistant)
                    {
                        // Clean up dummy / failed session files so they never linger
                        try
                        {
                            File.Delete(file);
                        }
                        catch
                        {
                            // File lock safeguard
                        }
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(session.FirstSearchedTopic))
                    {
                        session.FirstSearchedTopic = AiChatSession.ExtractFirstSearchedTopic(session.Messages, session.Title);
                    }

                    session.MessageCount = session.Messages.Count(m => !m.IsWelcome && !m.IsError);
                    list.Add(session);
                }
            }
            catch
            {
                // Ignore corrupted individual files
            }
        }

        return list.OrderByDescending(s => s.LastUpdatedAtUtc).ToList();
    }

    public async Task<AiChatSession?> LoadSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        string filePath = Path.Combine(_historyDirectory, $"{sessionId}.json");
        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            string json = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            var session = JsonSerializer.Deserialize<AiChatSession>(json, JsonOptions);
            if (session is not null)
            {
                if (string.IsNullOrWhiteSpace(session.FirstSearchedTopic))
                {
                    session.FirstSearchedTopic = AiChatSession.ExtractFirstSearchedTopic(session.Messages, session.Title);
                }
                session.MessageCount = session.Messages.Count(m => !m.IsWelcome && !m.IsError);
            }
            return session;
        }
        catch
        {
            return null;
        }
    }

    public async Task SaveSessionAsync(AiChatSession session, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(session.Id))
        {
            return;
        }

        bool hasUser = session.Messages.Any(m => m.IsUser && !m.IsWelcome && !string.IsNullOrWhiteSpace(m.Content));
        bool hasAssistant = session.Messages.Any(m => !m.IsUser && !m.IsError && !m.IsWelcome && !string.IsNullOrWhiteSpace(m.Content));
        if (!hasUser || !hasAssistant)
        {
            // Only persist real conversations that have both a user query and a valid assistant reply
            return;
        }

        if (string.IsNullOrWhiteSpace(session.FirstSearchedTopic))
        {
            session.FirstSearchedTopic = AiChatSession.ExtractFirstSearchedTopic(session.Messages, session.Title);
        }

        session.LastUpdatedAtUtc = DateTime.UtcNow;
        session.MessageCount = session.Messages.Count(m => !m.IsWelcome && !m.IsError);

        string filePath = Path.Combine(_historyDirectory, $"{session.Id}.json");
        string json = JsonSerializer.Serialize(session, JsonOptions);
        await File.WriteAllTextAsync(filePath, json, cancellationToken).ConfigureAwait(false);
    }

    public Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return Task.CompletedTask;
        }

        string filePath = Path.Combine(_historyDirectory, $"{sessionId}.json");
        if (File.Exists(filePath))
        {
            try
            {
                File.Delete(filePath);
            }
            catch
            {
                // File lock safeguard
            }
        }

        return Task.CompletedTask;
    }

    public Task ClearAllSessionsAsync(CancellationToken cancellationToken = default)
    {
        if (Directory.Exists(_historyDirectory))
        {
            foreach (var file in Directory.GetFiles(_historyDirectory, "*.json"))
            {
                try
                {
                    File.Delete(file);
                }
                catch
                {
                    // File lock safeguard
                }
            }
        }

        return Task.CompletedTask;
    }
}

