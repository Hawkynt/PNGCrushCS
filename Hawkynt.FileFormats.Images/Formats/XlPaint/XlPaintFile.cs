using System;
using FileFormat.Core;

namespace FileFormat.XlPaint;

/// <summary>In-memory representation of an XL-Paint picture (.xlp).</summary>
/// <remarks>
/// Two Graphics 15 screens shown alternately and averaged, packed together into one run-length
/// stream laid out column by column. Packing down columns rather than across rows is deliberate:
/// the two interlaced screens differ from each other far more than a screen differs from itself
/// vertically, so a column of one screen is the longest run there is to find.
/// <para/>
/// Later files carry a marker and a header; earlier ones carry neither, and the only way to tell a
/// 200-row picture from a 192-row one is to unpack it and see which length the stream fills.
/// </remarks>
public readonly record struct XlPaintFile
  : IImageFormatReader<XlPaintFile>, IImageToRawImage<XlPaintFile> {

  /// <summary>Screen pixels across.</summary>
  public const int Width = 320;

  /// <summary>Bytes one row of one screen occupies.</summary>
  public const int Stride = Width / 8;

  /// <summary>The text later files start with.</summary>
  public const string Signature = "XLPC";

  static string IImageFormatMetadata<XlPaintFile>.PrimaryExtension => ".xlp";
  static string[] IImageFormatMetadata<XlPaintFile>.FileExtensions => [".xlp"];
  static XlPaintFile IImageFormatReader<XlPaintFile>.FromSpan(ReadOnlySpan<byte> data)
    => XlPaintReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<XlPaintFile>.VideoModes => [
    new("XL-Paint", [(Width, 192), (Width, 200)], [10])
  ];

  /// <summary>Both unpacked screens, one after the other.</summary>
  public byte[] ScreenData { get; init; }

  /// <summary>Rows.</summary>
  public int Height { get; init; }

  /// <summary>The registers both screens draw from: background, PF0, PF1 and PF2.</summary>
  public byte[] Registers { get; init; }

  public static RawImage ToRawImage(XlPaintFile file) {
    var data = file.ScreenData ?? [];
    var registers = file.Registers ?? [];

    var first = Atari8BitGraphics.DecodeGr15Frame(data, 0, Stride, Width, file.Height, registers);
    var second = Atari8BitGraphics.DecodeGr15Frame(data, file.Height * Stride, Stride, Width, file.Height, registers);

    return new() {
      Width = Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = FrameBlend.Average(first, second),
    };
  }
}
