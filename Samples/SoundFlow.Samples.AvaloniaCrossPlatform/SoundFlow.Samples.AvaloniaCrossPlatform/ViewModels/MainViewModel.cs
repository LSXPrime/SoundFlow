using System;
using System.IO;
using System.Reactive;
using System.Reactive.Linq;
using ReactiveUI;
using SoundFlow.Abstracts;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Components;
using SoundFlow.Enums;
using SoundFlow.Providers;
using SoundFlow.Structs;

namespace SoundFlow.Samples.AvaloniaCrossPlatform.ViewModels;

public class MainViewModel : ReactiveObject, IDisposable
{
    private MiniAudioEngine? _audioEngine;
    private AudioPlaybackDevice? _playbackDevice;
    private SoundPlayer? _soundPlayer;
    private Stream? _currentFileStream;
    
    // UI State
    private string _currentTitle = "No file loaded";
    private string _timestampText = "00:00 / 00:00";
    private double _seekProgress; // 0.0 to 1.0
    private float _volume = 0.5f;
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
            .Subscribe(_ => UpdateUiState());
    }

    private void InitializeAudioEngine()
    {
        try
        {
            // Ensure we only init once
            if (_audioEngine == null)
            {
                _audioEngine = new MiniAudioEngine();
                
                var format = AudioFormat.DvdHq; 
                _playbackDevice = _audioEngine.InitializePlaybackDevice(null, format);
                _playbackDevice.Start();
                
                Console.WriteLine("Audio Engine and Playback Device Initialized.");
            }
        }
        catch (Exception ex)
        {
            CurrentTitle = $"Engine Error: {ex.Message}";
            Console.WriteLine(ex);
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
            // If the user drags the slider, seek immediately
            if (_soundPlayer is { Duration: > 0 })
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
        if (_audioEngine == null || _playbackDevice == null)
        {
            CurrentTitle = "Audio engine not initialized.";
            return;
        }
        
        try
        {
            // 1. Cleanup old resources
            StopPlayback();
            _currentFileStream?.Dispose();

            // 2. Setup new stream
            _currentFileStream = stream;
            
            // 3. Create Data Provider. It needs the engine to find a suitable decoder.
            var provider = new StreamDataProvider(_audioEngine, _currentFileStream);
            
            // 4. Create Player. It must match the device's audio format.
            _soundPlayer = new SoundPlayer(_audioEngine, _playbackDevice.Format, provider)
            {
                Volume = Volume,
                IsLooping = false
            };
            
            // 5. Connect to the device's Master Mixer
            _playbackDevice.MasterMixer.AddComponent(_soundPlayer);

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
        _playbackDevice?.MasterMixer.RemoveComponent(_soundPlayer);
        _soundPlayer.Dispose();
        _soundPlayer = null;
        
        IsPlaying = false;
        SeekProgress = 0;
        UpdateTimestamp(0, 0);
    }

    private void UpdateUiState()
    {
        if (_soundPlayer is not { State: PlaybackState.Playing })
        {
            // If player was playing but has now stopped (e.g. end of file), update state
            if (IsPlaying && _soundPlayer?.State != PlaybackState.Playing)
            {
                IsPlaying = false;
            }
            return;
        }

        var current = _soundPlayer.Time;
        var total = _soundPlayer.Duration;

        // Update Slider (0.0 - 1.0)
        if (total > 0)
        {
            var newProgress = current / total;
            this.RaiseAndSetIfChanged(ref _seekProgress, newProgress, nameof(SeekProgress));
        }

        UpdateTimestamp(current, total);
    }

    private void UpdateTimestamp(float current, float total)
    {
        var tsCurrent = TimeSpan.FromSeconds(current);
        var tsTotal = TimeSpan.FromSeconds(total);
        TimestampText = $@"{tsCurrent:mm\:ss} / {tsTotal:mm\:ss}";
    }



    public void Dispose()
    {
        _soundPlayer?.Dispose();
        _currentFileStream?.Dispose();
        _playbackDevice?.Dispose();
        _audioEngine?.Dispose();
    }
}