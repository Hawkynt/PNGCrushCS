using System;
using System.IO;

namespace FileFormat.Core;

/// <summary>
/// The block-oriented run-length encoding shared by the Atari ST packers descended from CrackArt.
/// </summary>
/// <remarks>
/// A block begins with four bytes naming its own escape byte, a default value and the stride it is
/// laid out in, so each block chooses the encoding that suits it rather than the format fixing one.
/// A stride of zero is a shorthand rather than a degenerate case: the block is the default value all
/// the way through and the stream contributes nothing at all, which is what a solid background costs.
/// <para/>
/// Everything that is not the escape byte stands for itself, and the escape doubled stands for the
/// escape — so a picture that happens to use every byte value still encodes, at one byte of cost.
/// </remarks>
public sealed class AtariStCaRle {

  private readonly byte[] _data;
  private int _at;

  public AtariStCaRle(ReadOnlySpan<byte> data, int offset) {
    this._data = data.ToArray();
    this._at = offset;
  }

  /// <summary>Where in the source the decoder has reached.</summary>
  public int Position {
    get => this._at;
    set => this._at = value;
  }

  /// <summary>
  /// Unpacks one block of <paramref name="blockSize"/> bytes into <paramref name="target"/>,
  /// reading no further than <paramref name="end"/>.
  /// </summary>
  public void UnpackBlock(Span<byte> target, int targetOffset, int blockSize, int end) {
    if (this._at > end - 4)
      throw new InvalidDataException("A packed block is too short to name its own encoding.");

    var escape = this._data[this._at];
    var fill = this._data[this._at + 1];
    var stride = (this._data[this._at + 2] << 8) | this._data[this._at + 3];
    if (stride >= blockSize)
      throw new InvalidDataException($"A packed block's stride of {stride} exceeds the block.");

    this._at += 4;

    var remaining = 0;
    var value = 0;
    if (stride == 0) {
      remaining = blockSize;
      value = fill;
      stride = 1;
    }

    for (var column = 0; column < stride; ++column)
    for (var position = column; position < blockSize; position += stride) {
      while (remaining == 0)
        this._ReadCommand(end, escape, fill, blockSize, ref remaining, ref value);

      --remaining;
      target[targetOffset + position] = (byte)value;
    }
  }

  private void _ReadCommand(int end, byte escape, byte fill, int blockSize, ref int remaining, ref int value) {
    var b = this._Next(end);
    if (b != escape) {
      remaining = 1;
      value = b;
      return;
    }

    var kind = this._Next(end);
    if (kind == escape) {
      remaining = 1;
      value = kind;
      return;
    }

    var count = this._Next(end);

    switch (kind) {
      case 0:
        remaining = count + 1;
        value = this._Next(end);
        break;

      case 1:
        remaining = ((count << 8) | this._Next(end)) + 1;
        value = this._Next(end);
        break;

      case 2:
        // A high byte of zero means the whole block rather than a run of 256.
        remaining = count == 0 ? blockSize : ((count << 8) | this._Next(end)) + 1;
        value = fill;
        break;

      default:
        remaining = kind + 1;
        value = count;
        break;
    }
  }

  private byte _Next(int end) {
    if (this._at >= end)
      throw new InvalidDataException("A packed block ends before it has filled itself.");

    return this._data[this._at++];
  }
}
