using System;
using System.Collections.Generic;

namespace FileFormat.Codecs.Vp5.Tests;

/// <summary>
/// The encoding half of VP5's binary arithmetic coder, so that a test can state what a frame says.
/// </summary>
/// <remarks>
/// There is no VP5 encoder in this package and this is not the beginning of one — it writes the frame
/// header and nothing else, which is all a test of what the decoder refuses needs. It exists because
/// the alternative is byte strings nobody can read: "a key frame stating 32 by 19 macroblocks" is a
/// fact about a test, and <c>0x02, 0x80, 0x00, ...</c> is a fact about arithmetic coding.
/// <para/>
/// The arithmetic is the standard VPx encoder's: the same split as the decoder, a carry propagated
/// backwards through any run of <c>0xFF</c> already written, and a flush of thirty-two even-odds
/// zeroes so that the last real decision is fully determined. That it is the exact inverse of
/// <c>Vp5RangeDecoder</c> is not assumed — <see cref="Vp5MalformedInputTests"/> round-trips it.
/// </remarks>
internal sealed class Vp5BoolWriter {

  private readonly List<byte> _bytes = [];
  private uint _low;
  private int _range = 255;
  private int _count = -24;

  internal void WriteBool(int bit, int probability) {
    var split = 1 + (((this._range - 1) * probability) >> 8);
    var range = split;
    if (bit != 0) {
      this._low += (uint)split;
      range = this._range - split;
    }

    var shift = _NormalizationShift(range);
    range <<= shift;
    this._count += shift;

    if (this._count >= 0) {
      var offset = shift - this._count;

      if ((this._low << (offset - 1) & 0x80000000u) != 0) {
        var at = this._bytes.Count - 1;
        while (at >= 0 && this._bytes[at] == 0xFF) {
          this._bytes[at] = 0;
          --at;
        }

        if (at >= 0)
          ++this._bytes[at];
      }

      this._bytes.Add((byte)(this._low >> (24 - offset)));
      this._low = this._low << offset & 0xFFFFFF;
      shift = this._count;
      this._count -= 8;
    }

    this._low <<= shift;
    this._range = range;
  }

  internal void WriteFlag(int bit) => this.WriteBool(bit, 128);

  internal void WriteLiteral(int value, int bits) {
    while (bits-- > 0)
      this.WriteFlag((value >> bits) & 1);
  }

  /// <summary>Flushes the coder and returns the coded bytes. Nothing more may be written after this.</summary>
  internal byte[] Finish() {
    for (var i = 0; i < 32; ++i)
      this.WriteFlag(0);

    return this._bytes.ToArray();
  }

  private static int _NormalizationShift(int range) {
    var shift = 0;
    while (((range << shift) & 0x80) == 0 && shift < 8)
      ++shift;

    return shift;
  }
}
