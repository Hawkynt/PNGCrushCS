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

  /// <summary>Packs one block, running together whatever repeats.</summary>
  /// <remarks>
  /// Literals alone would be simpler and will not do: the length of a packed block is written as at
  /// most five decimal digits and capped below the size of the block itself, so a block that grew
  /// under packing could not be described. Runs are therefore not an optimisation here but the thing
  /// that makes the format expressible at all.
  /// <para/>
  /// The escape value is picked from whatever the block does not contain, so no literal has to be
  /// doubled; only a block using all 256 values forces that, and then one of them is doubled.
  /// </remarks>
  public static byte[] Pack(ReadOnlySpan<byte> block) {
    var escape = _ChooseEscape(block);
    using var output = new System.IO.MemoryStream(block.Length);

    output.WriteByte(escape);
    output.WriteByte(0);

    // A stride of one walks the block from end to end; anything larger interleaves columns, which
    // only pays when the picture is stored in planes.
    output.WriteByte(0);
    output.WriteByte(1);

    for (var at = 0; at < block.Length;) {
      var value = block[at];
      var run = 1;
      while (at + run < block.Length && block[at + run] == value)
        ++run;

      // Three bytes to describe a run, so two of a kind is no saving and three only breaks even.
      if (run >= 4) {
        _WriteRun(output, escape, value, run);
        at += run;
        continue;
      }

      for (var i = 0; i < run; ++i) {
        output.WriteByte(value);
        if (value == escape)
          output.WriteByte(escape);
      }

      at += run;
    }

    return output.ToArray();
  }

  /// <summary>Writes one run, in the short form where it fits and the long one where it does not.</summary>
  private static void _WriteRun(System.IO.MemoryStream output, byte escape, byte value, int run) {
    while (run > 0) {
      var take = Math.Min(run, 0xFFFF);
      output.WriteByte(escape);

      if (take <= 256) {
        output.WriteByte(0);
        output.WriteByte((byte)(take - 1));
      } else {
        var encoded = take - 1;
        output.WriteByte(1);
        output.WriteByte((byte)(encoded >> 8));
        output.WriteByte((byte)encoded);
      }

      output.WriteByte(value);
      run -= take;
    }
  }

  /// <summary>A byte the block does not contain, if there is one, so nothing has to be doubled.</summary>
  private static byte _ChooseEscape(ReadOnlySpan<byte> block) {
    Span<bool> used = stackalloc bool[256];
    foreach (var value in block)
      used[value] = true;

    for (var candidate = 255; candidate >= 0; --candidate)
      if (!used[candidate])
        return (byte)candidate;

    return 0;
  }

}
