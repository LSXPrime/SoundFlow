using SoundFlow.Synthesis.Instruments;

namespace SoundFlow.Synthesis.Interfaces;

/// <summary>
/// Defines a collection of instruments, accessible via MIDI bank and program numbers.
/// </summary>
public interface IInstrumentBank
{
    /// <summary>
    /// Retrieves an instrument from the bank.
    /// </summary>
    /// <param name="bank">The MIDI bank number.</param>
    /// <param name="program">The MIDI program (preset) number.</param>
    /// <returns>The requested <see cref="Instrument"/>, or a default instrument if not found.</returns>
    Instrument GetInstrument(int bank, int program);
}