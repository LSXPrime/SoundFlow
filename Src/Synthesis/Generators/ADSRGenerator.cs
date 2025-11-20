using SoundFlow.Interfaces;
using SoundFlow.Structs;
using SoundFlow.Synthesis.Interfaces;
using SoundFlow.Synthesis.Voices;

namespace SoundFlow.Synthesis.Generators;

/// <summary>
/// An IGenerator that produces an Attack-Decay-Sustain-Release (ADSR) envelope signal.
/// </summary>
internal sealed class AdsrGenerator : IGenerator
{
    private enum EnvelopeState { Attack, Decay, Sustain, Release, Finished }

    private readonly AudioFormat _format;
    private EnvelopeState _state;
    private readonly float _attackRate;
    private readonly float _decayRate;
    private readonly float _sustainLevel;
    private readonly float _releaseTime;
    private float _releaseRate;
    private float _currentLevel;
    private bool _noteIsOn;

    /// <summary>
    /// Initializes a new instance of the <see cref="AdsrGenerator"/> class.
    /// </summary>
    /// <param name="format">The audio format, used to get the sample rate.</param>
    /// <param name="attackTime">Attack time in seconds.</param>
    /// <param name="decayTime">Decay time in seconds.</param>
    /// <param name="sustainLevel">Sustain level (0.0 to 1.0).</param>
    /// <param name="releaseTime">Release time in seconds.</param>
    public AdsrGenerator(AudioFormat format, float attackTime, float decayTime, float sustainLevel, float releaseTime)
    {
        _format = format;
        _attackRate = attackTime > 0.001f ? 1.0f / (attackTime * format.SampleRate) : float.MaxValue;
        _decayRate = decayTime > 0.001f ? (1.0f - sustainLevel) / (decayTime * format.SampleRate) : float.MaxValue;
        _sustainLevel = Math.Clamp(sustainLevel, 0.0f, 1.0f);
        _releaseTime = releaseTime;
        _releaseRate = releaseTime > 0.001f ? _sustainLevel / (releaseTime * format.SampleRate) : float.MaxValue;
        _state = EnvelopeState.Attack;
        _currentLevel = 0.0f;
        _noteIsOn = true;
    }

    /// <summary>
    /// Gets a value indicating whether the envelope has finished its release phase.
    /// </summary>
    public bool IsFinished => _state == EnvelopeState.Finished;

    /// <summary>
    /// Gets a value indicating if the envelope is in the release stage.
    /// </summary>
    public bool IsReleasing => _state == EnvelopeState.Release;

    /// <summary>
    /// Triggers the release phase of the envelope.
    /// </summary>
    public void NoteOff()
    {
        if (!_noteIsOn) return;
        _noteIsOn = false;

        // Recalculate release rate based on the level at which NoteOff was triggered.
        _releaseRate = _releaseTime > 0.001f ? _currentLevel / (_releaseTime * _format.SampleRate) : float.MaxValue;
        _state = EnvelopeState.Release;
    }

    /// <summary>
    /// Forces the envelope into a very fast release to kill the sound quickly.
    /// </summary>
    public void Kill()
    {
        _noteIsOn = false;
        _state = EnvelopeState.Release;
        // Override release rate for a 1ms fade out to prevent clicks
        _releaseRate = _currentLevel / (_format.SampleRate * 0.001f);
    }

    /// <inheritdoc />
    public int Generate(Span<float> buffer, VoiceContext context)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            switch (_state)
            {
                case EnvelopeState.Attack:
                    _currentLevel += _attackRate;
                    if (_currentLevel >= 1.0f)
                    {
                        _currentLevel = 1.0f;
                        _state = EnvelopeState.Decay;
                    }
                    break;

                case EnvelopeState.Decay:
                    _currentLevel -= _decayRate;
                    if (_currentLevel <= _sustainLevel)
                    {
                        _currentLevel = _sustainLevel;
                        if (_sustainLevel <= 0.0f && _noteIsOn)
                        {
                            // For percussive sounds with no sustain, automatically enter release
                            NoteOff();
                        }
                        else
                        {
                            _state = EnvelopeState.Sustain;
                        }
                    }
                    break;

                case EnvelopeState.Sustain:
                    // Level remains at _sustainLevel until NoteOff is called.
                    break;

                case EnvelopeState.Release:
                    _currentLevel -= _releaseRate;
                    if (_currentLevel <= 0.0f)
                    {
                        _currentLevel = 0.0f;
                        _state = EnvelopeState.Finished;
                    }
                    break;

                case EnvelopeState.Finished:
                    _currentLevel = 0.0f;
                    break;
            }
            buffer[i] = _currentLevel;
        }
        return buffer.Length;
    }

    /// <inheritdoc />
    public void Reset()
    {
        _state = EnvelopeState.Attack;
        _currentLevel = 0.0f;
        _noteIsOn = true;
    }
}