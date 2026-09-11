using System;
using System.Collections.Generic;

namespace FileFormat.Codecs.Vp9;

/// <summary>Writes the boolean arithmetic code used by VP9 compressed headers and tiles.</summary>
/// <remarks>
/// This is the encoder-side counterpart of <see cref="Vp9BoolDecoder"/> and specification section 9.2.
/// The implementation uses the same arithmetic convention already exercised by the decoder's
/// bitstream-building tests, now exposed on the write side rather than hidden in a test fixture.
/// </remarks>
internal sealed class Vp9BoolEncoder {

  private readonly List<byte> _output = [];
  private uint _range = 255;
  private uint _bottom;
  private int _bitCount = 24;

  internal void WriteBool(int probability, int value) {
    var split = 1 + (((this._range - 1) * (uint)probability) >> 8);
    if (value != 0) {
      this._bottom += split;
      this._range -= split;
    } else
      this._range = split;

    while (this._range < 128) {
      this._range <<= 1;
      if ((this._bottom & (1u << 31)) != 0)
        this._CarryIntoOutput();

      this._bottom <<= 1;
      if (--this._bitCount != 0)
        continue;

      this._output.Add((byte)(this._bottom >> 24));
      this._bottom &= (1u << 24) - 1;
      this._bitCount = 8;
    }
  }

  internal void WriteFlag(int value) => this.WriteBool(128, value);

  internal void WriteLiteral(int bits, int value) {
    while (bits-- > 0)
      this.WriteFlag((value >> bits) & 1);
  }

  internal void WriteTree(ReadOnlySpan<sbyte> tree, ReadOnlySpan<byte> probabilities, int value) {
    Span<int> path = stackalloc int[16];
    Span<int> bits = stackalloc int[16];
    var depth = _FindLeaf(tree, 0, value, path, bits, 0);

    if (depth < 0)
      throw new ArgumentException($"The tree has no leaf with the value {value}.", nameof(value));

    for (var i = 0; i < depth; ++i)
      this.WriteBool(probabilities[path[i] >> 1], bits[i]);
  }

  internal byte[] Finish() {
    var count = this._bitCount;
    var value = this._bottom;

    if ((value & (1u << (32 - count))) != 0)
      this._CarryIntoOutput();

    value <<= count & 7;
    count >>= 3;
    while (--count >= 0)
      value <<= 8;

    count = 4;
    while (--count >= 0) {
      this._output.Add((byte)(value >> 24));
      value <<= 8;
    }

    return this._output.ToArray();
  }

  private static int _FindLeaf(
    ReadOnlySpan<sbyte> tree, int node, int value, Span<int> path, Span<int> bits, int depth) {
    for (var bit = 0; bit < 2; ++bit) {
      var next = tree[node + bit];
      path[depth] = node;
      bits[depth] = bit;

      if (next <= 0) {
        if (-next == value)
          return depth + 1;

        continue;
      }

      var found = _FindLeaf(tree, next, value, path, bits, depth + 1);
      if (found >= 0)
        return found;
    }

    return -1;
  }

  private void _CarryIntoOutput() {
    var at = this._output.Count - 1;
    while (at >= 0 && this._output[at] == byte.MaxValue) {
      this._output[at] = 0;
      --at;
    }

    if (at >= 0)
      ++this._output[at];
  }
}
