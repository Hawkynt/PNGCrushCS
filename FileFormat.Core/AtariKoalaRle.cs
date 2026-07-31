using System;
using System.IO;

namespace FileFormat.Core;

/// <summary>
/// The run-length encoding Atari 8-bit Koala pictures use, and the formats that borrowed it.
/// </summary>
/// <remarks>
/// A command byte is a count with its top bit saying what kind of run follows: clear for a repeated
/// byte, set for a run of literals. A count of zero is not a wasted encoding but an escape — the
/// real count follows as two bytes — which is what lets a run of a thousand identical bytes cost
/// four bytes instead of four commands.
/// <para/>
/// A run may be spread across the picture rather than laid down consecutively, so the decoder holds
/// its position in the current run between calls: a format that unpacks column by column starts
/// each column in the middle of whatever run the last one ended in.
/// </remarks>
public sealed class AtariKoalaRle {

  private readonly byte[] _data;
  private int _at;
  private int _remaining;
  private int _value;

  public AtariKoalaRle(ReadOnlySpan<byte> data, int offset) {
    this._data = data.ToArray();
    this._at = offset;
    this._value = -1;
  }

  /// <summary>Where in the source the decoder has reached.</summary>
  public int Position => this._at;

  /// <summary>
  /// Unpacks bytes into <paramref name="target"/> starting at <paramref name="offset"/> and
  /// stepping by <paramref name="stride"/>, stopping before <paramref name="end"/>.
  /// </summary>
  public void Unpack(Span<byte> target, int offset, int stride, int end) {
    for (var position = offset; position < end; position += stride)
      target[position] = this._ReadByte();
  }

  private byte _ReadByte() {
    while (this._remaining == 0)
      this._ReadCommand();

    --this._remaining;
    if (this._value >= 0)
      return (byte)this._value;

    if (this._at >= this._data.Length)
      throw new InvalidDataException("A Koala-packed picture ends inside a run of literals.");

    return this._data[this._at++];
  }

  private void _ReadCommand() {
    if (this._at >= this._data.Length)
      throw new InvalidDataException("A Koala-packed picture ends before its picture does.");

    var b = this._data[this._at++];
    var repeated = b < 128;
    var count = repeated ? b : b - 128;

    if (count == 0) {
      if (this._at + 1 >= this._data.Length)
        throw new InvalidDataException("A Koala-packed picture's long count runs past the end.");

      count = (this._data[this._at] << 8) | this._data[this._at + 1];
      this._at += 2;
      if (count == 0)
        throw new InvalidDataException("A Koala-packed picture declares a run of nothing.");
    }

    this._remaining = count;

    if (!repeated) {
      this._value = -1;
      return;
    }

    if (this._at >= this._data.Length)
      throw new InvalidDataException("A Koala-packed picture's repeated run has no value.");

    this._value = this._data[this._at++];
  }
}
