using System;
using FileFormat.Core;

namespace FileFormat.NokiaNlm;

/// <summary>In-memory representation of a Nokia Logo Manager image.</summary>
/// <remarks>
/// Ten bytes of header and then the bitmap packed a bit a pixel. The size is stated in single bytes,
/// which is what limits one of these to 255 pixels a side — the phones it was written for had rather
/// less than that.
/// <para/>
/// It used to be written here as a bare bitmap with no header, locked to 84 by 48. Neither is real:
/// the header is what tells the size, and the size is whatever the header says.
/// </remarks>
public readonly record struct NokiaNlmFile
  : IImageFormatReader<NokiaNlmFile>, IImageToRawImage<NokiaNlmFile>,
    IImageFromRawImage<NokiaNlmFile>, IImageFormatWriter<NokiaNlmFile> {

  /// <summary>Bytes before the bitmap.</summary>
  internal const int HeaderSize = 10;

  /// <summary>The four characters every file starts with, the last being a space.</summary>
  internal const string Signature = "NLM ";

  /// <summary>Where the size sits, a byte each.</summary>
  internal const int WidthOffset = 7;

  internal const int HeightOffset = 8;

  /// <summary>The most either side can be, the field holding one byte.</summary>
  internal const int MaxSide = 255;

  private static readonly byte[] _BlackWhitePalette = [255, 255, 255, 0, 0, 0];

  static string IImageFormatMetadata<NokiaNlmFile>.PrimaryExtension => ".nlm";
  static string[] IImageFormatMetadata<NokiaNlmFile>.FileExtensions => [".nlm"];
  static NokiaNlmFile IImageFormatReader<NokiaNlmFile>.FromSpan(ReadOnlySpan<byte> data) => NokiaNlmReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<NokiaNlmFile>.VideoModes => [
    new("Default", [(new IntegerRange(1, MaxSide), new IntegerRange(1, MaxSide))], [2])
  ];
  static byte[] IImageFormatWriter<NokiaNlmFile>.ToBytes(NokiaNlmFile file) => NokiaNlmWriter.ToBytes(file);

  public int Width { get; init; }

  public int Height { get; init; }

  /// <summary>One index a pixel, zero for paper and one for ink.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Bytes one row of the packed bitmap takes.</summary>
  internal int Stride => (this.Width + 7) / 8;

  public static RawImage ToRawImage(NokiaNlmFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Indexed8,
    PixelData = file.PixelData[..],
    Palette = _BlackWhitePalette[..],
    PaletteCount = 2,
  };

  public static NokiaNlmFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    // A side is one byte, so anything larger has to be brought down to fit rather than refused.
    var width = Math.Min(image.Width, MaxSide);
    var height = Math.Min(image.Height, MaxSide);
    if (width != image.Width || height != image.Height)
      image = image.SampleTo(width, height);

    return new() {
      Width = width,
      Height = height,
      PixelData = BilevelRows.Threshold(image, setWhenDark: true),
    };
  }
}
