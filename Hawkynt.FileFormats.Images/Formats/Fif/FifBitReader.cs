using System;
using System.IO;

namespace FileFormat.Fif;

/// <summary>Reads FTC and FIF payloads least-significant bit first.</summary>
/// <remarks>
/// The FTC readers stop at the end of their data. The FIF1 readers may run on past it: Iterated's
/// decoder fetches whole machine words ahead and reads zeros beyond its buffer, so a progressive
/// restart stream ends with an implied run of zero bits. <see cref="Limit"/> bounds how far.
/// </remarks>
internal sealed class FifBitReader {

  private readonly byte[] _data;

  /// <summary>The next bit to read, counted from the start of <see cref="_data"/>.</summary>
  public long Position { get; private set; }

  /// <summary>The bit past which no read may advance.</summary>
  public long Limit { get; }

  /// <summary>The byte the reader started at.</summary>
  public int Start { get; }

  public FifBitReader(byte[] data, int start = 0, long endByte = -1) {
    _data = data;
    Start = start;
    Position = (long)start * 8;
    Limit = (endByte < 0 ? data.Length : endByte) * 8;
  }

  /// <summary>Bits in the underlying data.</summary>
  public long Length => (long)_data.Length * 8;

  /// <summary>The next <paramref name="count"/> bits without consuming them; bits past the data read as zero.</summary>
  public int Peek(int count) {
    var value = 0;
    for (var shift = 0; shift < count; ++shift) {
      var bit = Position + shift;
      var index = bit >> 3;
      if (index < _data.Length)
        value |= ((_data[index] >> (int)(bit & 7)) & 1) << shift;
    }

    return value;
  }

  public void Advance(int count) {
    if (count < 0 || Position + count > Limit)
      throw new InvalidDataException($"Fractal image stream at byte 0x{Start:x} is truncated at bit {Position - (long)Start * 8}.");

    Position += count;
  }

  public int Read(int count) {
    var value = Peek(count);
    Advance(count);
    return value;
  }

  /// <summary>A run length: unary prefix of k ones, then k-1 bits, giving 1 for no prefix.</summary>
  public int ReadRun() {
    var ones = 0;
    while (Read(1) != 0)
      if (++ones > 31)
        throw new InvalidDataException("FTC run-length prefix exceeds 31 one bits.");

    return ones == 0 ? 1 : (1 << (ones - 1)) + Read(ones - 1) + 1;
  }

  public void RequireZero(int count, string what) {
    if (count != 0 && Read(count) != 0)
      throw new InvalidDataException($"FTC {what} is not zero.");
  }

  /// <summary>Skips to the next byte boundary, which must be padded with zeros.</summary>
  public void AlignZero(string what) => RequireZero((int)(-Position & 7), what);

  /// <summary>Accepts up to seven zero bits of padding and nothing else before the limit.</summary>
  public void Finish(string what) {
    var remaining = Limit - Position;
    if (remaining > 7)
      throw new InvalidDataException($"{what} leaves {remaining} unexplained bits.");

    RequireZero((int)remaining, what + " padding");
  }
}
