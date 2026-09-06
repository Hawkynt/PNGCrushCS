using System.IO;
using System.Numerics;

namespace FileFormat.Codecs.ProRes;

/// <summary>
/// One Golomb-Rice/exponential-Golomb combination codebook, and the reading of a codeword from it.
/// </summary>
/// <remarks>
/// RDD 36:2022, 7.1.1.1. Every variable-length code in a ProRes colour component is one of these.
/// The idea is that small symbols are cheaper under a Golomb-Rice code and large ones under an
/// exponential-Golomb code, so the two are spliced: symbols below
/// <c>(lastRiceQ + 1) * 2^kRice</c> are coded the first way and everything above it the second.
/// <para/>
/// Both families write a codeword as a unary prefix of '0' bits — whose length is the <i>code
/// level</i> — then a single '1' separator, then a binary suffix. Which family a particular codeword
/// belongs to is therefore readable from the prefix alone, before any of the suffix is consumed:
/// a level of <see cref="LastRiceQ"/> or less is Golomb-Rice, anything longer is
/// exponential-Golomb. That is why the level is peeked rather than read.
/// <para/>
/// A plain exponential-Golomb code of order <c>k</c> is not a separate case. RDD 36:2022, 7.1.1.1
/// notes that the combination code with <c>lastRiceQ = 0</c>, <c>kRice = k</c> and
/// <c>kExp = k + 1</c> is exactly it, so <see cref="ExpGolomb"/> builds one and there is a single
/// reading routine rather than two that have to agree.
/// </remarks>
/// <param name="LastRiceQ">The largest code level for which the Golomb-Rice half still applies.</param>
/// <param name="RiceOrder">The order of the Golomb-Rice half.</param>
/// <param name="ExpOrder">The order of the exponential-Golomb half.</param>
internal readonly record struct ProResGolombCode(int LastRiceQ, int RiceOrder, int ExpOrder) {

  /// <summary>The exponential-Golomb code of order <paramref name="order"/>, as a combination code.</summary>
  internal static ProResGolombCode ExpGolomb(int order) => new(0, order, order + 1);

  /// <summary>Reads one codeword and returns the non-negative symbol it stands for.</summary>
  internal int Read(ProResBitReader bits) {
    var level = bits.PeekLevel();
    bits.Skip(level + 1);

    // The Golomb-Rice half: the level is the quotient with respect to 2^kRice and the suffix is the
    // remainder.
    if (level <= this.LastRiceQ)
      return (level << this.RiceOrder) + (int)bits.Bits(this.RiceOrder);

    // The exponential-Golomb half. The first lastRiceQ + 1 prefix bits are the marker that put the
    // codeword in this half and carry no value; what remains is an order-kExp codeword whose own
    // level is that much shorter. Its value is the separator bit and the suffix read together as one
    // number — the spec's "binary representation of n + 2^k" — from which 2^k comes back off, and
    // then the symbols the Golomb-Rice half already accounts for are added on.
    var expLevel = level - this.LastRiceQ - 1;
    var suffixBits = expLevel + this.ExpOrder;
    if (suffixBits > 30)
      throw new InvalidDataException(
        $"A ProRes codeword claimed a code level of {level}, whose value cannot be represented. The component's coded data is damaged.");

    var value = (1 << suffixBits) | (int)bits.Bits(suffixBits);

    return value - (1 << this.ExpOrder) + ((this.LastRiceQ + 1) << this.RiceOrder);
  }

  /// <summary>Writes the codeword one non-negative symbol stands for.</summary>
  /// <remarks>
  /// The exact inverse of <see cref="Read"/>, and written beside it so that the two are read
  /// together. Which half of the combination code a symbol falls in is decided by the same threshold
  /// the reader uses to tell them apart: <c>(lastRiceQ + 1) · 2^kRice</c> symbols are spent on the
  /// Golomb-Rice half and everything above it goes to the exponential-Golomb one, where the code
  /// level is the exponent's excess over <c>kExp</c> plus the prefix that marked the switch.
  /// </remarks>
  internal void Write(ProResBitWriter bits, int symbol) {
    var riceSymbols = (this.LastRiceQ + 1) << this.RiceOrder;

    if (symbol < riceSymbols) {
      bits.UnaryPrefix(symbol >> this.RiceOrder);
      bits.Bits(symbol, this.RiceOrder);
      return;
    }

    // The reader's "binary representation of n + 2^k": the value whose leading '1' is the separator
    // and whose remaining bits are the suffix, so the suffix is as many bits as the value's exponent.
    var value = symbol - riceSymbols + (1 << this.ExpOrder);
    var suffixBits = BitOperations.Log2((uint)value);

    bits.UnaryPrefix(this.LastRiceQ + 1 + suffixBits - this.ExpOrder);
    bits.Bits(value, suffixBits);
  }
}
