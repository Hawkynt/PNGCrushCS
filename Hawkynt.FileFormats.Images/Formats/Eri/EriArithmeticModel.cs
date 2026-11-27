using System;

namespace FileFormat.Eri;

/// <summary>One adaptive frequency table of the ERISA arithmetic code (ERI's fifth level).</summary>
/// <remarks>
/// 256 byte values and an escape, every count starting at one, kept sorted by count with the most
/// frequent first. A counted symbol moves up past every entry with a smaller count and stops
/// behind those with the same, so among equals the one that got there first stays in front. When
/// the counts reach 2000H in total they are all halved, rounding up.
/// </remarks>
internal sealed class EriArithmeticModel {

  /// <summary>The symbol that stands for "not a byte".</summary>
  public const int Escape = -1;

  private const int _SYMBOLS = 0x101;
  private const int _TOTAL_LIMIT = 0x2000;

  private readonly int[] _counts = new int[_SYMBOLS];
  private readonly int[] _symbols = new int[_SYMBOLS];

  public EriArithmeticModel() => this.Reset();

  public int Total { get; private set; }

  public int Count(int index) => this._counts[index];

  public int Symbol(int index) => this._symbols[index];

  public void Reset() {
    for (var i = 0; i < 0x100; ++i) {
      this._counts[i] = 1;
      this._symbols[i] = i;
    }

    this._counts[0x100] = 1;
    this._symbols[0x100] = Escape;
    this.Total = _SYMBOLS;
  }

  /// <summary>Counts the symbol at <paramref name="index"/> once more and returns where it now stands.</summary>
  public int Increment(int index) {
    var count = ++this._counts[index];
    var symbol = this._symbols[index];

    while (--index >= 0 && this._counts[index] < count) {
      this._counts[index + 1] = this._counts[index];
      this._symbols[index + 1] = this._symbols[index];
    }

    ++index;
    this._counts[index] = count;
    this._symbols[index] = symbol;

    if (++this.Total >= _TOTAL_LIMIT) {
      this.Total = 0;
      for (var i = 0; i < _SYMBOLS; ++i)
        this.Total += this._counts[i] = (this._counts[i] + 1) >> 1;
    }

    return index;
  }

  /// <summary>Where <paramref name="symbol"/> stands, and the counts in front of it; for the encoder.</summary>
  public (int Index, int Below) Find(int symbol) {
    var below = 0;
    for (var i = 0; i < _SYMBOLS; ++i) {
      if (this._symbols[i] == symbol)
        return (i, below);
      below += this._counts[i];
    }

    throw new ArgumentOutOfRangeException(nameof(symbol));
  }
}
