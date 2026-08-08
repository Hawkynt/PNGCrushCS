using System;
using FileFormat.Core;

namespace FileFormat.Apac3;

/// <summary>In-memory representation of an APAC 3 picture (.ap3, .apv, .dgi, .dgp, .esc, .ilc, .pzm, .app, .ils).</summary>
/// <remarks>
/// APAC shows a Graphics 9 luminance row and a Graphics 11 hue row on alternate scanlines so the
/// two merge into colour. APAC 3 does that twice, on alternate television fields, and lets the eye
/// average the pair as well — so the same picture carries two hues and two luminances for every
/// point, and reaches shades a single APAC screen cannot.
/// <para/>
/// The luminance halves come first, two per stored row; the hue halves follow at an offset the file
/// length gives away, and the two fields take them in opposite order. Which of the several
/// extensions a file carries says nothing about its contents — they are the programs that wrote it,
/// not the format.
/// </remarks>
public readonly record struct Apac3File
  : IImageFormatReader<Apac3File>, IImageToRawImage<Apac3File>,
    IImageFromRawImage<Apac3File>, IImageFormatWriter<Apac3File> {

  /// <summary>Displayed width.</summary>
  public const int Width = 320;

  /// <summary>Displayed height.</summary>
  public const int Height = 192;

  /// <summary>Stored rows in each half; every row covers two scanlines.</summary>
  public const int SourceRows = Height / 2;

  /// <summary>Bytes one field's row occupies.</summary>
  public const int FieldStride = 40;

  /// <summary>Bytes one stored row occupies across both fields.</summary>
  public const int RowStride = FieldStride * 2;

  static string IImageFormatMetadata<Apac3File>.PrimaryExtension => ".ap3";
  static string[] IImageFormatMetadata<Apac3File>.FileExtensions =>
    [".ap3", ".apv", ".dgi", ".dgp", ".esc", ".ilc", ".pzm", ".app", ".ils"];
  static Apac3File IImageFormatReader<Apac3File>.FromSpan(ReadOnlySpan<byte> data) => Apac3Reader.FromSpan(data);
  static byte[] IImageFormatWriter<Apac3File>.ToBytes(Apac3File file) => Apac3Writer.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<Apac3File>.VideoModes => [
    new("APAC 3", [(Width, Height)], [256])
  ];

  /// <summary>The file's bytes, kept whole because the halves are addressed by absolute offset.</summary>
  public byte[] Data { get; init; }

  /// <summary>Offset of the hue halves, which the file length gives away.</summary>
  public int HueOffset { get; init; }

  public static RawImage ToRawImage(Apac3File file) {
    var data = file.Data ?? [];
    var hue = file.HueOffset;

    // The two fields take the luminance and hue halves in opposite order, which is what makes them
    // different pictures rather than the same one shown twice.
    var first = _Field(data, luminanceOffset: 0, hueOffset: hue + FieldStride, hueStartRow: 1);
    var second = _Field(data, luminanceOffset: FieldStride, hueOffset: hue, hueStartRow: 0);

    var gtia = Atari8BitGraphics.Palette;
    var firstRgb = _ToRgb(first, gtia);
    var secondRgb = _ToRgb(second, gtia);

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Rgb24,
      PixelData = Atari8BitGraphics.BlendFrames(firstRgb, secondRgb),
    };
  }

  /// <summary>Builds one field as GTIA colour bytes: hue in the high nibble, luminance in the low.</summary>
  private static byte[] _Field(ReadOnlySpan<byte> data, int luminanceOffset, int hueOffset, int hueStartRow) {
    var frame = new byte[Width * Height];

    // The luminance rows land on every second scanline, starting where the hue rows do not.
    var luminanceStart = 1 - hueStartRow;
    for (var row = 0; row < SourceRows; ++row) {
      var y = row * 2 + luminanceStart;
      for (var x = 0; x < Width; ++x)
        frame[y * Width + x] = (byte)_Nibble(data, luminanceOffset + row * RowStride, x);
    }

    for (var row = 0; row < SourceRows; ++row) {
      var y = row * 2 + hueStartRow;
      for (var x = 0; x < Width; ++x) {
        var c = (byte)(_Nibble(data, hueOffset + row * RowStride, x) << 4);

        // A hue row carries no luminance of its own and takes the average of its neighbours.
        var above = y == 0 ? 0 : frame[(y - 1) * Width + x] & 15;
        var below = y >= Height - 1 ? 0 : frame[(y + 1) * Width + x] & 15;
        frame[y * Width + x] = (byte)(c | ((above + below) >> 1));

        if (y < Height - 1)
          frame[(y + 1) * Width + x] = (byte)(c | (frame[(y + 1) * Width + x] & 15));
      }
    }

    return frame;
  }

  /// <summary>Reads a nibble; each covers four screen pixels, high half of a byte first.</summary>
  private static int _Nibble(ReadOnlySpan<byte> data, int rowOffset, int x) {
    var index = rowOffset + (x >> 3);
    if (index < 0 || index >= data.Length)
      return 0;

    return (x & 4) == 0 ? data[index] >> 4 : data[index] & 15;
  }

  /// <summary>The shortest of the three lengths, which puts the hues straight after the luminances.</summary>
  public const int CompactSize = 15360;

  /// <summary>Where the hues begin in the shortest form.</summary>
  public const int CompactHueOffset = SourceRows * RowStride;

  /// <summary>Encodes a picture as an APAC 3 screen.</summary>
  /// <remarks>
  /// Written in the shortest of the three lengths. The longer one leaves a gap between the halves
  /// that the picture does not use, so a file that had it would be five hundred bytes longer and say
  /// exactly the same thing.
  /// <para/>
  /// The four blocks — two luminance halves and two hue halves — are chosen against each other
  /// rather than one at a time, because a hue row has no luminance of its own and takes the mean of
  /// its neighbours': a nibble therefore reaches four scanlines and choosing it against one of them
  /// makes the other three worse.
  /// </remarks>
  public static Apac3File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(Width, Height).PixelData;
    var streams = ApacInterlaceEncoder.Encode(rgb, Width, Height);
    var nibbles = Width / ApacInterlaceEncoder.PixelsPerNibble;
    var data = new byte[CompactSize];

    // The two fields interleave a row at a time: the luminances first, then the hues, and within
    // each half the second field's row sits between the first field's rows.
    ApacInterlaceEncoder.Pack(streams.FirstLuminance, data, 0, RowStride, SourceRows, nibbles);
    ApacInterlaceEncoder.Pack(streams.SecondLuminance, data, FieldStride, RowStride, SourceRows, nibbles);
    ApacInterlaceEncoder.Pack(streams.SecondHue, data, CompactHueOffset, RowStride, SourceRows, nibbles);
    ApacInterlaceEncoder.Pack(
      streams.FirstHue, data, CompactHueOffset + FieldStride, RowStride, SourceRows, nibbles);

    return new() { Data = data, HueOffset = CompactHueOffset };
  }

  private static byte[] _ToRgb(ReadOnlySpan<byte> frame, ReadOnlySpan<byte> gtia) {
    var rgb = new byte[frame.Length * 3];
    for (var i = 0; i < frame.Length; ++i) {
      var entry = frame[i] * 3;
      rgb[i * 3] = gtia[entry];
      rgb[i * 3 + 1] = gtia[entry + 1];
      rgb[i * 3 + 2] = gtia[entry + 2];
    }

    return rgb;
  }
}
