using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SoundFlow.Samples.AvaloniaCrossPlatform.ViewModels;
using System;

namespace SoundFlow.Samples.AvaloniaCrossPlatform.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
    }

    private async void OpenFile_Clicked(object? sender, RoutedEventArgs e)
    {
        // Get the top-level window to access the StorageProvider
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        try
        {
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open Audio File",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Audio Files")
                    {
                        Patterns = ["*.mp3", "*.wav", "*.flac"]
                    }
                ]
            });

            if (files.Count >= 1)
            {
                var file = files[0];
                var fileName = file.Name;
                
                // Open the stream. 
                var stream = await file.OpenReadAsync();

                if (DataContext is MainViewModel vm)
                {
                    vm.LoadStream(stream, fileName);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Picker Error: {ex.Message}");
        }
    }
}