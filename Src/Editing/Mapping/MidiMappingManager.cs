using System.Reflection;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Interfaces;
using SoundFlow.Midi.Devices;
using SoundFlow.Midi.Enums;
using SoundFlow.Midi.Structs;
using SoundFlow.Structs;
using SoundFlow.Utils;

namespace SoundFlow.Editing.Mapping;

/// <summary>
/// Manages and executes a collection of MIDI mappings in real-time for a composition.
/// </summary>
public sealed class MidiMappingManager : IDisposable
{
    private readonly Composition _composition;
    private readonly List<MidiMapping> _mappings = [];
    private readonly Dictionary<Guid, MemberInfo> _memberCache = [];
    private readonly List<MidiInputDevice> _subscribedDevices = [];
    private readonly Dictionary<MidiInputDevice, HighResCcParser> _highResCcParsers = new();

    /// <summary>
    /// Gets a read-only list of all active MIDI mappings.
    /// </summary>
    public IReadOnlyList<MidiMapping> Mappings => _mappings.AsReadOnly();

    /// <summary>
    /// Initializes a new instance of the <see cref="MidiMappingManager"/> class.
    /// </summary>
    /// <param name="composition">The parent composition.</param>
    internal MidiMappingManager(Composition composition)
    {
        _composition = composition;
    }

    /// <summary>
    /// Subscribes this manager to receive messages from a specific MIDI input device.
    /// This method should be called by the application for each device that should be used for mapping.
    /// </summary>
    /// <param name="device">The initialized MIDI input device to listen to.</param>
    public void AddInputDevice(MidiInputDevice device)
    {
        if (_subscribedDevices.Contains(device)) return;
        
        device.OnMessageReceived += OnMidiMessageReceived;
        _subscribedDevices.Add(device);
        _highResCcParsers[device] = new HighResCcParser(this);
    }

    /// <summary>
    /// Unsubscribes this manager from a specific MIDI input device.
    /// </summary>
    /// <param name="device">The MIDI input device to stop listening to.</param>
    public void RemoveInputDevice(MidiInputDevice device)
    {
        if (!_subscribedDevices.Contains(device)) return;

        device.OnMessageReceived -= OnMidiMessageReceived;
        _subscribedDevices.Remove(device);
        _highResCcParsers.Remove(device);
    }

    /// <summary>
    /// Adds a new MIDI mapping to the manager.
    /// </summary>
    /// <param name="mapping">The mapping to add.</param>
    public void AddMapping(MidiMapping mapping)
    {
        _mappings.Add(mapping);
        _composition.MarkDirty();
    }

    /// <summary>
    /// Removes a MIDI mapping from the manager by its ID.
    /// </summary>
    /// <param name="mappingId">The unique ID of the mapping to remove.</param>
    /// <returns>True if the mapping was found and removed; otherwise, false.</returns>
    public bool RemoveMapping(Guid mappingId)
    {
        var mapping = _mappings.FirstOrDefault(m => m.Id == mappingId);
        if (mapping == null) return false;
        
        _mappings.Remove(mapping);
        _memberCache.Remove(mapping.Id);
        _composition.MarkDirty();
        return true;
    }

    private void OnMidiMessageReceived(MidiMessage message, MidiDeviceInfo deviceInfo)
    {
        // Let the High-Resolution CC parser try its luck and process the message.
        var device = _subscribedDevices.FirstOrDefault(d => d.Info.Equals(deviceInfo));
        if (device != null && _highResCcParsers.TryGetValue(device, out var parser))
        {
            if (parser.ProcessMessage(message))
                return; // The message was part of a high-resolution sequence and has been handled.
        }
        
        // If not handled by the high-res parser, process as a standard 7-bit message.
        foreach (var mapping in _mappings)
        {
            if (!mapping.IsResolved) continue;

            var source = mapping.Source;
            var match = source.DeviceName == deviceInfo.Name && (source.Channel == 0 || source.Channel == message.Channel);
            if (!match) continue;

            match = source.MessageType switch
            {
                MidiMappingSourceType.ControlChange => message.Command == MidiCommand.ControlChange && message.ControllerNumber == source.MessageParameter,
                MidiMappingSourceType.NoteOn => message.Command == MidiCommand.NoteOn && message.NoteNumber == source.MessageParameter,
                MidiMappingSourceType.NoteOff => message.Command == MidiCommand.NoteOff && message.NoteNumber == source.MessageParameter,
                MidiMappingSourceType.PitchBend => message.Command == MidiCommand.PitchBend,
                _ => false
            };
            
            if (!match) continue;

            var inputValue = source.MessageType switch
            {
                MidiMappingSourceType.ControlChange => message.ControllerValue,
                MidiMappingSourceType.NoteOn => message.Velocity,
                MidiMappingSourceType.NoteOff => message.Velocity,
                MidiMappingSourceType.PitchBend => message.PitchBendValue,
                _ => -1
            };
            
            if (inputValue == -1) continue;
            
            ApplyMapping(mapping, inputValue);
        }
    }
    
    private void ApplyHighResMapping(int channel, int parameter, int value, MidiDeviceInfo deviceInfo)
    {
        foreach (var mapping in _mappings)
        {
            if (!mapping.IsResolved) continue;

            var source = mapping.Source;
            bool match = source.DeviceName == deviceInfo.Name &&
                         (source.Channel == 0 || source.Channel == channel) &&
                         source.MessageType == MidiMappingSourceType.HighResolutionControlChange &&
                         source.MessageParameter == parameter;

            if (match)
            {
                ApplyMapping(mapping, value);
            }
        }
    }

    private void ApplyMapping(MidiMapping mapping, int inputValue)
    {
        if (!_composition.TryGetMappableObject(mapping.Target.TargetObjectId, out var targetObject) || targetObject == null)
        {
            mapping.IsResolved = false; // Mark as unresolved if target is missing
            return;
        }

        if (!_memberCache.TryGetValue(mapping.Id, out var memberInfo))
        {
            memberInfo = targetObject.GetType().GetMember(mapping.Target.TargetMemberName, BindingFlags.Public | BindingFlags.Instance).FirstOrDefault();
            if (memberInfo == null)
            {
                mapping.IsResolved = false; // Mark as unresolved if member is missing
                return;
            }
            _memberCache[mapping.Id] = memberInfo;
        }

        try
        {
            switch (mapping.Behavior)
            {
                case MidiMappingBehavior.Absolute:
                    HandleAbsolute(memberInfo, targetObject, inputValue, mapping.Transformer);
                    break;
                case MidiMappingBehavior.Toggle:
                    HandleToggle(memberInfo, targetObject, inputValue, mapping.ActivationThreshold);
                    break;
                case MidiMappingBehavior.Trigger:
                    HandleTrigger(memberInfo, targetObject, inputValue, mapping);
                    break;
                case MidiMappingBehavior.Relative:
                    HandleRelative(memberInfo, targetObject, inputValue, mapping.Transformer);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[MIDI Mapping] Error applying mapping for '{mapping.Target.TargetMemberName}': {ex.Message}");
            _memberCache.Remove(mapping.Id);
        }
    }

    private static void HandleAbsolute(MemberInfo memberInfo, IMidiMappable targetObject, int inputValue, ValueTransformer transformer)
    {
        if (memberInfo is not PropertyInfo propInfo) return;
        
        var attribute = propInfo.GetCustomAttribute<ControllableParameterAttribute>();
        if (attribute == null) return;
        
        var transformedValue = TransformValue(inputValue, transformer, attribute);
        var convertedValue = Convert.ChangeType(transformedValue, propInfo.PropertyType);
        propInfo.SetValue(targetObject, convertedValue);
    }
    
    private static void HandleToggle(MemberInfo memberInfo, IMidiMappable targetObject, int inputValue, int threshold)
    {
        if (memberInfo is not PropertyInfo propInfo || propInfo.PropertyType != typeof(bool)) return;
        if (inputValue < threshold) return;

        var currentValue = (bool)propInfo.GetValue(targetObject)!;
        propInfo.SetValue(targetObject, !currentValue);
    }

    private static void HandleTrigger(MemberInfo memberInfo, IMidiMappable targetObject, int inputValue, MidiMapping mapping)
    {
        if (memberInfo is not MethodInfo methodInfo) return;
        if (inputValue < mapping.ActivationThreshold) return;

        var methodParams = methodInfo.GetParameters();
        var mappingArgs = mapping.Target.MethodArguments;

        if (methodParams.Length != mappingArgs.Count)
        {
            Log.Warning($"[MIDI Mapping] Method '{methodInfo.Name}' signature does not match mapping argument count.");
            return;
        }
        
        var invokeArgs = new object?[methodParams.Length];
        for (var i = 0; i < methodParams.Length; i++)
        {
            var argDef = mappingArgs[i];
            var paramInfo = methodParams[i];
            
            var attribute = paramInfo.GetCustomAttribute<ControllableParameterAttribute>();
            if (attribute == null)
            {
                Log.Warning($"[MIDI Mapping] Method parameter '{paramInfo.Name}' is missing [ControllableParameter] attribute.");
                return;
            }

            var value = argDef.Source == MidiMappingArgumentSource.Constant
                ? argDef.ConstantValue
                : argDef.Transformer != null
                    ? TransformValue(inputValue, argDef.Transformer, attribute)
                    : inputValue;

            // Convert to the method parameter's actual type
            invokeArgs[i] = Convert.ChangeType(value, paramInfo.ParameterType);
        }
        
        methodInfo.Invoke(targetObject, invokeArgs);
    }
    
    private static void HandleRelative(MemberInfo memberInfo, IMidiMappable targetObject, int inputValue, ValueTransformer transformer)
    {
        if (memberInfo is not PropertyInfo propInfo || !IsNumericType(propInfo.PropertyType)) return;

        var attribute = propInfo.GetCustomAttribute<ControllableParameterAttribute>();
        if (attribute == null) return;

        var currentValue = Convert.ToSingle(propInfo.GetValue(targetObject));
        
        // Standard relative encoder behavior: 64 is center, <64 is down, >64 is up
        var delta = inputValue - 64;
        
        // Scale the delta based on the target range to define the step size
        var step = (float)((attribute.MaxValue - attribute.MinValue) / (transformer.SourceMax - transformer.SourceMin));
        var newValue = currentValue + (delta * step);
        newValue = Math.Clamp(newValue, (float)attribute.MinValue, (float)attribute.MaxValue);
        
        var convertedValue = Convert.ChangeType(newValue, propInfo.PropertyType);
        propInfo.SetValue(targetObject, convertedValue);
    }

    private static float TransformValue(int inputValue, ValueTransformer transformer, ControllableParameterAttribute attribute)
    {
        // 1. Normalize MIDI input to [0, 1] based on transformer's source range
        var normalizedMidi = (inputValue - transformer.SourceMin) / (transformer.SourceMax - transformer.SourceMin);
        normalizedMidi = Math.Clamp(normalizedMidi, 0.0f, 1.0f);

        // 2. Apply the mapping's transfer curve (e.g., for reversing polarity)
        normalizedMidi = transformer.CurveType switch
        {
            MidiMappingCurveType.Exponential => normalizedMidi * normalizedMidi,
            MidiMappingCurveType.Logarithmic => MathF.Sqrt(normalizedMidi),
            _ => normalizedMidi
        };
        
        // 3. Denormalize from the transformer's target range (e.g. 0-1) to create the final normalized value
        var finalNormalized = transformer.TargetMin + normalizedMidi * (transformer.TargetMax - transformer.TargetMin);
        finalNormalized = Math.Clamp(finalNormalized, 0.0f, 1.0f);

        // 4. Denormalize from [0, 1] to the parameter's actual range using its scale
        if (attribute.Scale == MappingScale.Logarithmic)
        {
            var minLog = Math.Log(attribute.MinValue);
            var maxLog = Math.Log(attribute.MaxValue);
            return (float)Math.Exp(minLog + (maxLog - minLog) * finalNormalized);
        }
        
        // Linear scale
        return (float)(attribute.MinValue + (attribute.MaxValue - attribute.MinValue) * finalNormalized);
    }

    private static bool IsNumericType(Type type)
    {
        return type == typeof(float) || type == typeof(double) || type == typeof(int) || type == typeof(long);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var device in _subscribedDevices)
        {
            device.OnMessageReceived -= OnMidiMessageReceived;
        }
        _subscribedDevices.Clear();
        _highResCcParsers.Clear();
    }

    /// <summary>
    /// A stateful parser for 14-bit RPN/NRPN messages.
    /// </summary>
    private class HighResCcParser(MidiMappingManager manager)
    {
        private class ChannelState
        {
            public int RpnMsb = -1;
            public int RpnLsb = -1;
            public int NrpnMsb = -1;
            public int NrpnLsb = -1;
            public int DataMsb = -1;
        }

        private readonly Dictionary<int, ChannelState> _channelStates = new();

        public bool ProcessMessage(MidiMessage message)
        {
            if (message.Command != MidiCommand.ControlChange) return false;

            if (!_channelStates.TryGetValue(message.Channel, out var state))
            {
                state = new ChannelState();
                _channelStates[message.Channel] = state;
            }

            switch (message.ControllerNumber)
            {
                // NRPN
                case 99: state.NrpnMsb = message.ControllerValue; state.RpnMsb = -1; state.RpnLsb = -1; return true;
                case 98: state.NrpnLsb = message.ControllerValue; return true;
                // RPN
                case 101: state.RpnMsb = message.ControllerValue; state.NrpnMsb = -1; state.NrpnLsb = -1; return true;
                case 100: state.RpnLsb = message.ControllerValue; return true;
                
                // Data Entry
                case 6: state.DataMsb = message.ControllerValue; return true;
                case 38:
                    if (state.DataMsb != -1)
                    {
                        var parameter = -1;
                        if (state.NrpnMsb != -1 && state.NrpnLsb != -1)
                        {
                            parameter = (state.NrpnMsb << 7) | state.NrpnLsb;
                        }
                        else if (state.RpnMsb != -1 && state.RpnLsb != -1)
                        {
                            parameter = (state.RpnMsb << 7) | state.RpnLsb;
                        }

                        if (parameter != -1)
                        {
                            var value = (state.DataMsb << 7) | message.ControllerValue;
                            var device = manager._subscribedDevices.FirstOrDefault(d => manager._highResCcParsers[d] == this);
                            if(device != null) manager.ApplyHighResMapping(message.Channel, parameter, value, device.Info);
                            
                            // Reset data entry state after use
                            state.DataMsb = -1; 
                        }
                        return true;
                    }
                    break;
            }

            return false;
        }
    }
}