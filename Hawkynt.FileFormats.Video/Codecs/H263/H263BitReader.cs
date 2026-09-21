using System;
using System.IO;

namespace FileFormat.Codecs.H263;

/// <summary>
/// Reads an H.263 bitstream: most significant bit first, with no escaping of any kind.
/// </summary>
/// <remarks>
/// Start codes are found by looking at bits rather than at bytes, which is the difference between
/// this and the MPEG-1 reader beside it. ITU-T H.263 5.2.1 has an encoder put fewer than eight zero
/// bits in front of a group-of-blocks start code so that the code itself lands on a byte boundary,
/// and those stuffing bits are indistinguishable from the leading zeroes of the code. So a search
/// that aligned first would have to know how much stuffing there was before it could align, and one
/// that looks for sixteen zeroes followed by a one finds the code from wherever the macroblock layer
/// left the position. That search is safe because H.263 forbids a coded macroblock from producing
/// sixteen consecutive zeroes (5.2.2), which is the same guarantee that lets a start code be found
/// at all.
/// <para/>
/// <see cref="NextBits"/> pads with zeroes past the end of the data rather than throwing. Every
/// variable-length code is read by peeking the width of the longest code in its table and consuming
/// only what matched, so the last code of a picture always peeks past the end; the bits that are not
/// there are zero, and a code that appeared to match on them is one the table refuses by name.
/// </remarks>
internal ref struct H263BitReader {

  private readonly ReadOnlySpan<byte> _data;
  private readonly bool _realVideoExtendedEscapeLevel;
  private int _bitPosition;

  private bool _hasRealVideoPredictiveIntraDc;
  private int _realVideoIntraBlock;
  private byte _realVideoFirstDcMask;
  private int _realVideoLumaDc;
  private int _realVideoCbDc;
  private int _realVideoCrDc;

  public H263BitReader(ReadOnlySpan<byte> data, bool realVideoExtendedEscapeLevel = false) {
    this._data = data;
    this._realVideoExtendedEscapeLevel = realVideoExtendedEscapeLevel;
    this._bitPosition = 0;
    this._hasRealVideoPredictiveIntraDc = false;
    this._realVideoIntraBlock = 0;
    this._realVideoFirstDcMask = 0;
    this._realVideoLumaDc = 0;
    this._realVideoCbDc = 0;
    this._realVideoCrDc = 0;
  }

  /// <summary>The bit the next read will take, counted from the first bit of the first byte.</summary>
  public readonly int BitPosition => this._bitPosition;

  /// <summary>How many bits are left.</summary>
  public readonly int BitsRemaining => (this._data.Length << 3) - this._bitPosition;

  /// <summary>
  /// Whether the H.263-compatible block syntax uses RealVideo 1's extension of the escape level.
  /// </summary>
  internal readonly bool HasRealVideoExtendedEscapeLevel => this._realVideoExtendedEscapeLevel;

  /// <summary>Whether intra DC values are being reconstructed with RealVideo 1's predictive VLC.</summary>
  internal readonly bool HasRealVideoPredictiveIntraDc => this._hasRealVideoPredictiveIntraDc;

  /// <summary>
  /// Starts RealVideo 1's predictive intra-DC coding for one independently coded run.
  /// </summary>
  /// <remarks>
  /// A non-zero RV10 micro revision seeds Y, Cb and Cr in the run header. The first block of each
  /// component consumes no DC bits at all and uses its seed; every later block carries a VLC-coded
  /// difference which wraps modulo 256. Keeping that state in the reader matters because the block
  /// decoder is deliberately shared with ordinary H.263, whose INTRADC is instead one literal byte
  /// per block.
  /// </remarks>
  internal void UseRealVideoPredictiveIntraDc(int luma, int cb, int cr) {
    if ((uint)luma > byte.MaxValue || (uint)cb > byte.MaxValue || (uint)cr > byte.MaxValue)
      throw new ArgumentOutOfRangeException(nameof(luma), "RealVideo intra-DC predictors are eight-bit values.");

    this._hasRealVideoPredictiveIntraDc = true;
    this._realVideoIntraBlock = 0;
    this._realVideoFirstDcMask = 0;
    this._realVideoLumaDc = luma;
    this._realVideoCbDc = cb;
    this._realVideoCrDc = cr;
  }

  /// <summary>
  /// Reads the DC value of one intra block in the syntax selected for this bitstream.
  /// </summary>
  internal int ReadIntraDc() {
    if (!this._hasRealVideoPredictiveIntraDc)
      return this.ReadBits(8);

    var block = this._realVideoIntraBlock++ % 6;
    var component = block < 4 ? 0 : block - 3;
    var mask = (byte)(1 << component);
    var value = component switch {
      0 => this._realVideoLumaDc,
      1 => this._realVideoCbDc,
      _ => this._realVideoCrDc,
    };

    if ((this._realVideoFirstDcMask & mask) == 0) {
      this._realVideoFirstDcMask |= mask;
      return value;
    }

    value = (value + this._ReadRealVideoDcDifference(component == 0)) & 0xFF;
    switch (component) {
      case 0: this._realVideoLumaDc = value; break;
      case 1: this._realVideoCbDc = value; break;
      default: this._realVideoCrDc = value; break;
    }

    return value;
  }

  /// <summary>Takes one bit.</summary>
  public int ReadBit() {
    var position = this._bitPosition;
    if (position >= this._data.Length << 3)
      throw new InvalidDataException("The H.263 bitstream ended in the middle of a syntax element.");

    this._bitPosition = position + 1;
    return (this._data[position >> 3] >> (7 - (position & 7))) & 1;
  }

  /// <summary>Takes <paramref name="count"/> bits as an unsigned number, most significant first.</summary>
  public int ReadBits(int count) {
    if (count == 0)
      return 0;

    if (this._bitPosition + count > this._data.Length << 3)
      throw new InvalidDataException(
        $"The H.263 bitstream ended {count - this.BitsRemaining} bit(s) short of a {count}-bit field.");

    var value = 0;
    for (var i = 0; i < count; ++i) {
      var position = this._bitPosition + i;
      value = (value << 1) | ((this._data[position >> 3] >> (7 - (position & 7))) & 1);
    }

    this._bitPosition += count;
    return value;
  }

  /// <summary>
  /// Looks at the next <paramref name="count"/> bits without consuming them, padding with zeroes past
  /// the end of the data.
  /// </summary>
  public readonly int NextBits(int count) {
    var value = 0;
    var limit = this._data.Length << 3;
    for (var i = 0; i < count; ++i) {
      var position = this._bitPosition + i;
      var bit = position < limit ? (this._data[position >> 3] >> (7 - (position & 7))) & 1 : 0;
      value = (value << 1) | bit;
    }

    return value;
  }

  /// <summary>Drops <paramref name="count"/> bits.</summary>
  public void Skip(int count) => this._bitPosition += count;

  /// <summary>Moves the position, which the start-code search needs in order to put one back.</summary>
  public void SeekToBit(int position) => this._bitPosition = position;

  /// <summary>
  /// Whether a start code begins here, allowing for the stuffing bits in front of it.
  /// </summary>
  /// <remarks>
  /// Sixteen zeroes is the test and not the whole seventeen-bit code, because the stuffing of H.263
  /// 5.2.1 is itself zeroes: between nought and seven of them sit in front of the code, so the number
  /// of zeroes before the terminating one is not known until it is found. Sixteen is enough to decide
  /// — no coded macroblock may produce that many in a row — and where the code really is follows from
  /// scanning to the one.
  /// </remarks>
  public readonly bool AtStartCode() => this.BitsRemaining >= 17 && this.NextBits(16) == 0;

  /// <summary>
  /// Consumes the stuffing and the seventeen-bit start code, leaving the position at the code's
  /// five-bit group number.
  /// </summary>
  public void ConsumeStartCode() {
    var zeroes = 0;
    while (this.BitsRemaining > 0 && this.ReadBit() == 0)
      ++zeroes;

    if (zeroes < 16)
      throw new InvalidDataException(
        $"An H.263 start code was expected but only {zeroes} zero bit(s) preceded the terminating one; the start code "
        + "of ITU-T H.263 5.2.2 is sixteen zeroes and a one.");
  }

  /// <summary>
  /// Reads the canonical RealVideo 1 DC VLC used by non-zero RV10 micro revisions.
  /// </summary>
  /// <remarks>
  /// The length counts and run-compressed symbol ordering are interoperability data from RealVideo's
  /// public decoder behaviour. Codes are generated canonically here instead of copying a generated
  /// lookup table. The two all-one prefixes are the format's deliberately redundant -1/255 escape
  /// spellings; their remaining bits are ignored but still consumed.
  /// </remarks>
  private int _ReadRealVideoDcDifference(bool luminance) {
    ReadOnlySpan<ushort> counts = luminance
      ? [1, 0, 2, 4, 8, 16, 32, 0, 64, 0, 128, 0, 256, 0, 512]
      : [1, 2, 4, 0, 8, 0, 16, 0, 32, 0, 64, 0, 128, 0, 256];

    var code = this.ReadBits(2);
    var firstCode = 0;
    var firstSymbol = 0;

    for (var length = 2; length <= 16; ++length) {
      if (luminance && length == 7 && code == 0x7F) {
        _ = this.ReadBits(11);
        return 255;
      }

      if (!luminance && length == 9 && code == 0x1FE) {
        _ = this.ReadBits(9);
        return 255;
      }

      var count = counts[length - 2];
      var offset = code - firstCode;
      if ((uint)offset < count)
        return _RealVideoDcSymbol(firstSymbol + offset, luminance);

      firstSymbol += count;
      if (length == 16)
        break;

      firstCode = (firstCode + count) << 1;
      code = (code << 1) | this.ReadBit();
    }

    throw new InvalidDataException("The RealVideo 1 predictive intra-DC field contains no valid VLC codeword.");
  }

  private static int _RealVideoDcSymbol(int index, bool luminance) {
    ReadOnlySpan<byte> runs = [
      0, 0, 1, 0, 255, 0, 3, 1, 254, 1,
      7, 3, 252, 3, 15, 7, 248, 7, 31, 15,
      240, 15, 63, 31, 224, 31, 127, 63, 192, 63,
      255, 127, 128, 127, 127, 255, 128, 255,
    ];

    var pairCount = runs.Length / 2 - (luminance ? 0 : 2);
    for (var pair = 0; pair < pairCount; ++pair) {
      var count = runs[pair * 2 + 1] + 1;
      if (index < count)
        return (runs[pair * 2] - index) & 0xFF;

      index -= count;
    }

    throw new InvalidDataException("The RealVideo 1 predictive intra-DC VLC resolved outside its symbol table.");
  }
}