using System;
using System.Buffers.Binary;
using FileFormat.Core;

namespace FileFormat.Codecs.Ffv1;

/// <summary>Maps FFV1's numeric samples to the repository's byte-oriented raw pixel formats.</summary>
/// <remarks>
/// FFV1 stores sample values, not a byte order. The raw-image model does: planar YUV P10/P12/P16
/// and Gray10 use right-justified little-endian words, while Gray16/GrayAlpha32/Rgb48/Rgba64 use
/// big-endian words. Keeping that distinction here prevents the entropy and prediction code from
/// acquiring storage-format branches.
/// </remarks>
internal static class Ffv1SampleIO {

  internal static int InferredBitDepth(PixelFormat format) => format switch {
    PixelFormat.Gray8 or PixelFormat.GrayAlpha16
      or PixelFormat.Yuv420P8 or PixelFormat.Yuv422P8 or PixelFormat.Yuv440P8 or PixelFormat.Yuv444P8
      or PixelFormat.Rgb24 or PixelFormat.Rgba32 => 8,
    PixelFormat.Gray10
      or PixelFormat.Yuv420P10 or PixelFormat.Yuv422P10 or PixelFormat.Yuv440P10 or PixelFormat.Yuv444P10 => 10,
    PixelFormat.Yuv420P12 or PixelFormat.Yuv422P12 or PixelFormat.Yuv440P12 or PixelFormat.Yuv444P12 => 12,
    PixelFormat.Gray16 or PixelFormat.GrayAlpha32
      or PixelFormat.Yuv420P16 or PixelFormat.Yuv422P16 or PixelFormat.Yuv440P16 or PixelFormat.Yuv444P16
      or PixelFormat.Rgb48 or PixelFormat.Rgba64 => 16,
    _ => throw new NotSupportedException($"{format} has no integer sample layout supported by FFV1 here."),
  };

  /// <summary>Maximum significant width the raw format can hold without changing the numeric sample.</summary>
  internal static int StorageBitDepth(PixelFormat format) => format switch {
    PixelFormat.Gray8 or PixelFormat.GrayAlpha16
      or PixelFormat.Yuv420P8 or PixelFormat.Yuv422P8 or PixelFormat.Yuv440P8 or PixelFormat.Yuv444P8
      or PixelFormat.Rgb24 or PixelFormat.Rgba32 => 8,
    PixelFormat.Gray10
      or PixelFormat.Yuv420P10 or PixelFormat.Yuv422P10 or PixelFormat.Yuv440P10 or PixelFormat.Yuv444P10 => 10,
    PixelFormat.Yuv420P12 or PixelFormat.Yuv422P12 or PixelFormat.Yuv440P12 or PixelFormat.Yuv444P12 => 12,
    PixelFormat.Gray16 or PixelFormat.GrayAlpha32
      or PixelFormat.Yuv420P16 or PixelFormat.Yuv422P16 or PixelFormat.Yuv440P16 or PixelFormat.Yuv444P16
      or PixelFormat.Rgb48 or PixelFormat.Rgba64 => 16,
    _ => throw new NotSupportedException($"{format} has no integer sample layout supported by FFV1 here."),
  };

  internal static int ResolveBitDepth(PixelFormat format, int requested) {
    var inferred = InferredBitDepth(format);
    if (requested == 0)
      return inferred;

    if (requested is < 8 or > 16)
      throw new ArgumentOutOfRangeException(nameof(requested), requested, "RFC 9043 sample widths handled here are eight through sixteen bits.");

    var storage = StorageBitDepth(format);
    if (requested > storage)
      throw new NotSupportedException($"{format} can hold at most {storage} significant bits per component, not {requested}.");

    // Eight-bit byte layouts cannot truthfully be labelled as narrower or deeper. Word layouts are
    // intentionally useful as generic containers for otherwise-unrepresented 9/11/13/14/15-bit FFV1.
    if (storage == 8 && requested != 8)
      throw new NotSupportedException($"{format} is an eight-bit sample layout and cannot represent an FFV1 stream labelled {requested}-bit.");

    return requested;
  }

  internal static int ReadSample(ReadOnlySpan<byte> data, int sampleIndex, PixelFormat format) {
    if (_IsByteSamples(format))
      return data[sampleIndex];

    var offset = checked(sampleIndex * 2);
    return _IsLittleEndianWords(format)
      ? BinaryPrimitives.ReadUInt16LittleEndian(data[offset..])
      : BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
  }

  internal static void WriteSample(Span<byte> data, int sampleIndex, PixelFormat format, int value) {
    if (_IsByteSamples(format)) {
      data[sampleIndex] = (byte)value;
      return;
    }

    var offset = checked(sampleIndex * 2);
    if (_IsLittleEndianWords(format))
      BinaryPrimitives.WriteUInt16LittleEndian(data[offset..], (ushort)value);
    else
      BinaryPrimitives.WriteUInt16BigEndian(data[offset..], (ushort)value);
  }

  internal static PixelFormat GreyFormat(int bits, bool alpha) {
    if (bits <= 8)
      return alpha ? PixelFormat.GrayAlpha16 : PixelFormat.Gray8;

    if (alpha)
      return PixelFormat.GrayAlpha32;

    return bits <= 10 ? PixelFormat.Gray10 : PixelFormat.Gray16;
  }

  internal static PixelFormat RgbFormat(int bits, bool alpha)
    => bits <= 8
      ? alpha ? PixelFormat.Rgba32 : PixelFormat.Rgb24
      : alpha ? PixelFormat.Rgba64 : PixelFormat.Rgb48;

  internal static PixelFormat YuvFormat(int bits, int horizontalShift, int verticalShift) {
    var depth = bits <= 10 ? 10 : bits <= 12 ? 12 : 16;
    return (horizontalShift, verticalShift, depth) switch {
      (1, 1, 10) => PixelFormat.Yuv420P10,
      (1, 0, 10) => PixelFormat.Yuv422P10,
      (0, 1, 10) => PixelFormat.Yuv440P10,
      (0, 0, 10) => PixelFormat.Yuv444P10,
      (1, 1, 12) => PixelFormat.Yuv420P12,
      (1, 0, 12) => PixelFormat.Yuv422P12,
      (0, 1, 12) => PixelFormat.Yuv440P12,
      (0, 0, 12) => PixelFormat.Yuv444P12,
      (1, 1, 16) => PixelFormat.Yuv420P16,
      (1, 0, 16) => PixelFormat.Yuv422P16,
      (0, 1, 16) => PixelFormat.Yuv440P16,
      (0, 0, 16) => PixelFormat.Yuv444P16,
      _ => throw new NotSupportedException($"FFV1 chroma subsampling 2^{horizontalShift} by 2^{verticalShift} has no planar raw-image representation here."),
    };
  }

  internal static bool UsesWords(PixelFormat format) => !_IsByteSamples(format);

  private static bool _IsByteSamples(PixelFormat format) => format is
    PixelFormat.Gray8 or PixelFormat.GrayAlpha16
    or PixelFormat.Yuv420P8 or PixelFormat.Yuv422P8 or PixelFormat.Yuv440P8 or PixelFormat.Yuv444P8
    or PixelFormat.Rgb24 or PixelFormat.Rgba32;

  private static bool _IsLittleEndianWords(PixelFormat format) => format is
    PixelFormat.Gray10
    or PixelFormat.Yuv420P10 or PixelFormat.Yuv422P10 or PixelFormat.Yuv440P10 or PixelFormat.Yuv444P10
    or PixelFormat.Yuv420P12 or PixelFormat.Yuv422P12 or PixelFormat.Yuv440P12 or PixelFormat.Yuv444P12
    or PixelFormat.Yuv420P16 or PixelFormat.Yuv422P16 or PixelFormat.Yuv440P16 or PixelFormat.Yuv444P16;
}