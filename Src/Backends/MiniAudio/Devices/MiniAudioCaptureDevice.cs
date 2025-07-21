using System.Buffers;
using SoundFlow.Abstracts;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Enums;
using SoundFlow.Structs;
using SoundFlow.Utils;

namespace SoundFlow.Backends.MiniAudio.Devices;

internal sealed class MiniAudioCaptureDevice : AudioCaptureDevice
{
    private readonly MiniAudioDevice _device;

    public MiniAudioCaptureDevice(AudioEngine engine, nint context, DeviceInfo? info, AudioFormat format, DeviceConfig config) : base(engine, format, config)
    {
        _device = new MiniAudioDevice(this, context, info, format, config, ProcessAudioCallback);

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
    /// The callback method invoked by the MiniAudio backend when captured audio data is available.
    /// This method converts the raw device data to the standard 32-bit float format and raises the event.
    /// </summary>
    private void ProcessAudioCallback(nint pOutput, nint pInput, uint frameCount, MiniAudioDevice device)
    {
        if (pInput == nint.Zero) return;

        var length = (int)frameCount * device.Format.Channels;
        if (length <= 0) return;

        // Fast path: If the device is already providing F32, no conversion is needed.
        if (device.Format.Format == SampleFormat.F32)
        {
            var inputSpan = Extensions.GetSpan<float>(pInput, length);
            InvokeOnAudioProcessed(inputSpan);
            return;
        }

        // For other formats, we must convert to our internal float format using a temporary buffer.
        var tempBuffer = ArrayPool<float>.Shared.Rent(length);
        try
        {
            var floatSpan = tempBuffer.AsSpan(0, length);

            // 1. Convert from the device's native format into our temporary float buffer.
            DeviceBufferHelper.ConvertFromDeviceFormat(pInput, floatSpan, length, device.Format.Format);

            // 2. Invoke the event with the correctly converted sample data.
            InvokeOnAudioProcessed(floatSpan);
        }
        finally
        {
            // 3. Always return the rented buffer to the pool.
            ArrayPool<float>.Shared.Return(tempBuffer);
        }
    }
}