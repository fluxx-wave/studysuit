using System;
using System.IO;
using NAudio.Wave;

namespace StudySuit.PdfReader.Services;

public interface IAudioRecordingService : IDisposable
{
    bool IsRecording { get; }
    double CurrentAudioLevel { get; }
    event Action<double>? AudioLevelChanged;
    event Action<byte[]>? RecordingCompleted;
    event Action<string>? ErrorOccurred;
    bool StartRecording();
    byte[] StopRecording();
    void CancelRecording();
}

public sealed class AudioRecordingService : IAudioRecordingService
{
    private WaveInEvent? _waveIn;
    private WaveFileWriter? _writer;
    private MemoryStream? _audioStream;
    private readonly WaveFormat _waveFormat = new(16000, 16, 1); // 16kHz, 16-bit, Mono PCM
    private readonly object _lock = new();

    public bool IsRecording { get; private set; }
    public double CurrentAudioLevel { get; private set; }

    public event Action<double>? AudioLevelChanged;
    public event Action<byte[]>? RecordingCompleted;
    public event Action<string>? ErrorOccurred;

    public bool StartRecording()
    {
        lock (_lock)
        {
            if (IsRecording)
            {
                return true;
            }

            try
            {
                if (WaveInEvent.DeviceCount == 0)
                {
                    ErrorOccurred?.Invoke("No audio recording device (microphone) found.");
                    return false;
                }

                _audioStream = new MemoryStream();
                _writer = new WaveFileWriter(_audioStream, _waveFormat);

                _waveIn = new WaveInEvent
                {
                    WaveFormat = _waveFormat,
                    BufferMilliseconds = 50
                };

                _waveIn.DataAvailable += OnDataAvailable;
                _waveIn.RecordingStopped += OnRecordingStopped;

                _waveIn.StartRecording();
                IsRecording = true;
                return true;
            }
            catch (Exception ex)
            {
                CleanupResources();
                ErrorOccurred?.Invoke($"Failed to start microphone: {ex.Message}");
                return false;
            }
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        lock (_lock)
        {
            if (!IsRecording || _writer is null) return;

            try
            {
                _writer.Write(e.Buffer, 0, e.BytesRecorded);

                // Compute peak level normalized between 0.0 and 1.0
                double peak = 0;
                for (int i = 0; i < e.BytesRecorded - 1; i += 2)
                {
                    short sample = (short)((e.Buffer[i + 1] << 8) | e.Buffer[i]);
                    double abs = Math.Abs(sample) / 32768.0;
                    if (abs > peak) peak = abs;
                }

                CurrentAudioLevel = peak;
                AudioLevelChanged?.Invoke(peak);
            }
            catch
            {
                // Ignore buffer write errors on stopping
            }
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
        {
            ErrorOccurred?.Invoke($"Recording stopped unexpectedly: {e.Exception.Message}");
        }
    }

    public byte[] StopRecording()
    {
        lock (_lock)
        {
            if (!IsRecording)
            {
                return Array.Empty<byte>();
            }

            IsRecording = false;
            CurrentAudioLevel = 0;

            try
            {
                _waveIn?.StopRecording();
            }
            catch
            {
            }

            try
            {
                _writer?.Flush();
                _writer?.Dispose();
                _writer = null;
            }
            catch
            {
            }

            try
            {
                _waveIn?.Dispose();
                _waveIn = null;
            }
            catch
            {
            }

            byte[] wavBytes = _audioStream?.ToArray() ?? Array.Empty<byte>();
            try
            {
                _audioStream?.Dispose();
                _audioStream = null;
            }
            catch
            {
            }

            RecordingCompleted?.Invoke(wavBytes);
            return wavBytes;
        }
    }

    public void CancelRecording()
    {
        lock (_lock)
        {
            if (!IsRecording) return;
            IsRecording = false;
            CurrentAudioLevel = 0;
            CleanupResources();
        }
    }

    private void CleanupResources()
    {
        try
        {
            _waveIn?.StopRecording();
            _waveIn?.Dispose();
        }
        catch { }
        finally { _waveIn = null; }

        try
        {
            _writer?.Dispose();
        }
        catch { }
        finally { _writer = null; }

        try
        {
            _audioStream?.Dispose();
        }
        catch { }
        finally { _audioStream = null; }

        IsRecording = false;
        CurrentAudioLevel = 0;
    }

    public void Dispose()
    {
        CleanupResources();
    }
}
