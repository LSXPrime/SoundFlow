using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ReactiveUI;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Components;
using SoundFlow.Enums;
using SoundFlow.Providers;
using SoundFlow.Samples.AvaloniaCrossPlatform.Models;

namespace SoundFlow.Samples.AvaloniaCrossPlatform.ViewModels;

public class MainViewModel : ViewModelBase
{
    private MiniAudioEngine _audioEngine;
    private SoundPlayer _soundPlayer;
    private float _volume = 0.5f;
    private double _playbackProgress;
    private PlaybackState _playbackState = PlaybackState.Stopped;
    private Track? _currentTrack;
    private bool _isLooping;

    public ObservableCollection<Track> Tracks { get; } = [];

    public ReactiveCommand<Unit, Unit> PlayCommand { get; }
    public ReactiveCommand<Unit, Unit> PauseCommand { get; }
    public ReactiveCommand<Unit, Unit> StopCommand { get; }
    public ReactiveCommand<Unit, Unit> NextTrackCommand { get; }
    public ReactiveCommand<Unit, Unit> PreviousTrackCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenFolderCommand { get; }
    public ReactiveCommand<double, Unit> SeekCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleLoopCommand { get; }


    public MainViewModel()
    {
        NewMethod();
        
        PlayCommand = ReactiveCommand.Create(Play);
        PauseCommand = ReactiveCommand.Create(Pause);
        StopCommand = ReactiveCommand.Create(Stop);
        NextTrackCommand = ReactiveCommand.Create(NextTrack);
        PreviousTrackCommand = ReactiveCommand.Create(PreviousTrack);
        OpenFolderCommand = ReactiveCommand.Create(OpenFolder);
        SeekCommand = ReactiveCommand.Create<double>(Seek);
        ToggleLoopCommand = ReactiveCommand.Create(ToggleLoop);

        Observable.Interval(TimeSpan.FromSeconds(0.1))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ => UpdatePlaybackProgress());
    }

    private async void NewMethod()
    {
        try
        {
            Console.WriteLine("Miniaudio WASM Initialized before Avalonia Initialize.");
            _audioEngine = new MiniAudioEngine(48000, Capability.Playback);
            
            Console.WriteLine("Audio Engine Initialized.");

            _soundPlayer = new SoundPlayer(new AssetDataProvider([]));
        }
        catch (TypeInitializationException typeInitEx)
        {
            Console.WriteLine($"TypeInitializationException caught: {typeInitEx.Message}");
            if (typeInitEx.InnerException != null)
            {
                Console.WriteLine($"  Inner Exception Type: {typeInitEx.InnerException.GetType().FullName}");
                Console.WriteLine($"  Inner Exception Message: {typeInitEx.InnerException.Message}");
                Console.WriteLine($"  Inner Exception StackTrace: {typeInitEx.InnerException.StackTrace}");
            }
            else
            {
                Console.WriteLine("  No Inner Exception.");
            }
            throw; // Re-throw the exception so it still gets reported to JavaScript
        }
        catch (AggregateException aggregateEx)
        {
            Console.WriteLine($"AggregateException caught: {aggregateEx.Message}");
            foreach (var innerEx in aggregateEx.InnerExceptions)
            {
                Console.WriteLine($"  Inner Exception Type: {innerEx.GetType().FullName}");
                Console.WriteLine($"  Inner Exception Message: {innerEx.Message}");
                Console.WriteLine($"  Inner Exception StackTrace: {innerEx.StackTrace}");
            }
            throw; // Re-throw the exception
        }
        catch (Exception ex) // Catch any other unexpected exceptions for logging too
        {
            Console.WriteLine($"Unexpected Exception caught: {ex.GetType().FullName}");
            Console.WriteLine($"  Message: {ex.Message}");
            Console.WriteLine($"  StackTrace: {ex.StackTrace}");
            throw; // Re-throw
        }
    }

    public float Volume
    {
        get => _volume;
        set
        {
            this.RaiseAndSetIfChanged(ref _volume, value);
            _soundPlayer.Volume = _volume;
        }
    }

    public double PlaybackProgress
    {
        get => _playbackProgress;
        set => this.RaiseAndSetIfChanged(ref _playbackProgress, value);
    }

    public PlaybackState PlaybackState
    {
        get => _playbackState;
        set => this.RaiseAndSetIfChanged(ref _playbackState, value);
    }

    public Track? CurrentTrack
    {
        get => _currentTrack;
        set => this.RaiseAndSetIfChanged(ref _currentTrack, value);
    }

    public bool IsLooping
    {
        get => _isLooping;
        set => this.RaiseAndSetIfChanged(ref _isLooping, value);
    }


    private void Play()
    {
        if (CurrentTrack != null)
        {
            if (PlaybackState != PlaybackState.Playing)
            {
                _soundPlayer.Play();
                PlaybackState = PlaybackState.Playing;
            }
        }
    }

    private void Pause()
    {
        if (PlaybackState == PlaybackState.Playing)
        {
            _soundPlayer.Pause();
            PlaybackState = PlaybackState.Paused;
        }
    }

    private void Stop()
    {
        _soundPlayer.Stop();
        PlaybackState = PlaybackState.Stopped;
        PlaybackProgress = 0;
    }

    private void NextTrack()
    {
        if (Tracks.Count == 0) return;
        int currentIndex = Tracks.IndexOf(CurrentTrack);
        int nextIndex = (currentIndex + 1) % Tracks.Count;
        PlayTrack(Tracks[nextIndex]);
    }

    private void PreviousTrack()
    {
        if (Tracks.Count == 0) return;
        int currentIndex = Tracks.IndexOf(CurrentTrack);
        int previousIndex = (currentIndex - 1 + Tracks.Count) % Tracks.Count;
        PlayTrack(Tracks[previousIndex]);
    }

    private void PlayTrack(Track track)
    {
        try
        {
            Stop(); // Stop current playback before loading new track
            var dataProvider = new ChunkedDataProvider(track.FilePath);
            _soundPlayer = new SoundPlayer(dataProvider) { Volume = Volume, IsLooping = IsLooping };
            Mixer.Master.AddComponent(_soundPlayer); // Add to mixer if not already added

            _soundPlayer.PlaybackEnded += SoundPlayer_PlaybackEnded; // Subscribe to PlaybackEnded event

            CurrentTrack = track;
            Play();
        }
        catch (Exception ex)
        {
            // Basic error handling - improve this in a real application
            Console.WriteLine($"Error loading track: {ex.Message}");
        }
    }

    private void SoundPlayer_PlaybackEnded(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!IsLooping)
                NextTrack(); // Play next track when current finishes if not looping
        });
    }


    private async void OpenFolder()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop || desktop.MainWindow is null)
            return;

        var dialog = await desktop.MainWindow.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions());

        if (dialog.Count > 0)
        {
            var folderPath = dialog[0].Path.ToString();
            Tracks.Clear();
            var audioFiles = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories)
                .Where(s => s.EndsWith(".mp3") || s.EndsWith(".wav"));

            foreach (var file in audioFiles)
            {
                Tracks.Add(new Track { FilePath = file, Title = Path.GetFileNameWithoutExtension(file) });
            }

            if (Tracks.Any())
                PlayTrack(Tracks.First()); // Start playing the first track in the folder
        }
    }

    private void UpdatePlaybackProgress()
    {
        if (PlaybackState is not (PlaybackState.Playing or PlaybackState.Paused)) return;
        if (_soundPlayer.Duration > 0)
            PlaybackProgress = _soundPlayer.Time / _soundPlayer.Duration;
        else
            PlaybackProgress = 0; // Avoid NaN if duration is unknown (e.g., streaming)
    }

    private void Seek(double position)
    {
        if (_soundPlayer.Duration > 0)
        {
            _soundPlayer.Seek((float)(position * _soundPlayer.Duration));
        }
    }

    private void ToggleLoop()
    {
        IsLooping = !IsLooping;
        _soundPlayer.IsLooping = IsLooping;
    }
}