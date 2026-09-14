using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs.UtVideo;

/// <summary>The stream-level layout of the Ut Video T2 family.</summary>
/// <remarks>
/// T2 is not the Huffman bitstream used by the older <c>UL*</c> family. Its sixteen-byte stream
/// description names an eight-symbol packing mode, a horizontal band count, and whether frames may
/// refer to their predecessor. The six public T2 codes cover RGB(A), 4:2:2 and 4:4:4; there is no T2
/// 4:2:0 code.
/// </remarks>
internal sealed class UtVideoT2Format {

  internal const int ExtraSize = 16;
  internal const byte EncodingMode8SymbolPack = 2;
  internal const byte UseControlCompressionFlag = 1;
  internal const byte UseTemporalCompressionFlag = 2;

  private UtVideoT2Format(
    UtVideoColourSpace colourSpace, int planeCount, int chromaHorizontalShift, bool isBt709,
    int sliceCount, bool useControlCompression, bool useTemporalCompression) {
    this.ColourSpace = colourSpace;
    this.PlaneCount = planeCount;
    this.ChromaHorizontalShift = chromaHorizontalShift;
    this.IsBt709 = isBt709;
    this.SliceCount = sliceCount;
    this.UseControlCompression = useControlCompression;
    this.UseTemporalCompression = useTemporalCompression;
  }

  internal UtVideoColourSpace ColourSpace { get; }
  internal int PlaneCount { get; }
  internal int ChromaHorizontalShift { get; }
  internal bool IsBt709 { get; }
  internal int SliceCount { get; }
  internal bool UseControlCompression { get; }
  internal bool UseTemporalCompression { get; }
  internal bool HasAlpha => this.ColourSpace == UtVideoColourSpace.Rgba;

  internal static bool IsTag(CodecTag codec) => _TryLayout(codec.ToString(), out _);

  internal static UtVideoT2Format Parse(CodecTag codec, ReadOnlySpan<byte> extra, int streamIndex) {
    if (!_TryLayout(codec.ToString(), out var layout))
      throw new NotSupportedException($"Video stream {streamIndex} is named {codec}, which is not a Ut Video T2 code.");

    if (extra.Length < ExtraSize)
      throw new InvalidDataException(
        $"Video stream {streamIndex} is {codec} but carries {extra.Length} bytes of T2 stream description, where {ExtraSize} are required.");

    if (extra[8] != EncodingMode8SymbolPack)
      throw new NotSupportedException(
        $"Video stream {streamIndex} is {codec} with T2 encoding mode {extra[8]}; this decoder implements the documented eight-symbol packing mode {EncodingMode8SymbolPack}.");

    var flags = extra[10];
    if ((flags & ~(UseControlCompressionFlag | UseTemporalCompressionFlag)) != 0)
      throw new InvalidDataException(
        $"Video stream {streamIndex} sets reserved Ut Video T2 stream flags 0x{flags & 0xFC:X2}.");

    for (var i = 11; i < ExtraSize; ++i)
      if (extra[i] != 0)
        throw new InvalidDataException(
          $"Video stream {streamIndex} has non-zero data in reserved Ut Video T2 stream-description byte {i}.");

    var slices = extra[9] + 1;
    return new(
      layout.ColourSpace,
      layout.Planes,
      layout.HorizontalShift,
      layout.Bt709,
      slices,
      (flags & UseControlCompressionFlag) != 0,
      (flags & UseTemporalCompressionFlag) != 0);
  }

  internal static UtVideoT2Format ForEncoding(CodecTag codec, int sliceCount, bool temporal, int streamIndex) {
    if (!_TryLayout(codec.ToString(), out var layout))
      throw new NotSupportedException($"Video stream {streamIndex} asks for {codec}, which is not a Ut Video T2 code.");
    if (sliceCount is < 1 or > 256)
      throw new ArgumentOutOfRangeException(nameof(sliceCount), sliceCount, "A Ut Video T2 stream has from one to 256 bands.");

    return new(
      layout.ColourSpace,
      layout.Planes,
      layout.HorizontalShift,
      layout.Bt709,
      sliceCount,
      temporal,
      temporal);
  }

  internal byte[] Describe() {
    var result = new byte[ExtraSize];

    // A conventional encoder/version value. T2 decoders do not use it to interpret the packet.
    result[0] = 0xF0;
    result[3] = 0x01;
    result[8] = EncodingMode8SymbolPack;
    result[9] = checked((byte)(this.SliceCount - 1));
    result[10] = (byte)(
      (this.UseControlCompression ? UseControlCompressionFlag : 0)
      | (this.UseTemporalCompression ? UseTemporalCompressionFlag : 0));
    return result;
  }

  /// <summary>Returns the first frame row in one independently coded horizontal band.</summary>
  internal int SliceStart(int index, int frameHeight) => frameHeight * index / this.SliceCount;

  /// <summary>The active sample width of one internal plane.</summary>
  internal int PlaneWidth(int plane, int frameWidth)
    => this.ColourSpace == UtVideoColourSpace.Yuv && plane is 1 or 2
      ? frameWidth >> this.ChromaHorizontalShift
      : frameWidth;

  /// <summary>T2 rounds every internal plane row to a whole 64 bytes before packing it.</summary>
  internal int PlaneStride(int plane, int frameWidth) {
    var width = this.PlaneWidth(plane, frameWidth);
    return checked((width + 63) & ~63);
  }

  private static bool _TryLayout(
    string name,
    out (UtVideoColourSpace ColourSpace, int Planes, int HorizontalShift, bool Bt709) layout) {
    layout = name.ToUpperInvariant() switch {
      "UMRG" => (UtVideoColourSpace.Rgb, 3, 0, false),
      "UMRA" => (UtVideoColourSpace.Rgba, 4, 0, false),
      "UMY2" => (UtVideoColourSpace.Yuv, 3, 1, false),
      "UMY4" => (UtVideoColourSpace.Yuv, 3, 0, false),
      "UMH2" => (UtVideoColourSpace.Yuv, 3, 1, true),
      "UMH4" => (UtVideoColourSpace.Yuv, 3, 0, true),
      _ => default,
    };

    return name.Length == 4 && name[0] is 'U' or 'u' && name[1] is 'M' or 'm'
      && layout.Planes != 0;
  }
}
