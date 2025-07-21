using System.Buffers;
using SoundFlow.Abstracts;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Enums;
using SoundFlow.Structs;
using SoundFlow.Utils;

namespace SoundFlow.Backends.MiniAudio.Devices;

internal sealed class MiniAudioPlaybackDevice : AudioPlaybackDevice
{
    private readonly MiniAudioDevice _device;

    public MiniAudioPlaybackDevice(AudioEngine engine, nint context, DeviceInfo? info, AudioFormat format, DeviceConfig config) : base(engine, format, config)
    {
        _device = new MiniAudioDevice(this, context, info, format, config, ProcessAudioCallback);

        // Populate public properties from the internal device
        Info = _device.Info;
        Capability = _device.Capability;
    }

    public override void Start()
    {
        _device.Start();
        IsRunning = true;
    }

    public override void Stop()
    {
        _device.Stop();
        IsRunning = false;
    }

    public override void Dispose()
    {
        if (IsDisposed) return;
        OnDisposedHandler();
        _device.Dispose();
        IsDisposed = true;
    }

    /// <summary>
    /// The callback method invoked by the MiniAudio backend to request audio data.
    /// This method is responsible for generating audio and converting it to the device's output format.
    /// </summary>
    private void ProcessAudioCallback(nint pOutput, nint pInput, uint frameCount, MiniAudioDevice device)
    {
        if (pOutput == nint.Zero) return;

        var length = (int)frameCount * Format.Channels;
        if (length <= 0) return;

        // Fast path: If the device format is F32, we can process directly on the output buffer.
        if (device.Format.Format == SampleFormat.F32)
        {
            var buffer = Extensions.GetSpan<float>(pOutput, length);
            ProcessAndFillBuffer(buffer, device.Format.Channels);
            return;
        }

        // For other formats, we need a temporary float buffer for processing.
        var tempBuffer = ArrayPool<float>.Shared.Rent(length);
        try
        {
            var buffer = tempBuffer.AsSpan(0, length);
            
            // 1. Generate the audio signal into our temporary float buffer.
            ProcessAndFillBuffer(buffer, device.Format.Channels);

            // 2. Convert the float buffer to the device's native format.
            DeviceBufferHelper.ConvertToDeviceFormat(buffer, pOutput, length, device.Format.Format);
        }
        finally
        {
            ArrayPool<float>.Shared.Return(tempBuffer);
        }
    }
    
    /// <summary>
    /// Fills the provided buffer with audio by processing the master mixer or a soloed component.
    /// </summary>
    /// <param name="buffer">The buffer to fill with audio data.</param>
    /// <param name="channels">The number of channels to process.</param>
    private void ProcessAndFillBuffer(Span<float> buffer, int channels)
    {
        buffer.Clear();

        var soloedComponent = Engine.GetSoloedComponent();
        if (soloedComponent != null)
            soloedComponent.Process(buffer, channels);
        else
            MasterMixer.Process(buffer, channels);
    }
}