using System;
using System.Collections.Generic;

namespace FileFormat.Mapletown;

/// <summary>Writes the printable bit stream a Mapletown picture is carried in.</summary>
/// <remarks>
/// The mirror of <see cref="MapletownStream"/>'s text form. The alphabet is printable ASCII less
/// the six characters a quoting layer might touch, and then part of the half-width Japanese range —
/// 128 characters exactly, so a character carries seven bits and not the six the format's own
/// documentation claims. The reader agrees: it refills after six shifts of a sentinel that starts
/// one place below the value, which is seven bits out of every character.
/// <para/>
/// Characters above ASCII are written as the single bytes a Japanese board would have carried. The
/// reader also accepts them re-encoded as three-byte sequences, which is what a file that has been
/// through a modern text tool looks like, but a file written here has been through no such thing.
/// </remarks>
public sealed class MapletownEncoder {

  /// <summary>Bits one character of the printable alphabet carries.</summary>
  public const int BitsPerCharacter = 7;

  /// <summary>The widest a length may be written, which is what the reader will follow.</summary>
  private const int _MAX_LENGTH_BITS = 20;

  /// <summary>The largest run or count a length can state.</summary>
  public const int MaxLength = (1 << (_MAX_LENGTH_BITS + 1)) - 2;

  private static readonly byte[] _Encode = _CreateEncodeTable();

  private readonly List<byte> _bytes = [];
  private int _pending;
  private int _count;

  private static byte[] _CreateEncodeTable() {
    var decode = MapletownStream.CreateDecodeTable();
    var encode = new byte[1 << BitsPerCharacter];
    for (var character = 0; character < decode.Length; ++character)
      if (decode[character] < encode.Length)
        encode[decode[character]] = (byte)character;

    return encode;
  }

  /// <summary>Writes literal text, which the bit stream is never part of.</summary>
  /// <remarks>
  /// A picture is announced by a line of plain text and the reader hunts for that line rather than
  /// counting bytes to it, so anything half-written has to be finished before one is emitted.
  /// </remarks>
  public void Text(string text) {
    ArgumentNullException.ThrowIfNull(text);
    this.Flush();
    foreach (var character in text)
      this._bytes.Add((byte)character);
  }

  public void Bit(int bit) {
    this._pending = (this._pending << 1) | (bit & 1);
    if (++this._count < BitsPerCharacter)
      return;

    this._bytes.Add(_Encode[this._pending]);
    this._pending = 0;
    this._count = 0;
  }

  /// <summary>Writes a fixed-width value, most significant bit first.</summary>
  public void Bits(int value, int count) {
    while (--count >= 0)
      this.Bit(value >> count);
  }

  /// <summary>
  /// Writes a length: as many ones as the value needs bits, then a zero, then the value itself.
  /// </summary>
  /// <remarks>
  /// A run of one costs two bits and a run of a thousand twenty-one, which is why the format spends
  /// nothing on the flat runs that make up most of a drawing.
  /// </remarks>
  public void Length(int value) {
    if (value is < 1 or > MaxLength)
      throw new ArgumentOutOfRangeException(nameof(value), value, $"A length runs from 1 to {MaxLength}.");

    var bits = 1;
    while (value + 1 >= 1 << (bits + 1))
      ++bits;

    this.Bits((1 << (bits - 1)) - 1, bits - 1);
    this.Bit(0);
    this.Bits(value - (1 << bits) + 1, bits);
  }

  /// <summary>Pads whatever is half-written out to a whole character.</summary>
  /// <remarks>The padding is read as bits, so it may only follow something the reader stops at.</remarks>
  public void Flush() {
    if (this._count == 0)
      return;

    this._pending <<= BitsPerCharacter - this._count;
    this._bytes.Add(_Encode[this._pending]);
    this._pending = 0;
    this._count = 0;
  }

  public byte[] ToArray() {
    this.Flush();

    return this._bytes.ToArray();
  }
}
