using SoundFlow.Interfaces;
using SoundFlow.Synthesis.Instruments;
using SoundFlow.Synthesis.Interfaces;

namespace SoundFlow.Synthesis.Banks;

/// <summary>
/// A high-level instrument bank that manages a prioritized list of other IInstrumentBank instances.
/// This allows for layering multiple sound sources (e.g., multiple SoundFonts, or a SoundFont
/// layered on top of basic synths) and provides a robust fallback mechanism.
/// </summary>
public sealed class MultiInstrumentBank : IInstrumentBank, IDisposable
{
    private readonly List<IInstrumentBank> _banks = [];
    private readonly Instrument _masterFallbackInstrument;

    /// <summary>
    /// Initializes a new instance of the <see cref="MultiInstrumentBank"/> class.
    /// </summary>
    /// <param name="masterFallbackInstrument">The final fallback instrument to use if a program is not found in any loaded bank.</param>
    public MultiInstrumentBank(Instrument masterFallbackInstrument)
    {
        if (!masterFallbackInstrument.IsFallback)
            throw new ArgumentException("The master fallback instrument must be explicitly marked as a fallback.", nameof(masterFallbackInstrument));
        _masterFallbackInstrument = masterFallbackInstrument;
    }

    /// <summary>
    /// Adds an instrument bank to the collection. Banks added later have higher priority.
    /// </summary>
    /// <param name="bank">The instrument bank to add.</param>
    public void AddBank(IInstrumentBank bank)
    {
        _banks.Add(bank);
    }

    /// <summary>
    /// Removes an instrument bank from the collection.
    /// </summary>
    /// <param name="bank">The instrument bank to remove.</param>
    public bool RemoveBank(IInstrumentBank bank)
    {
        return _banks.Remove(bank);
    }

    /// <summary>
    /// Clears all loaded instrument banks.
    /// </summary>
    public void ClearBanks()
    {
        _banks.Clear();
    }

    /// <inheritdoc />
    public Instrument GetInstrument(int bank, int program)
    {
        // Search in reverse order, so the last bank added has the highest priority.
        for (var i = _banks.Count - 1; i >= 0; i--)
        {
            var instrument = _banks[i].GetInstrument(bank, program);
            
            // If the returned instrument is not a fallback, we've found our match.
            if (instrument is { IsFallback: false })
                return instrument;
        }

        // If no real instrument was found in any bank, return the master fallback.
        return _masterFallbackInstrument;
    }

    /// <summary>
    /// Disposes of all disposable IInstrumentBank instances held by this manager.
    /// </summary>
    public void Dispose()
    {
        foreach (var bank in _banks)
        {
            if (bank is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
        ClearBanks();
    }
}