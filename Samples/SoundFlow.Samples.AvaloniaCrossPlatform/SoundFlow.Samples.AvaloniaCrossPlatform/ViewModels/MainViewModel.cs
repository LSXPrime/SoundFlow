using System;
using System.IO;
using System.Reactive;
using System.Reactive.Linq;
using ReactiveUI;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Components;
using SoundFlow.Enums;
using SoundFlow.Providers;

namespace SoundFlow.Samples.AvaloniaCrossPlatform.ViewModels;

public class MainViewModel : ReactiveObject, IDisposable
{
    private MiniAudioEngine? _audioEngine;
    private SoundPlayer? _soundPlayer;
    private Stream? _currentFileStream;
    
    // UI State
    private string _currentTitle = "No file loaded";
    private string _timestampText = "00:00 / 00:00";
    private double _seekProgress; // 0.0 to 1.0
    private float _volume = 0.5f;
    private bool _isBusy;
    private bool _isPlaying;

    public MainViewModel()
    {
        InitializeAudioEngine();

        // Commands
        PlayPauseCommand = ReactiveCommand.Create(TogglePlayPause);
        StopCommand = ReactiveCommand.Create(StopPlayback);
        
        // Timer to update the seek bar while playing
        Observable.Interval(TimeSpan.FromMilliseconds(200))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ => UpdateUIState());
    }

    private void InitializeAudioEngine()
    {
        try
        {
            // Ensure we only init once
            if (_audioEngine == null)
            {
                // 48kHz is a safe default for most WASM contexts
                _audioEngine = new MiniAudioEngine(48000, Capability.Playback);
                Console.WriteLine("Audio Engine Initialized.");
            }
        }
        catch (Exception ex)
        {
            CurrentTitle = $"Engine Error: {ex.Message}";
        }
    }

    // Properties

    public string CurrentTitle
    {
        get => _currentTitle;
        set => this.RaiseAndSetIfChanged(ref _currentTitle, value);
    }

    public string TimestampText
    {
        get => _timestampText;
        set => this.RaiseAndSetIfChanged(ref _timestampText, value);
    }

    public double SeekProgress
    {
        get => _seekProgress;
        set
        {
            this.RaiseAndSetIfChanged(ref _seekProgress, value);
            if (_soundPlayer != null && _soundPlayer.Duration > 0)
            {
                var targetTime = (float)(value * _soundPlayer.Duration);
                if (Math.Abs(_soundPlayer.Time - targetTime) > 0.5f)
                {
                    _soundPlayer.Seek(targetTime);
                }
            }
        }
    }

    public float Volume
    {
        get => _volume;
        set
        {
            this.RaiseAndSetIfChanged(ref _volume, value);
            if (_soundPlayer != null) _soundPlayer.Volume = value;
        }
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        set => this.RaiseAndSetIfChanged(ref _isPlaying, value);
    }

    // Commands

    public ReactiveCommand<Unit, Unit> PlayPauseCommand { get; }
    public ReactiveCommand<Unit, Unit> StopCommand { get; }

    // Actions

    /// <summary>
    /// Called by the View when a file is picked.
    /// In WASM, we must accept a Stream, not a file path string.
    /// </summary>
    public void LoadStream(Stream stream, string fileName)
    {
        try
        {
            StopPlayback();

            // 1. Cleanup old resources
            _currentFileStream?.Dispose();
            
            if (_soundPlayer != null)
            {
                Mixer.Master.RemoveComponent(_soundPlayer);
                _soundPlayer = null;
            }

            // 2. Setup new stream
            _currentFileStream = stream;
            
            // 3. Create Data Provider (StreamDataProvider handles the decoding via MiniAudioDecoder)
            var provider = new StreamDataProvider(_currentFileStream);
            
            // 4. Create Player
            _soundPlayer = new SoundPlayer(provider)
            {
                Volume = Volume,
                IsLooping = false
            };
            
            // 5. Connect to Master Mixer
            Mixer.Master.AddComponent(_soundPlayer);

            // 6. UI Update
            CurrentTitle = fileName;
            _soundPlayer.Play();
            IsPlaying = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            CurrentTitle = $"Error loading: {ex.Message}";
        }
    }

    private void TogglePlayPause()
    {
        if (_soundPlayer == null) return;

        if (_soundPlayer.State == PlaybackState.Playing)
        {
            _soundPlayer.Pause();
            IsPlaying = false;
        }
        else
        {
            _soundPlayer.Play();
            IsPlaying = true;
        }
    }

    private void StopPlayback()
    {
        if (_soundPlayer == null) return;
        _soundPlayer.Stop();
        IsPlaying = false;
        SeekProgress = 0;
        UpdateTimestamp(0, _soundPlayer.Duration);
    }

    private void UpdateUIState()
    {
        if (_soundPlayer == null || _soundPlayer.State != PlaybackState.Playing) return;

        var current = _soundPlayer.Time;
        var total = _soundPlayer.Duration;

        // Update Slider (0.0 - 1.0)
        if (total > 0)
        {
            var newProgress = current / total;
            this.RaisePropertyChanging(nameof(SeekProgress));
            _seekProgress = newProgress;
            this.RaisePropertyChanged(nameof(SeekProgress));
        }

        UpdateTimestamp(current, total);
        
        // Sync IsPlaying state in case it stopped naturally
        if (_soundPlayer.State == PlaybackState.Stopped && IsPlaying)
        {
            IsPlaying = false;
        }
    }

    private void UpdateTimestamp(float current, float total)
    {
        var tsCurrent = TimeSpan.FromSeconds(current);
        var tsTotal = TimeSpan.FromSeconds(total);
        TimestampText = $"{tsCurrent:mm\\:ss} / {tsTotal:mm\\:ss}";
    }

    public void Dispose()
    {
        _currentFileStream?.Dispose();
        _audioEngine?.Dispose();
    }
}