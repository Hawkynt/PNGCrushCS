using System;

namespace FileFormat.Codecs.Vp8;

/// <summary>
/// The segmentation state: which of four segments a macroblock is in, and what that changes
/// (RFC 6386, 9.3 and 10).
/// </summary>
/// <remarks>
/// A segment adjusts two things and only two: the quantiser index and the loop filter level. Both
/// adjustments are read either as an absolute value or as a delta from the frame's own, which the
/// one <see cref="AbsoluteValues"/> flag decides for all eight of them together.
/// <para/>
/// The state outlives the frame that set it. A frame may leave <see cref="UpdateMap"/> clear and
/// keep the segment each macroblock was assigned to last time, which is the whole point of the
/// feature: an encoder marks a region once and spends nothing on it thereafter. A key frame clears
/// everything, so a stream can be entered at one.
/// </remarks>
internal sealed class Vp8Segmentation {

  internal const int SEGMENT_COUNT = 4;

  internal bool Enabled;
  internal bool UpdateMap;
  internal bool AbsoluteValues;

  internal readonly int[] QuantiserIndex = new int[SEGMENT_COUNT];
  internal readonly int[] LoopFilterLevel = new int[SEGMENT_COUNT];
  internal readonly byte[] TreeProbabilities = [255, 255, 255];

  /// <summary>The segment each macroblock is in, kept from frame to frame until a frame restates it.</summary>
  private byte[] _map = [];

  internal void Reset() {
    this.Enabled = false;
    this.UpdateMap = false;
    this.AbsoluteValues = false;
    Array.Clear(this.QuantiserIndex);
    Array.Clear(this.LoopFilterLevel);
    this.TreeProbabilities[0] = this.TreeProbabilities[1] = this.TreeProbabilities[2] = 255;
    Array.Clear(this._map);
  }

  /// <summary>Makes room for a frame of this many macroblocks, clearing the map if the size changed.</summary>
  internal void Resize(int macroblockCount) {
    if (this._map.Length == macroblockCount)
      return;

    this._map = new byte[macroblockCount];
  }

  internal int this[int macroblock] {
    get => this._map[macroblock];
    set => this._map[macroblock] = (byte)value;
  }

  /// <summary>Reads the segmentation part of a frame header (RFC 6386, 9.3).</summary>
  internal void Parse(ref Vp8BoolDecoder reader) {
    this.Enabled = reader.ReadFlag() != 0;
    if (!this.Enabled) {
      this.UpdateMap = false;
      return;
    }

    this.UpdateMap = reader.ReadFlag() != 0;
    var updateData = reader.ReadFlag() != 0;

    if (updateData) {
      this.AbsoluteValues = reader.ReadFlag() != 0;

      // Every one of the eight is rewritten, not only the ones with a flag set. A quantiser or filter
      // adjustment whose flag is clear becomes zero rather than keeping what a previous frame gave
      // it — which is the opposite of how the loop filter deltas in the very next field behave, and
      // is the one asymmetry in this header worth writing down.
      for (var segment = 0; segment < SEGMENT_COUNT; ++segment)
        this.QuantiserIndex[segment] = _ReadOptionalSignedValue(ref reader, 7);

      for (var segment = 0; segment < SEGMENT_COUNT; ++segment)
        this.LoopFilterLevel[segment] = _ReadOptionalSignedValue(ref reader, 6);
    }

    if (!this.UpdateMap)
      return;

    for (var node = 0; node < this.TreeProbabilities.Length; ++node)
      this.TreeProbabilities[node] = reader.ReadFlag() != 0 ? (byte)reader.ReadLiteral(8) : (byte)255;
  }

  /// <summary>The quantiser index a macroblock of this segment uses, given the frame's own.</summary>
  internal int QuantiserIndexFor(int segment, int frameIndex)
    => !this.Enabled ? frameIndex
      : this.AbsoluteValues ? this.QuantiserIndex[segment]
      : frameIndex + this.QuantiserIndex[segment];

  /// <summary>The loop filter level a macroblock of this segment starts from, given the frame's own.</summary>
  internal int LoopFilterLevelFor(int segment, int frameLevel)
    => !this.Enabled ? frameLevel
      : this.AbsoluteValues ? this.LoopFilterLevel[segment]
      : frameLevel + this.LoopFilterLevel[segment];

  private static int _ReadOptionalSignedValue(ref Vp8BoolDecoder reader, int bits)
    => reader.ReadFlag() != 0 ? reader.ReadSignedValue(bits) : 0;
}
