using SoundFlow.Abstracts;
using SoundFlow.Structs;

namespace SoundFlow.Components;

/// <summary>
/// Generates an envelope signal, commonly used for controlling the amplitude of audio signals over time.
/// </summary>
public class EnvelopeGenerator : SoundComponent
{
    /// <summary>
    /// Defines the different stages of the envelope.
    /// </summary>
    public enum EnvelopeState
    {
        /// <summary>
        /// The envelope is inactive and outputting silence.
        /// </summary>
        Idle,

        /// <summary>
        /// The envelope is in the attack stage, rising from silence to full level.
        /// </summary>
        Attack,

        /// <summary>
        /// The envelope is in the decay stage, falling from full level to the sustain level.
        /// </summary>
        Decay,

        /// <summary>
        /// The envelope is in the sustain stage, holding at a constant level.
        /// </summary>
        Sustain,

        /// <summary>
        /// The envelope is in the release stage, falling from the sustain level to silence.
        /// </summary>
        Release
    }

    /// <summary>
    /// Defines how the envelope is triggered and how it transitions through its stages.
    /// </summary>
    public enum TriggerMode
    {
        /// <summary>
        /// The envelope enters the release stage only when <see cref="TriggerOff"/> is called after a <see cref="TriggerOn"/>.
        /// Suitable for note-on/note-off style triggering.
        /// </summary>
        NoteOn,

        /// <summary>
        /// The envelope enters the release stage immediately when <see cref="TriggerOff"/> is called.
        /// Suitable for gate-style triggering where the envelope follows the gate signal.
        /// </summary>
        Gate,

        /// <summary>
        /// The envelope performs an instant attack and decay upon <see cref="TriggerOn"/>, bypassing the sustain stage.
        /// Useful for percussive sounds or short bursts.
        /// </summary>
        Trigger // Instant Attack and Decay, no Sustain
    }
    
    /// <summary>
    /// Initializes a new instance of the <see cref="EnvelopeGenerator"/> class.
    /// </summary>
    /// <param name="engine">The parent audio engine.</param>
    /// <param name="format">The audio format containing channels and sample rate and sample format</param>
    public EnvelopeGenerator(AudioEngine engine, AudioFormat format) : base(engine, format) { }

    /// <summary>
    /// Gets or sets the attack time in seconds.
    /// This is the time it takes for the envelope to rise from 0 to 1.
    /// </summary>
    public float AttackTime { get; set; } = 0.01f; // 10 ms

    /// <summary>
    /// Gets or sets the decay time in seconds.
    /// This is the time it takes for the envelope to fall from 1 to the <see cref="SustainLevel"/>.
    /// </summary>
    public float DecayTime { get; set; } = 0.1f; // 100 ms

    /// <summary>
    /// Gets or sets the sustain level, a value between 0 and 1.
    /// This is the level the envelope will hold at after the decay stage and before the release stage (in NoteOn mode).
    /// </summary>
    public float SustainLevel { get; set; } = 0.7f;

    /// <summary>
    /// Gets or sets the release time in seconds.
    /// This is the time it takes for the envelope to fall from the current level to 0.
    /// </summary>
    public float ReleaseTime { get; set; } = 0.05f; // 50 ms

    /// <summary>
    /// Gets or sets the trigger mode, determining how the envelope reacts to trigger signals.
    /// See <see cref="TriggerMode"/> for available modes.
    /// </summary>
    public TriggerMode Trigger { get; set; } = TriggerMode.Gate;

    /// <summary>
    /// Gets or sets a value indicating whether the envelope should retrigger if <see cref="TriggerOn"/> is called while it's already active.
    /// If true, calling <see cref="TriggerOn"/> will restart the envelope from the attack stage even if it's currently in another stage.
    /// </summary>
    public bool Retrigger { get; set; } = false;

    // Internal State
    private EnvelopeState _currentState = EnvelopeState.Idle;
    private float _currentLevel;
    private float _attackRate;
    private float _decayRate;
    private float _releaseRate;

    /// <summary>
    /// Occurs when the envelope level changes during audio generation.
    /// Subscribers can use this event to react to envelope level changes, for example, to visualize the envelope shape.
    /// </summary>
    public event Action<float>? LevelChanged;


    /// <inheritdoc/>
    public override string Name { get; set; } = "Envelope Generator";

    /// <summary>
    /// Triggers the envelope to start the attack stage.
    /// This method is typically called when a note is pressed or a gate signal is activated.
    /// </summary>
    public void TriggerOn()
    {
        if (!Retrigger && _currentState != EnvelopeState.Idle) return;
        _currentState = EnvelopeState.Attack;
        _currentLevel = 0f;
        CalculateRates();
    }

    /// <summary>
    /// Triggers the envelope to start the release stage.
    /// This method is typically called when a note is released or a gate signal is deactivated.
    /// It only has an effect if the <see cref="Trigger"/> mode is set to <see cref="TriggerMode.Gate"/> and the envelope is not already idle.
    /// </summary>
    public void TriggerOff()
    {
        if (_currentState == EnvelopeState.Idle || Trigger != TriggerMode.Gate) return;
        _currentState = EnvelopeState.Release;
        CalculateRates();
    }

    /// <summary>
    /// Calculates the rate of level change per sample for attack, decay, and release stages based on the current parameters and sample rate.
    /// This method is called internally when the envelope state changes to ensure correct rate calculations.
    /// </summary>
    private void CalculateRates()
    {
        // Calculate rates per sample for each stage
        _attackRate = AttackTime > 0 ? 1f / (AttackTime * Format.SampleRate) : float.MaxValue;
        _decayRate = DecayTime > 0
            ? (1f - SustainLevel) / (DecayTime * Format.SampleRate)
            : float.MaxValue;
        _releaseRate = ReleaseTime > 0
            ? _currentLevel / (ReleaseTime * Format.SampleRate)
            : float.MaxValue;
    }

    /// <inheritdoc/>
    protected override void GenerateAudio(Span<float> buffer, int channels)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            Update();
            LevelChanged?.Invoke(_currentLevel);
        }
    }

    /// <summary>
    /// Updates the envelope level based on the current state and calculated rates.
    /// This method is called per sample in the <see cref="GenerateAudio"/> method to advance the envelope through its stages.
    /// </summary>
    private void Update()
    {
        switch (_currentState)
        {
            case EnvelopeState.Attack:
                _currentLevel += _attackRate;
                if (_currentLevel >= 1f)
                {
                    _currentLevel = 1f;
                    _currentState = Trigger == TriggerMode.NoteOn ? EnvelopeState.Sustain : EnvelopeState.Decay;
                    CalculateRates();
                }

                break;

            case EnvelopeState.Decay:
                _currentLevel -= _decayRate;
                if (_currentLevel <= SustainLevel)
                {
                    _currentLevel = SustainLevel;
                    _currentState = EnvelopeState.Sustain;
                }

                break;

            case EnvelopeState.Sustain:
                if (Trigger == TriggerMode.Trigger)
                {
                    _currentState = EnvelopeState.Release;
                    CalculateRates();
                }

                // Hold at sustain level
                break;

            case EnvelopeState.Release:
                _currentLevel -= _releaseRate;
                if (_currentLevel <= 0f)
                {
                    _currentLevel = 0f;
                    _currentState = EnvelopeState.Idle;
                }

                break;

            case EnvelopeState.Idle:
                // Remain idle
                break;
        }
    }
}
