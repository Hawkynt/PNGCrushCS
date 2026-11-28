using System;
using System.IO;

namespace FileFormat.Eri;

/// <summary>A source of the signed byte samples a lossless ERI picture is coded as.</summary>
internal interface IEriSymbolDecoder {

  /// <summary>Fills <paramref name="destination"/> with the next samples.</summary>
  void Decode(Span<sbyte> destination);
}

/// <summary>The run-length gamma code of ERI's first level.</summary>
/// <remarks>
/// Runs of zeros and runs of non-zero values alternate, so a run needs no tag, only its length as
/// a gamma code; one leading bit says which kind comes first. A non-zero value is its sign bit and
/// its magnitude as a gamma code. Runs carry on from one call to the next until the context is
/// reset, which the block-independent coding does at every block.
/// </remarks>
internal sealed class EriGammaDecoder(EriBitReader bits) : IEriSymbolDecoder {

  private bool _zeros;
  private int _remaining;
  private bool _started;

  /// <summary>Reads the bit that says whether the first run is of zeros.</summary>
  public void Reset() {
    this._zeros = bits.ReadBit() == 0;
    this._remaining = 0;
    this._started = false;
  }

  public void Decode(Span<sbyte> destination) {
    for (var i = 0; i < destination.Length; ++i) {
      if (this._remaining == 0) {
        if (this._started)
          this._zeros = !this._zeros;
        this._remaining = bits.ReadGamma();
        this._started = true;
      }

      --this._remaining;
      if (this._zeros) {
        destination[i] = 0;
        continue;
      }

      var negative = bits.ReadBit() != 0;
      var magnitude = (sbyte)bits.ReadGamma();
      destination[i] = negative ? (sbyte)-magnitude : magnitude;
    }
  }
}

/// <summary>The ERINA code: run-length adaptive Huffman over the previous sample (ERI's second level).</summary>
/// <remarks>
/// 256 trees, one for each value the previous sample had, plus one for run lengths. A zero is
/// followed by the length of the run of zeros it starts, from that tree, escaped as a gamma code.
/// </remarks>
internal sealed class EriHuffmanDecoder : IEriSymbolDecoder {

  private readonly EriBitReader _bits;
  private readonly EriHuffmanTree[] _trees = new EriHuffmanTree[0x101];
  private readonly bool _orderZero;
  private EriHuffmanTree _current;
  private int _zeros;

  public EriHuffmanDecoder(EriBitReader bits, bool orderZero = false) {
    this._bits = bits;
    this._orderZero = orderZero;
    if (orderZero) {
      var shared = new EriHuffmanTree();
      for (var i = 0; i < 0x100; ++i)
        this._trees[i] = shared;
    } else
      for (var i = 0; i < 0x100; ++i)
        this._trees[i] = new();

    this._trees[0x100] = new();
    this._current = this._trees[0];
  }

  /// <summary>Reads one symbol from <paramref name="tree"/>, escaped as eight plain bits.</summary>
  public int Symbol(EriHuffmanTree tree) {
    var code = tree.Decode(this._bits, this._Counts(tree));
    if (code is { } known)
      return known;

    var plain = this._bits.ReadBits(8);
    tree.Add(plain);
    return plain;
  }

  private int _Length(EriHuffmanTree tree) {
    var code = tree.Decode(this._bits, this._Counts(tree));
    if (code is { } known)
      return known;

    var plain = this._bits.ReadGamma();
    tree.Add(plain);
    return plain;
  }

  // The order-0 coder stops counting once a tree is one short of the halving limit.
  private bool _Counts(EriHuffmanTree tree) => !this._orderZero || tree.TotalWeight < 0x4000 - 1;

  public void Decode(Span<sbyte> destination) {
    var i = 0;
    while (i < destination.Length && this._zeros > 0) {
      destination[i++] = 0;
      --this._zeros;
    }

    while (i < destination.Length) {
      var symbol = this.Symbol(this._current);
      destination[i++] = (sbyte)symbol;

      if (symbol == 0) {
        var length = this._Length(this._trees[0x100]) - 1;
        if (length < 0)
          throw new InvalidDataException("ERINA zero run has no length.");
        this._zeros = length;
        while (i < destination.Length && this._zeros > 0) {
          destination[i++] = 0;
          --this._zeros;
        }
      }

      this._current = this._trees[symbol & 0xFF];
    }
  }
}

/// <summary>The arithmetic decoder of the ERISA code (ERI's fifth level).</summary>
/// <remarks>
/// A sixteen-bit code register and a sixteen-bit interval width ("augend"), narrowed by integer
/// arithmetic: the lower bound rounded up, the width rounded down. The stream starts with sixteen
/// zero bits, so the first 32 bits loaded leave the register's upper half empty. Up to 256 bits past
/// the end are read as zeros, which the encoder's flush relies on.
/// </remarks>
internal sealed class EriArithmeticDecoder {

  private const int _POST_BITS_LIMIT = 256;

  private readonly EriBitReader _bits;
  private uint _code;
  private uint _augend;
  private int _postBits;

  public EriArithmeticDecoder(EriBitReader bits) {
    this._bits = bits;
    for (var i = 0; i < 32; ++i)
      this._code = (this._code << 1) | (uint)this._NextBit();
    this._augend = 0xFFFF;
  }

  private int _NextBit() {
    var bit = this._bits.TryReadBit();
    if (bit >= 0)
      return bit;
    if (++this._postBits >= _POST_BITS_LIMIT)
      throw new InvalidDataException("ERISA code stream ends before the picture does.");
    return 0;
  }

  /// <summary>Decodes the index of a symbol in <paramref name="model"/> without counting it.</summary>
  public int DecodeIndex(EriArithmeticModel model) {
    var total = (uint)model.Total;
    var target = this._code * total / this._augend;
    if (target >= 0x2000)
      throw new InvalidDataException("ERISA code value lies outside its interval.");

    var index = 0;
    var remaining = target;
    var below = 0u;
    uint count;
    while (true) {
      count = (uint)model.Count(index);
      if (remaining < count)
        break;
      remaining -= count;
      below += count;
      if (++index >= 0x101)
        throw new InvalidDataException("ERISA code value lies beyond every symbol.");
    }

    this._code -= (this._augend * below + total - 1) / total;
    this._augend = this._augend * count / total;
    if (this._augend == 0)
      throw new InvalidDataException("ERISA code narrowed to nothing.");

    while ((this._augend & 0x8000) == 0) {
      this._code = (this._code << 1) | (uint)this._NextBit();
      this._augend <<= 1;
    }

    this._code &= 0xFFFF;
    return index;
  }

  /// <summary>Decodes and counts one symbol of <paramref name="model"/>.</summary>
  public int Decode(EriArithmeticModel model) {
    var index = this.DecodeIndex(model);
    var symbol = model.Symbol(index);
    model.Increment(index);
    return symbol;
  }
}

/// <summary>The ERISA byte code: the arithmetic code over the previous sample's model, with zero runs.</summary>
internal sealed class EriNemesisDecoder : IEriSymbolDecoder {

  private readonly EriArithmeticDecoder _coder;
  private readonly EriArithmeticModel[] _models = new EriArithmeticModel[0x100];
  private readonly EriArithmeticModel _runLengths = new();
  private EriArithmeticModel _current;
  private int _zeros;

  public EriNemesisDecoder(EriArithmeticDecoder coder) {
    this._coder = coder;
    for (var i = 0; i < 0x100; ++i)
      this._models[i] = new();
    this._current = this._models[0];
  }

  public void Decode(Span<sbyte> destination) {
    var i = 0;
    while (i < destination.Length) {
      if (this._zeros > 0) {
        destination[i++] = 0;
        --this._zeros;
        continue;
      }

      var symbol = this._coder.Decode(this._current);
      destination[i++] = (sbyte)symbol;
      if (symbol == 0) {
        var run = this._coder.Decode(this._runLengths);
        if (run == EriArithmeticModel.Escape)
          throw new InvalidDataException("ERISA zero run has no length.");
        this._zeros = run;
      }

      this._current = this._models[symbol & 0xFF];
    }
  }
}
