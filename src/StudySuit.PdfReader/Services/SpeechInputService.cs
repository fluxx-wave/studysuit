using System;
using System.Speech.Recognition;

namespace StudySuit.PdfReader.Services;

public interface ISpeechInputService : IDisposable
{
    bool IsSupported { get; }
    bool IsListening { get; }
    event Action<string>? SpeechRecognized;
    event Action<string>? SpeechHypothesized;
    event Action<string>? ErrorOccurred;
    void StartListening();
    void StopListening();
}

public sealed class SpeechInputService : ISpeechInputService
{
    private SpeechRecognitionEngine? _engine;
    private bool _isListening;
    private readonly object _lock = new();

    public bool IsSupported
    {
        get
        {
            try
            {
                return SpeechRecognitionEngine.InstalledRecognizers().Count > 0;
            }
            catch
            {
                return false;
            }
        }
    }

    public bool IsListening => _isListening;

    public event Action<string>? SpeechRecognized;
    public event Action<string>? SpeechHypothesized;
    public event Action<string>? ErrorOccurred;

    public void StartListening()
    {
        lock (_lock)
        {
            if (_isListening) return;

            try
            {
                if (_engine == null)
                {
                    _engine = new SpeechRecognitionEngine();
                    _engine.SetInputToDefaultAudioDevice();
                    _engine.LoadGrammar(new DictationGrammar());

                    _engine.SpeechRecognized += (s, e) =>
                    {
                        if (e.Result != null && !string.IsNullOrWhiteSpace(e.Result.Text))
                        {
                            SpeechRecognized?.Invoke(e.Result.Text);
                        }
                    };

                    _engine.SpeechHypothesized += (s, e) =>
                    {
                        if (e.Result != null && !string.IsNullOrWhiteSpace(e.Result.Text))
                        {
                            SpeechHypothesized?.Invoke(e.Result.Text);
                        }
                    };

                    _engine.RecognizeCompleted += (s, e) =>
                    {
                        if (e.Error != null)
                        {
                            ErrorOccurred?.Invoke(e.Error.Message);
                        }
                        _isListening = false;
                    };
                }

                _engine.RecognizeAsync(RecognizeMode.Multiple);
                _isListening = true;
            }
            catch (Exception ex)
            {
                _isListening = false;
                ErrorOccurred?.Invoke(ex.Message);
            }
        }
    }

    public void StopListening()
    {
        lock (_lock)
        {
            if (!_isListening || _engine == null) return;
            try
            {
                _engine.RecognizeAsyncStop();
            }
            catch
            {
            }
            finally
            {
                _isListening = false;
            }
        }
    }

    public void Dispose()
    {
        StopListening();
        _engine?.Dispose();
        _engine = null;
    }
}
