namespace FileFormat.JpegXr.JxrLib;

/// <summary>
/// One adaptive VLC: a family of fixed code tables between which the decoder switches as a running
/// discriminant drifts (T.832 clause 9.5, jxrlib adapthuff.c).
/// </summary>
internal sealed class JxrAdaptiveHuffman(int symbols) {

  private const int _THRESHOLD = 8;
  private const int _MEMORY = 8;
  private const int _ROOT_BITS = 5;
  private const int _ROOT_BITS_LOG = 3;

  internal readonly int Symbols = symbols;
  internal int TableIndex;
  internal short[] DecodeTable = _G4HuffLookupTable;
  internal int[]? Delta;
  internal int DeltaOffset;
  internal int[]? Delta1;
  internal int Delta1Offset;
  internal bool Initialized;
  internal int Discriminant;
  internal int Discriminant1;
  private int _upperBound;
  private int _lowerBound;

  internal int DeltaAt(int index) => this.Delta![this.DeltaOffset + index];
  internal int Delta1At(int index) => this.Delta1![this.Delta1Offset + index];

  #region decode tables

  private static readonly short[] _G4HuffLookupTable = [
    19, 19, 19, 19, 27, 27, 27, 27, 10, 10, 10, 10, 10, 10, 10, 10,
    1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
    0, 0, 0, 0, 0, 0, 0, 0,
  ];

  private static readonly short[][] _G5HuffLookupTable = [
    [
      28, 28, 36, 36, 19, 19, 19, 19, 10, 10, 10, 10, 10, 10, 10, 10,
      1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
      0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    ],
    [
      11, 11, 11, 11, 19, 19, 19, 19, 27, 27, 27, 27, 35, 35, 35, 35,
      1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
      0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    ],
  ];

  private static readonly short[][] _G6HuffLookupTable = [
    [
      13, 29, 44, 44, 19, 19, 19, 19, 34, 34, 34, 34, 34, 34, 34, 34,
      1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
      0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    ],
    [
      12, 12, 28, 28, 43, 43, 43, 43, 2, 2, 2, 2, 2, 2, 2, 2,
      18, 18, 18, 18, 18, 18, 18, 18, 34, 34, 34, 34, 34, 34, 34, 34,
      0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    ],
    [
      4, 4, 12, 12, 43, 43, 43, 43, 18, 18, 18, 18, 18, 18, 18, 18,
      26, 26, 26, 26, 26, 26, 26, 26, 34, 34, 34, 34, 34, 34, 34, 34,
      0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    ],
    [
      5, 13, 36, 36, 43, 43, 43, 43, 18, 18, 18, 18, 18, 18, 18, 18,
      25, 25, 25, 25, 25, 25, 25, 25, 25, 25, 25, 25, 25, 25, 25, 25,
      0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    ],
  ];

  private static readonly short[][] _G7HuffLookupTable = [
    [
      45, 53, 36, 36, 27, 27, 27, 27, 2, 2, 2, 2, 2, 2, 2, 2,
      10, 10, 10, 10, 10, 10, 10, 10, 18, 18, 18, 18, 18, 18, 18, 18,
      0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    ],
    [
      -32736, 37, 28, 28, 19, 19, 19, 19, 10, 10, 10, 10, 10, 10, 10, 10,
      1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
      5, 6, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    ],
  ];

  private static readonly short[][] _G8HuffLookupTable = [
    [
      53, 21, 28, 28, 11, 11, 11, 11, 43, 43, 43, 43, 59, 59, 59, 59,
      2, 2, 2, 2, 2, 2, 2, 2, 34, 34, 34, 34, 34, 34, 34, 34,
      0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    ],
    [
      52, 52, 20, 20, 3, 3, 3, 3, 11, 11, 11, 11, 27, 27, 27, 27,
      35, 35, 35, 35, 43, 43, 43, 43, 58, 58, 58, 58, 58, 58, 58, 58,
      0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    ],
  ];

  private static readonly short[][] _G9HuffLookupTable = [
    [
      13, 29, 37, 61, 20, 20, 68, 68, 3, 3, 3, 3, 51, 51, 51, 51,
      41, 41, 41, 41, 41, 41, 41, 41, 41, 41, 41, 41, 41, 41, 41, 41,
      0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
      0, 0,
    ],
    [
      -32736, 53, 28, 28, 11, 11, 11, 11, 19, 19, 19, 19, 43, 43, 43, 43,
      1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
      -32734, 4, 7, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
      0, 0,
    ],
  ];

  private static readonly short[][] _G12HuffLookupTable = [
    [
      -32736, 5, 76, 76, 37, 53, 69, 85, 43, 43, 43, 43, 91, 91, 91, 91,
      57, 57, 57, 57, 57, 57, 57, 57, 57, 57, 57, 57, 57, 57, 57, 57,
      -32734, 1, 2, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
      0, 0, 0, 0, 0, 0, 0, 0,
    ],
    [
      -32736, 85, 13, 53, 4, 4, 36, 36, 43, 43, 43, 43, 67, 67, 67, 67,
      75, 75, 75, 75, 91, 91, 91, 91, 58, 58, 58, 58, 58, 58, 58, 58,
      2, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
      0, 0, 0, 0, 0, 0, 0, 0,
    ],
    [
      -32736, 37, 92, 92, 11, 11, 11, 11, 43, 43, 43, 43, 59, 59, 59, 59,
      67, 67, 67, 67, 75, 75, 75, 75, 2, 2, 2, 2, 2, 2, 2, 2,
      -32734, -32732, 2, 3, 6, 10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
      0, 0, 0, 0, 0, 0, 0, 0,
    ],
    [
      -32736, 29, 37, 69, 3, 3, 3, 3, 43, 43, 43, 43, 59, 59, 59, 59,
      75, 75, 75, 75, 91, 91, 91, 91, 10, 10, 10, 10, 10, 10, 10, 10,
      -32734, 10, 2, 6, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
      0, 0, 0, 0, 0, 0, 0, 0,
    ],
    [
      -32736, 93, 28, 28, 60, 60, 76, 76, 3, 3, 3, 3, 43, 43, 43, 43,
      9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9,
      -32734, -32732, -32730, 2, 4, 8, 6, 10, 0, 0, 0, 0, 0, 0, 0, 0,
      0, 0, 0, 0, 0, 0, 0, 0,
    ],
  ];

  private static readonly int[] _G5DeltaTable = [0, -1, 0, 1, 1];

  private static readonly int[] _G6DeltaTable = [
    -1, 1, 1, 1, 0, 1,
    -2, 0, 0, 2, 0, 0,
    -1, -1, 0, 1, -2, 0,
  ];

  private static readonly int[] _G7DeltaTable = [1, 0, -1, -1, -1, -1, -1];

  private static readonly int[] _G9DeltaTable = [2, 2, 1, 1, -1, -2, -2, -2, -3];

  private static readonly int[] _G12DeltaTable = [
    1, 1, 1, 1, 1, 0, 0, -1, 2, 1, 0, 0,
    2, 2, -1, -1, -1, 0, -2, -1, 0, 0, -2, -1,
    -1, 1, 0, 2, 0, 0, 0, 0, -2, 0, 1, 1,
    0, 1, 0, 1, -2, 0, -1, -1, -2, -1, -2, -2,
  ];

  private static readonly int[] _GMaxTables = [0, 0, 0, 0, 1, 2, 4, 2, 2, 2, 0, 0, 5];
  private static readonly int[] _GSecondDisc = [0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1];

  #endregion

  /// <summary>Moves to the neighbouring code table when the discriminant has crossed a bound, then selects that table.</summary>
  internal void AdaptDiscriminant() {
    var sym = this.Symbols;
    var change = false;
    if (!this.Initialized) {
      this.Initialized = true;
      this.Discriminant = this.Discriminant1 = 0;
      this.TableIndex = _GSecondDisc[sym];
    }

    var dL = this.Discriminant;
    var dH = this.Discriminant;
    if (_GSecondDisc[sym] != 0)
      dH = this.Discriminant1;

    if (dL < this._lowerBound) {
      --this.TableIndex;
      change = true;
    } else if (dH > this._upperBound) {
      ++this.TableIndex;
      change = true;
    }

    if (change) {
      this.Discriminant = 0;
      this.Discriminant1 = 0;
    }

    this.Discriminant = Clamp(this.Discriminant);
    this.Discriminant1 = Clamp(this.Discriminant1);

    var t = this.TableIndex;
    if (t < 0 || t >= _GMaxTables[sym])
      throw new System.IO.InvalidDataException("JPEG XR VLC table index out of range.");

    this._lowerBound = t == 0 ? int.MinValue : -_THRESHOLD;
    this._upperBound = t == _GMaxTables[sym] - 1 ? 1 << 30 : _THRESHOLD;

    switch (sym) {
      case 4:
        this.DecodeTable = _G4HuffLookupTable;
        this.Delta = null;
        break;
      case 5:
        this.Delta = _G5DeltaTable;
        this.DeltaOffset = 0;
        this.DecodeTable = _G5HuffLookupTable[t];
        break;
      case 6:
        this.Delta1 = _G6DeltaTable;
        this.Delta1Offset = sym * (t - (t + 1 == _GMaxTables[sym] ? 1 : 0));
        this.Delta = _G6DeltaTable;
        this.DeltaOffset = (t - 1 + (t == 0 ? 1 : 0)) * sym;
        this.DecodeTable = _G6HuffLookupTable[t];
        break;
      case 7:
        this.Delta = _G7DeltaTable;
        this.DeltaOffset = 0;
        this.DecodeTable = _G7HuffLookupTable[t];
        break;
      case 8:
        this.Delta = null;
        this.DecodeTable = _G8HuffLookupTable[0];
        break;
      case 9:
        this.Delta = _G9DeltaTable;
        this.DeltaOffset = 0;
        this.DecodeTable = _G9HuffLookupTable[t];
        break;
      case 12:
        this.Delta1 = _G12DeltaTable;
        this.Delta1Offset = sym * (t - (t + 1 == _GMaxTables[sym] ? 1 : 0));
        this.Delta = _G12DeltaTable;
        this.DeltaOffset = (t - 1 + (t == 0 ? 1 : 0)) * sym;
        this.DecodeTable = _G12HuffLookupTable[t];
        break;
      default:
        throw new System.IO.InvalidDataException("Undefined JPEG XR VLC alphabet.");
    }

    static int Clamp(int d) => d < -_THRESHOLD * _MEMORY ? -_THRESHOLD * _MEMORY : d > _THRESHOLD * _MEMORY ? _THRESHOLD * _MEMORY : d;
  }

  /// <summary>Decodes one symbol with a table that may need escape walks beyond the 5-bit root.</summary>
  internal static int GetHuff(short[] table, JxrBitReader io) {
    int symbol = table[io.Peek(_ROOT_BITS)];
    io.Flush(symbol < 0 ? _ROOT_BITS : symbol & ((1 << _ROOT_BITS_LOG) - 1));
    var huff = symbol >> _ROOT_BITS_LOG;
    if (huff >= 0)
      return huff;

    huff = symbol;
    var guard = 0;
    while ((huff = table[huff + 32768 + (int)io.Get(1)]) < 0)
      if (++guard > 64)
        throw new System.IO.InvalidDataException("JPEG XR VLC escape chain does not terminate.");
    return huff;
  }

  /// <summary>Decodes one symbol with a table whose codes all fit in the 5-bit root.</summary>
  internal static int GetHuffShort(short[] table, JxrBitReader io) {
    int symbol = table[io.Peek(_ROOT_BITS)];
    io.Flush(symbol & ((1 << _ROOT_BITS_LOG) - 1));
    return symbol >> _ROOT_BITS_LOG;
  }
}
