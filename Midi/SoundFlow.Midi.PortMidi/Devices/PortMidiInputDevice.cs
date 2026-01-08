using SoundFlow.Midi.Devices;
using SoundFlow.Midi.PortMidi.Enums;
using SoundFlow.Midi.PortMidi.Exceptions;
using SoundFlow.Midi.Structs;
using SoundFlow.Utils;

namespace SoundFlow.Midi.PortMidi.Devices;

/// <summary>
/// A concrete implementation of a MIDI input device using the PortMidi backend.
/// </summary>
internal sealed class PortMidiInputDevice : MidiInputDevice
{
    private readonly nint _stream;
    private readonly Task _pollingTask;
    private readonly CancellationTokenSource _cancellationTokenSource;

    /// <summary>
    /// Occurs when a system-level MIDI message (System Common or System Real-Time) is received.
    /// The int parameter is the full packed PmMessage.
    /// </summary>
    internal event Action<int, long>? OnSystemMessageReceived;

    /// <summary>
    /// Initializes a new instance of the <see cref="PortMidiInputDevice"/> class.
    /// </summary>
    /// <param name="info">The device information.</param>
    internal PortMidiInputDevice(MidiDeviceInfo info) : base(info)
    {
        var result = (PortMidiError)Native.Pm_OpenInput(out _stream, info.Id, nint.Zero, 1024, nint.Zero, nint.Zero);
        if (result != PortMidiError.NoError)
            throw new PortBackendException(result);
        
        // Disable default filters to ensure we get all data, including real-time clock messages.
        Native.Pm_SetFilter(_stream, 0);

        _cancellationTokenSource = new CancellationTokenSource();
        _pollingTask = Task.Run(PollForMessages, _cancellationTokenSource.Token);
    }

    private unsafe void PollForMessages()
    {
        var token = _cancellationTokenSource.Token;
        var eventBuffer = new Native.PmEvent[32];
        var sysexBuffer = new List<byte>(1024);
        var inSysEx = false;

        try
        {
            while (!token.IsCancellationRequested)
            {
                var result = (PortMidiError)Native.Pm_Poll(_stream);
                if (result is PortMidiError.NoError or (PortMidiError)1)
                {
                    fixed (Native.PmEvent* pBuffer = eventBuffer)
                    {
                        var messagesRead = Native.Pm_Read(_stream, pBuffer, eventBuffer.Length);
                        if (messagesRead > 0)
                        {
                            for (var i = 0; i < messagesRead; i++)
                            {
                                var pmEvent = pBuffer[i];
                                var status = (byte)(pmEvent.Message & 0xFF);

                                // A non-real-time status byte terminates any ongoing SysEx message.
                                if (inSysEx && (status & 0x80) != 0 && status < 0xF8 && status != 0xF7)
                                {
                                    Log.Warning("SysEx message was truncated by a new status byte.");
                                    inSysEx = false;
                                    sysexBuffer.Clear();
                                }

                                if (status == 0xF0) // SysEx Start
                                {
                                    inSysEx = true;
                                    sysexBuffer.Clear();
                                }

                                if (inSysEx)
                                {
                                    // Extract up to 4 bytes from the PmMessage int.
                                    var bytes = new byte[4];
                                    bytes[0] = (byte)((pmEvent.Message >> 0) & 0xFF);
                                    bytes[1] = (byte)((pmEvent.Message >> 8) & 0xFF);
                                    bytes[2] = (byte)((pmEvent.Message >> 16) & 0xFF);
                                    bytes[3] = (byte)((pmEvent.Message >> 24) & 0xFF);

                                    // Add bytes to our buffer until we find the EOX marker.
                                    for (var j = 0; j < 4; j++)
                                    {
                                        var currentByte = bytes[j];
                                        sysexBuffer.Add(currentByte);
                                        if (currentByte == 0xF7) // SysEx End
                                        {
                                            inSysEx = false;
                                            // Extract the payload (between F0 and F7).
                                            var payload = sysexBuffer.Skip(1).Take(sysexBuffer.Count - 2).ToArray();
                                            InvokeOnSysExReceived(payload);
                                            break;
                                        }
                                    }
                                }
                                else if (status >= 0xF1) // System Common or System Real-Time message
                                {
                                    OnSystemMessageReceived?.Invoke(pmEvent.Message, pmEvent.Timestamp);
                                }
                                else if ((status & 0x80) != 0) // Standard Channel Message
                                {
                                    var message = new MidiMessage(
                                        status,
                                        (byte)((pmEvent.Message >> 8) & 0xFF),
                                        (byte)((pmEvent.Message >> 16) & 0xFF),
                                        pmEvent.Timestamp
                                    );
                                    InvokeOnMessageReceived(message);
                                }
                            }
                        }
                    }
                }
                Task.Delay(1, token).Wait(token);
            }
        }
        catch (OperationCanceledException)
        {
            Log.Warning("PortMidiInputDevice: Polling task was cancelled.");
            // Task was cancelled, which is expected on Dispose.
        }
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        if (IsDisposed) return;
        
        _cancellationTokenSource.Cancel();
        _pollingTask.Wait(TimeSpan.FromSeconds(1));
        _cancellationTokenSource.Dispose();
        
        if (_stream != nint.Zero)
            Native.Pm_Close(_stream);
            
        IsDisposed = true;
    }
}