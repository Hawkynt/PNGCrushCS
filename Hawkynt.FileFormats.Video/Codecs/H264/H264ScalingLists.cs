using System;
using System.IO;

namespace FileFormat.Codecs.H264;

/// <summary>
/// The six 4x4 and two luma 8x8 scaling lists used by an 8-bit 4:2:0 H.264 picture.
/// Values are stored by coefficient position, not syntax scan index.
/// </summary>
internal sealed class H264ScalingLists {

  internal static readonly byte[] DefaultIntra4x4 = [
    6, 13, 20, 28,
    13, 20, 28, 32,
    20, 28, 32, 37,
    28, 32, 37, 42,
  ];

  internal static readonly byte[] DefaultInter4x4 = [
    10, 14, 20, 24,
    14, 20, 24, 27,
    20, 24, 27, 30,
    24, 27, 30, 34,
  ];

  internal static readonly byte[] DefaultIntra8x8 = [
    6, 10, 13, 16, 18, 23, 25, 27,
    10, 11, 16, 18, 23, 25, 27, 29,
    13, 16, 18, 23, 25, 27, 29, 31,
    16, 18, 23, 25, 27, 29, 31, 33,
    18, 23, 25, 27, 29, 31, 33, 36,
    23, 25, 27, 29, 31, 33, 36, 38,
    25, 27, 29, 31, 33, 36, 38, 40,
    27, 29, 31, 33, 36, 38, 40, 42,
  ];

  internal static readonly byte[] DefaultInter8x8 = [
    9, 13, 15, 17, 19, 21, 22, 24,
    13, 13, 17, 19, 21, 22, 24, 25,
    15, 17, 19, 21, 22, 24, 25, 27,
    17, 19, 21, 22, 24, 25, 27, 28,
    19, 21, 22, 24, 25, 27, 28, 30,
    21, 22, 24, 25, 27, 28, 30, 32,
    22, 24, 25, 27, 28, 30, 32, 33,
    24, 25, 27, 28, 30, 32, 33, 35,
  ];

  /// <summary>Table 8-14 / Figure 8-10 scan for 8x8 scaling lists and residual levels.</summary>
  internal static readonly int[] ZigZagScan8x8 = [
    0, 1, 8, 16, 9, 2, 3, 10,
    17, 24, 32, 25, 18, 11, 4, 5,
    12, 19, 26, 33, 40, 48, 41, 34,
    27, 20, 13, 6, 7, 14, 21, 28,
    35, 42, 49, 56, 57, 50, 43, 36,
    29, 22, 15, 23, 30, 37, 44, 51,
    58, 59, 52, 45, 38, 31, 39, 46,
    53, 60, 61, 54, 47, 55, 62, 63,
  ];

  private readonly byte[][] _fourByFour;
  private readonly byte[][] _eightByEight;

  private H264ScalingLists(byte[][] fourByFour, byte[][] eightByEight) {
    this._fourByFour = fourByFour;
    this._eightByEight = eightByEight;
  }

  internal ReadOnlySpan<byte> FourByFour(int index) => this._fourByFour[index];

  /// <param name="intra">True for list 6 (Intra Y), false for list 7 (Inter Y).</param>
  internal ReadOnlySpan<byte> EightByEight(bool intra) => this._eightByEight[intra ? 0 : 1];

  internal static H264ScalingLists Flat() {
    var four = new byte[6][];
    for (var i = 0; i < four.Length; ++i) {
      four[i] = new byte[16];
      Array.Fill(four[i], (byte)16);
    }

    var eight = new byte[2][];
    for (var i = 0; i < eight.Length; ++i) {
      eight[i] = new byte[64];
      Array.Fill(eight[i], (byte)16);
    }

    return new(four, eight);
  }

  /// <summary>Parses seq_scaling_matrix_present_flag's lists, including fallback rule A.</summary>
  internal static H264ScalingLists ParseSequence(ref H264BitReader reader, int chromaFormatIdc) {
    var four = new byte[6][];
    four[0] = _ReadOrFallback(ref reader, 16, DefaultIntra4x4, DefaultIntra4x4);
    four[1] = _ReadOrFallback(ref reader, 16, DefaultIntra4x4, four[0]);
    four[2] = _ReadOrFallback(ref reader, 16, DefaultIntra4x4, four[1]);
    four[3] = _ReadOrFallback(ref reader, 16, DefaultInter4x4, DefaultInter4x4);
    four[4] = _ReadOrFallback(ref reader, 16, DefaultInter4x4, four[3]);
    four[5] = _ReadOrFallback(ref reader, 16, DefaultInter4x4, four[4]);

    var eight = new byte[2][];
    eight[0] = _ReadOrFallback(ref reader, 64, DefaultIntra8x8, DefaultIntra8x8);
    eight[1] = _ReadOrFallback(ref reader, 64, DefaultInter8x8, DefaultInter8x8);

    // 4:4:4 carries four additional chroma 8x8 lists. They are parsed so the SPS remains aligned,
    // but this decoder still refuses 4:4:4 reconstruction at the geometry boundary.
    if (chromaFormatIdc == 3)
      for (var i = 0; i < 4; ++i)
        _ = _ReadOrFallback(
          ref reader, 64, (i & 1) == 0 ? DefaultIntra8x8 : DefaultInter8x8,
          (i & 1) == 0 ? eight[0] : eight[1]);

    return new(four, eight);
  }

  /// <summary>Parses the optional PPS lists without resolving absent-list fallback B yet.</summary>
  internal static H264ScalingListOverrides ParsePictureOverrides(ref H264BitReader reader, bool transform8x8) {
    var lists = new byte[]?[8];
    for (var i = 0; i < 6; ++i)
      lists[i] = _ReadOptional(ref reader, 16, i < 3 ? DefaultIntra4x4 : DefaultInter4x4);

    if (transform8x8) {
      lists[6] = _ReadOptional(ref reader, 64, DefaultIntra8x8);
      lists[7] = _ReadOptional(ref reader, 64, DefaultInter8x8);
    }

    return new(lists);
  }

  internal static H264ScalingLists ResolvePicture(
    H264ScalingListOverrides? overrides,
    H264ScalingLists sequence,
    bool sequenceMatrixPresent) {
    if (overrides == null)
      return sequence;

    var four = new byte[6][];
    four[0] = overrides[0] ?? _Copy(sequenceMatrixPresent ? sequence.FourByFour(0) : DefaultIntra4x4);
    four[1] = overrides[1] ?? _Copy(four[0]);
    four[2] = overrides[2] ?? _Copy(four[1]);
    four[3] = overrides[3] ?? _Copy(sequenceMatrixPresent ? sequence.FourByFour(3) : DefaultInter4x4);
    four[4] = overrides[4] ?? _Copy(four[3]);
    four[5] = overrides[5] ?? _Copy(four[4]);

    var eight = new byte[2][];
    eight[0] = overrides[6] ?? _Copy(sequenceMatrixPresent ? sequence.EightByEight(true) : DefaultIntra8x8);
    eight[1] = overrides[7] ?? _Copy(sequenceMatrixPresent ? sequence.EightByEight(false) : DefaultInter8x8);
    return new(four, eight);
  }

  private static byte[] _ReadOrFallback(
    ref H264BitReader reader, int size, ReadOnlySpan<byte> useDefault, ReadOnlySpan<byte> absentFallback) {
    if (reader.ReadBit() == 0)
      return _Copy(absentFallback);

    return _ReadList(ref reader, size, useDefault);
  }

  private static byte[]? _ReadOptional(ref H264BitReader reader, int size, ReadOnlySpan<byte> useDefault) {
    if (reader.ReadBit() == 0)
      return null;
    return _ReadList(ref reader, size, useDefault);
  }

  private static byte[] _ReadList(ref H264BitReader reader, int size, ReadOnlySpan<byte> useDefault) {
    var result = new byte[size];
    var last = 8;
    var next = 8;
    var scan = size == 16 ? H264Transform.ZigZagScan4x4 : ZigZagScan8x8;

    for (var i = 0; i < size; ++i) {
      if (next != 0) {
        var delta = reader.ReadSignedExpGolomb();
        if (delta is < -128 or > 127)
          throw new InvalidDataException(
            $"An H.264 scaling list states delta_scale {delta}, outside the -128..127 syntax range.");
        next = (last + delta + 256) & 255;
      }

      if (i == 0 && next == 0)
        return _Copy(useDefault);

      last = next == 0 ? last : next;
      result[scan[i]] = (byte)last;
    }

    return result;
  }

  private static byte[] _Copy(ReadOnlySpan<byte> source) => source.ToArray();
}

/// <summary>Presence-sensitive PPS scaling-list syntax, resolved only once its SPS is known.</summary>
internal sealed class H264ScalingListOverrides {
  private readonly byte[]?[] _lists;
  internal H264ScalingListOverrides(byte[]?[] lists) => this._lists = lists;
  internal byte[]? this[int index] => this._lists[index] is { } list ? (byte[])list.Clone() : null;
}
