using System;

namespace FileFormat.Codecs.Indeo;

/// <summary>
/// Reads the two-bit codes of an Indeo 3 plane's binary tree, most-significant-bit first.
/// </summary>
/// <remarks>
/// A plane holds two things at once and this reads one of them. The tree that says how the plane is cut
/// into cells is a bit stream; the motion vectors and the coded cells the tree's leaves name are whole
/// bytes taken from the same buffer at the point the tree has reached. The two interleave, so the
/// reader has to be able to say which byte the bit position corresponds to — see
/// <see cref="NextByteIndex"/> — and to be pushed past bytes something else consumed.
/// <para/>
/// Indices are into the whole frame rather than into the plane, because that is what the cell data
/// pointers are: the tree, the vectors and the cells all address the same buffer.
/// </remarks>
internal ref struct Indeo3BitReader {

  private readonly ReadOnlySpan<byte> _frame;
  private readonly int _start;
  private readonly int _bitCount;
  private int _position;

  /// <summary>Starts a reader over a plane's bits.</summary>
  /// <param name="frame">The whole frame.</param>
  /// <param name="start">The index in it where this plane's bits begin.</param>
  /// <param name="bitCount">How many bits of it belong to the plane.</param>
  internal Indeo3BitReader(ReadOnlySpan<byte> frame, int start, int bitCount) {
    this._frame = frame;
    this._start = start;
    this._bitCount = bitCount;
    this._position = 0;
  }

  /// <summary>How many bits of the plane have not been read yet.</summary>
  internal readonly int BitsLeft => this._bitCount - this._position;

  /// <summary>Whether the next bit starts a byte, which is when a pending skip may be taken.</summary>
  internal readonly bool IsByteAligned => (this._position & 7) == 0;

  /// <summary>The index of the first whole byte at or after the bit position.</summary>
  internal readonly int NextByteIndex => this._start + ((this._position + 7) >> 3);

  /// <summary>
  /// Reads the next few bits, the first one read as the most significant.
  /// </summary>
  /// <remarks>
  /// Past the plane's own end the frame is still read, and past the frame's end the bits are zero.
  /// Both are what the tree wants: a plane's bit count is where its tree is <i>expected</i> to finish
  /// and the cell data that follows it is in the same buffer, so a read that runs a little past the
  /// count is reading real bytes rather than falling off anything. Where the tree is genuinely
  /// exhausted the callers test <see cref="BitsLeft"/>, which is the check that ends a plane.
  /// </remarks>
  internal int ReadBits(int count) {
    var value = 0;
    for (var i = 0; i < count; ++i) {
      var at = this._start + ((this._position + i) >> 3);
      var bit = at < this._frame.Length ? (this._frame[at] >> (7 - ((this._position + i) & 7))) & 1 : 0;
      value = (value << 1) | bit;
    }

    this._position += count;
    return value;
  }

  /// <summary>Advances past bits something other than the tree has consumed.</summary>
  /// <remarks>
  /// Not bounds-checked on purpose: the tree is what decides when the plane is finished, and a skip
  /// that lands past the end simply leaves nothing to read, which the loops already test for.
  /// </remarks>
  internal void Skip(int count) => this._position += count;
}
