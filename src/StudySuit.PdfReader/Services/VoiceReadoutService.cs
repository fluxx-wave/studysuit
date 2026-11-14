using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using StudySuit.PdfReader.Models;

namespace StudySuit.PdfReader.Services;

public interface IVoiceReadoutService : IDisposable
{
    Task SpeakAsync(string markdownText, VoiceReadoutMode mode, CancellationToken cancellationToken = default);
    Task SpeakAsync(string markdownText, VoiceReadoutMode mode, double speed, int pitch, string? voiceName = null, CancellationToken cancellationToken = default);
    IReadOnlyList<string> GetInstalledWindowsVoices();
    void Stop();
}

/// <summary>
/// Reads AI, PDF, and news text aloud with natural Hindi (hi-IN) or English neural audio chunks,
/// optional English-to-Hindi translation, offline Windows SAPI fallback, and instant race-free stop.
/// </summary>
public sealed partial class VoiceReadoutService : IVoiceReadoutService
{
    private readonly HttpClient _http;
    private readonly string _tempDir;
    private MediaPlayer? _activePlayer;
    private dynamic? _activeSpVoice;
    private CancellationTokenSource? _internalCts;
    private int _playbackGeneration;

    public VoiceReadoutService(HttpClient? httpClient = null)
    {
        _http = httpClient ?? CreateDefaultClient();
        _tempDir = Path.Combine(Path.GetTempPath(), "StudySuitPdfVoice");
        Directory.CreateDirectory(_tempDir);
    }

    public Task SpeakAsync(
        string markdownText,
        VoiceReadoutMode mode,
        CancellationToken cancellationToken = default) =>
        SpeakAsync(markdownText, mode, speed: 1.0, pitch: 0, voiceName: null, cancellationToken);

    public async Task SpeakAsync(
        string markdownText,
        VoiceReadoutMode mode,
        double speed,
        int pitch,
        string? voiceName = null,
        CancellationToken cancellationToken = default)
    {
        Stop();
        int myGeneration = Interlocked.Increment(ref _playbackGeneration);

        string cleanText = PrepareSpeechText(markdownText);
        if (string.IsNullOrWhiteSpace(cleanText))
        {
            return;
        }

        _internalCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ct = _internalCts.Token;

        try
        {
            string speechText = cleanText;
            if (mode == VoiceReadoutMode.HindiTranslate && !ContainsDevanagari(cleanText))
            {
                speechText = await TranslateToHindiAsync(cleanText, ct).ConfigureAwait(false);
            }

            string langCode = mode == VoiceReadoutMode.English ? "en" : "hi";
            var chunks = SplitIntoSpeechChunks(speechText, maxLen: 180);
            bool isReelsVoice = !string.IsNullOrWhiteSpace(voiceName) &&
                (voiceName.Contains("Reels", StringComparison.OrdinalIgnoreCase) ||
                 voiceName.Contains("Instagram", StringComparison.OrdinalIgnoreCase) ||
                 voiceName.Contains("Baritone", StringComparison.OrdinalIgnoreCase));

            bool forceSapi = (!string.IsNullOrWhiteSpace(voiceName) &&
                             (voiceName.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) ||
                              voiceName.Contains("Windows Native", StringComparison.OrdinalIgnoreCase))) ||
                             isReelsVoice;

            foreach (string chunk in chunks)
            {
                if (ct.IsCancellationRequested || Volatile.Read(ref _playbackGeneration) != myGeneration)
                {
                    ct.ThrowIfCancellationRequested();
                    break;
                }

                if (!forceSapi)
                {
                    try
                    {
                        byte[] audioBytes = await FetchTtsMp3Async(chunk, langCode, ct).ConfigureAwait(false);
                        if (Volatile.Read(ref _playbackGeneration) != myGeneration)
                        {
                            break;
                        }

                        if (audioBytes.Length > 0)
                        {
                            await PlayMp3ChunkAsync(audioBytes, speed, myGeneration, ct).ConfigureAwait(false);
                            continue;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                        if (Volatile.Read(ref _playbackGeneration) != myGeneration || ct.IsCancellationRequested)
                        {
                            break;
                        }
                    }
                }

                await SpeakViaWindowsSapiAsync(chunk, speed, pitch, voiceName, myGeneration, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            if (Volatile.Read(ref _playbackGeneration) == myGeneration)
            {
                StopActivePlayers();
            }
        }
    }

    public void Stop()
    {
        Interlocked.Increment(ref _playbackGeneration);

        try
        {
            _internalCts?.Cancel();
            _internalCts?.Dispose();
            _internalCts = null;
        }
        catch
        {
        }

        StopActivePlayers();
    }

    public static string PrepareSpeechText(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return string.Empty;
        }

        string text = markdown.Replace("\r\n", "\n").Replace('\r', '\n');

        text = FencedBlockRegex().Replace(text, m =>
        {
            string lang = m.Groups["lang"].Value.Trim().ToLowerInvariant();
            string body = m.Groups["body"].Value.Trim();
            if (lang is "cards" or "grid")
            {
                return ExtractCardsReadableText(body);
            }

            return " ";
        });

        text = DisplayMathRegex().Replace(text, " equation. ");
        text = InlineMathRegex().Replace(text, m => m.Groups[1].Value);
        text = MarkdownImageRegex().Replace(text, " ");
        text = MarkdownLinkRegex().Replace(text, "$1");
        text = AlertTagRegex().Replace(text, "$1: ");
        text = HeadingMarkerRegex().Replace(text, "");
        text = TableDividerRegex().Replace(text, " ");
        text = text.Replace('|', ' ');
        text = MarkdownPunctuationRegex().Replace(text, "");
        text = MultiSpaceRegex().Replace(text, " ");

        return text.Trim();
    }

    public static IReadOnlyList<string> SplitIntoSpeechChunks(string text, int maxLen = 180)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return result;
        }

        var sentences = SentenceSplitRegex().Split(text.Trim())
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s));

        var current = new StringBuilder();
        foreach (string sentence in sentences)
        {
            if (sentence.Length > maxLen)
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString().Trim());
                    current.Clear();
                }

                int start = 0;
                while (start < sentence.Length)
                {
                    int len = Math.Min(maxLen, sentence.Length - start);
                    if (start + len < sentence.Length)
                    {
                        int lastSpace = sentence.LastIndexOf(' ', start + len, len);
                        if (lastSpace > start)
                        {
                            len = lastSpace - start;
                        }
                    }

                    string piece = sentence.Substring(start, len).Trim();
                    if (!string.IsNullOrEmpty(piece))
                    {
                        result.Add(piece);
                    }

                    start += len;
                    while (start < sentence.Length && char.IsWhiteSpace(sentence[start]))
                    {
                        start++;
                    }
                }

                continue;
            }

            if (current.Length > 0 && current.Length + 1 + sentence.Length > maxLen)
            {
                result.Add(current.ToString().Trim());
                current.Clear();
            }

            if (current.Length > 0)
            {
                current.Append(' ');
            }

            current.Append(sentence);
        }

        if (current.Length > 0)
        {
            result.Add(current.ToString().Trim());
        }

        return result;
    }

    private async Task<string> TranslateToHindiAsync(string text, CancellationToken ct)
    {
        try
        {
            var chunks = SplitIntoSpeechChunks(text, maxLen: 450);
            var translated = new StringBuilder();

            foreach (string chunk in chunks)
            {
                ct.ThrowIfCancellationRequested();
                string url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl=hi&dt=t&q={Uri.EscapeDataString(chunk)}";
                using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    return text;
                }

                string json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
                {
                    var segs = doc.RootElement[0];
                    if (segs.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var seg in segs.EnumerateArray())
                        {
                            if (seg.ValueKind == JsonValueKind.Array && seg.GetArrayLength() > 0 && seg[0].ValueKind == JsonValueKind.String)
                            {
                                translated.Append(seg[0].GetString());
                            }
                        }

                        translated.Append(' ');
                    }
                }
            }

            string final = translated.ToString().Trim();
            return string.IsNullOrWhiteSpace(final) ? text : final;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return text;
        }
    }

    private async Task<byte[]> FetchTtsMp3Async(string chunk, string langCode, CancellationToken ct)
    {
        string url = $"https://translate.google.com/translate_tts?ie=UTF-8&tl={langCode}&client=tw-ob&q={Uri.EscapeDataString(chunk)}";
        using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
    }

    private async Task PlayMp3ChunkAsync(byte[] mp3Bytes, double speed, int generation, CancellationToken ct)
    {
        string filePath = Path.Combine(_tempDir, $"tts_{Guid.NewGuid():N}.mp3");
        await File.WriteAllBytesAsync(filePath, mp3Bytes, ct).ConfigureAwait(false);

        try
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null)
            {
                return;
            }

            using var reg = ct.Register(() =>
            {
                StopActivePlayers();
                tcs.TrySetCanceled(ct);
            });

            await dispatcher.InvokeAsync(() =>
            {
                if (ct.IsCancellationRequested || Volatile.Read(ref _playbackGeneration) != generation)
                {
                    tcs.TrySetCanceled(ct);
                    return;
                }

                StopActivePlayersOnDispatcher();

                var player = new MediaPlayer();
                _activePlayer = player;
                player.SpeedRatio = Math.Clamp(speed, 0.5, 2.0);
                player.MediaEnded += (_, _) =>
                {
                    ClosePlayerInstance(player);
                    tcs.TrySetResult(true);
                };
                player.MediaFailed += (_, args) =>
                {
                    ClosePlayerInstance(player);
                    tcs.TrySetException(args.ErrorException ?? new InvalidOperationException("Media playback failed."));
                };

                player.Open(new Uri(filePath, UriKind.Absolute));
                player.Play();
            });

            await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
            catch
            {
            }
        }
    }

    private Task SpeakViaWindowsSapiAsync(string text, double speed, int pitch, string? voiceName, int generation, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            if (ct.IsCancellationRequested || Volatile.Read(ref _playbackGeneration) != generation)
            {
                ct.ThrowIfCancellationRequested();
                return;
            }

            Type? spType = Type.GetTypeFromProgID("SAPI.SpVoice");
            if (spType is null)
            {
                return;
            }

            dynamic? voice = Activator.CreateInstance(spType);
            if (voice is null)
            {
                return;
            }

            _activeSpVoice = voice;
            try
            {
                bool isReelsVoice = !string.IsNullOrWhiteSpace(voiceName) &&
                    (voiceName.Contains("Reels", StringComparison.OrdinalIgnoreCase) ||
                     voiceName.Contains("Instagram", StringComparison.OrdinalIgnoreCase) ||
                     voiceName.Contains("Baritone", StringComparison.OrdinalIgnoreCase));

                // SAPI rate: -10 to +10, where 1.0x speed is 0
                int sapiRate = (int)Math.Round((speed - 1.0) * 5.0);
                if (isReelsVoice && Math.Abs(speed - 1.0) < 0.05)
                {
                    sapiRate = 1; // Punchy, crisp Reels cadence
                }
                voice.Rate = Math.Clamp(sapiRate, -10, 10);

                if (isReelsVoice)
                {
                    try
                    {
                        foreach (dynamic v in voice.GetVoices())
                        {
                            string desc = v.GetDescription();
                            if (desc.Contains("David", StringComparison.OrdinalIgnoreCase) ||
                                desc.Contains("Mark", StringComparison.OrdinalIgnoreCase) ||
                                desc.Contains("George", StringComparison.OrdinalIgnoreCase) ||
                                desc.Contains("Male", StringComparison.OrdinalIgnoreCase))
                            {
                                voice.Voice = v;
                                break;
                            }
                        }
                    }
                    catch
                    {
                    }
                }
                else if (!string.IsNullOrWhiteSpace(voiceName) &&
                         !voiceName.Contains("Google", StringComparison.OrdinalIgnoreCase) &&
                         !voiceName.Contains("Windows Native", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        foreach (dynamic v in voice.GetVoices())
                        {
                            string desc = v.GetDescription();
                            if (desc.Contains(voiceName, StringComparison.OrdinalIgnoreCase))
                            {
                                voice.Voice = v;
                                break;
                            }
                        }
                    }
                    catch
                    {
                    }
                }

                int effectivePitch = pitch;
                if (isReelsVoice && pitch == 0)
                {
                    // Deep resonant baritone pitch for the famous viral Instagram Reels documentary narrator voice
                    effectivePitch = -4;
                }

                if (effectivePitch != 0)
                {
                    string safeChunk = System.Security.SecurityElement.Escape(text) ?? text;
                    string ssml = $"<pitch absmiddle=\"{effectivePitch}\">{safeChunk}</pitch>";
                    voice.Speak(ssml, 1 | 8); // SVSFlagsAsync | SVSFIsXML
                }
                else
                {
                    voice.Speak(text, 1); // SVSFlagsAsync
                }

                while (!ct.IsCancellationRequested && Volatile.Read(ref _playbackGeneration) == generation)
                {
                    bool done = voice.WaitUntilDone(80);
                    if (done)
                    {
                        break;
                    }
                }

                if (ct.IsCancellationRequested || Volatile.Read(ref _playbackGeneration) != generation)
                {
                    try
                    {
                        voice.Speak(string.Empty, 3); // SVSFlagsAsync | SVSFPurgeBeforeSpeak
                    }
                    catch
                    {
                    }
                }
            }
            finally
            {
                _activeSpVoice = null;
            }
        }, ct);
    }

    public IReadOnlyList<string> GetInstalledWindowsVoices()
    {
        var list = new List<string>();
        try
        {
            Type? spType = Type.GetTypeFromProgID("SAPI.SpVoice");
            if (spType is not null)
            {
                dynamic? voice = Activator.CreateInstance(spType);
                if (voice is not null)
                {
                    foreach (dynamic v in voice.GetVoices())
                    {
                        string desc = v.GetDescription();
                        if (!string.IsNullOrWhiteSpace(desc))
                        {
                            list.Add(desc);
                        }
                    }
                }
            }
        }
        catch
        {
        }

        return list;
    }

    private void StopActivePlayers()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null)
        {
            if (dispatcher.CheckAccess())
            {
                StopActivePlayersOnDispatcher();
            }
            else
            {
                dispatcher.BeginInvoke(StopActivePlayersOnDispatcher);
            }
        }

        try
        {
            _activeSpVoice?.Speak(string.Empty, 3);
        }
        catch
        {
        }
    }

    private void StopActivePlayersOnDispatcher()
    {
        var player = _activePlayer;
        if (player is not null)
        {
            _activePlayer = null;
            try
            {
                player.Stop();
                player.Close();
            }
            catch
            {
            }
        }
    }

    private void ClosePlayerInstance(MediaPlayer player)
    {
        if (ReferenceEquals(_activePlayer, player))
        {
            _activePlayer = null;
        }

        try
        {
            player.Stop();
            player.Close();
        }
        catch
        {
        }
    }

    private static bool ContainsDevanagari(string text)
    {
        foreach (char c in text)
        {
            if (c >= '\u0900' && c <= '\u097F')
            {
                return true;
            }
        }

        return false;
    }

    private static string ExtractCardsReadableText(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true });
            var sb = new StringBuilder();
            if (doc.RootElement.TryGetProperty("title", out var t))
            {
                sb.Append(t.GetString()).Append(". ");
            }

            if (doc.RootElement.TryGetProperty("cards", out var cards) && cards.ValueKind == JsonValueKind.Array)
            {
                foreach (var card in cards.EnumerateArray())
                {
                    if (card.TryGetProperty("title", out var ct))
                    {
                        sb.Append(ct.GetString()).Append(": ");
                    }

                    if (card.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in items.EnumerateArray())
                        {
                            sb.Append(item.GetString()).Append(", ");
                        }
                    }

                    sb.Append(". ");
                }
            }

            return sb.ToString();
        }
        catch
        {
            return " ";
        }
    }

    private static HttpClient CreateDefaultClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) StudySuitPdfReader/1.0");
        return client;
    }

    public void Dispose()
    {
        Stop();
    }

    [GeneratedRegex(@"```(?<lang>[^\n]*)\n(?<body>[\s\S]*?)```")]
    private static partial Regex FencedBlockRegex();

    [GeneratedRegex(@"\$\$[\s\S]*?\$\$|\\\[[\s\S]*?\\\]")]
    private static partial Regex DisplayMathRegex();

    [GeneratedRegex(@"\$([^$\n]+?)\$")]
    private static partial Regex InlineMathRegex();

    [GeneratedRegex(@"!\[[^\]]*\]\([^)]+\)")]
    private static partial Regex MarkdownImageRegex();

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]+\)")]
    private static partial Regex MarkdownLinkRegex();

    [GeneratedRegex(@"^\s*>\s*\[!(NOTE|TIP|IMPORTANT|WARNING|CAUTION)\]\s*", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex AlertTagRegex();

    [GeneratedRegex(@"^\s*#{1,6}\s*", RegexOptions.Multiline)]
    private static partial Regex HeadingMarkerRegex();

    [GeneratedRegex(@"^\|[\s:-]+(?:\|[\s:-]+)*\|?$", RegexOptions.Multiline)]
    private static partial Regex TableDividerRegex();

    [GeneratedRegex(@"[*_~`>#]")]
    private static partial Regex MarkdownPunctuationRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultiSpaceRegex();

    [GeneratedRegex(@"(?<=[।\.!\?])\s+")]
    private static partial Regex SentenceSplitRegex();
}
