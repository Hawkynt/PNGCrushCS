using System;
using FileFormat.Core;

namespace FileFormat.Miff;

/// <summary>In-memory representation of a MIFF (ImageMagick) image.</summary>
[FormatMagicBytes([0x69, 0x64, 0x3D, 0x49, 0x6D, 0x61, 0x67, 0x65, 0x4D, 0x61, 0x67, 0x69, 0x63, 0x6B])]
public readonly record struct MiffFile : IImageFormatReader<MiffFile>, IImageToRawImage<MiffFile>, IImageFromRawImage<MiffFile>, IImageFormatWriter<MiffFile> {

  static string IImageFormatMetadata<MiffFile>.PrimaryExtension => ".miff";
  static string[] IImageFormatMetadata<MiffFile>.FileExtensions => [".miff", ".mif"];
  static MiffFile IImageFormatReader<MiffFile>.FromSpan(ReadOnlySpan<byte> data) => MiffReader.FromSpan(data);
  static byte[] IImageFormatWriter<MiffFile>.ToBytes(MiffFile file) => MiffWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public int Depth { get; init; }
  public MiffColorClass ColorClass { get; init; }
  public MiffCompression Compression { get; init; }
  public string Colorspace { get; init; }
  public string Type { get; init; }
  public byte[] PixelData { get; init; }
  public byte[]? Palette { get; init; }

  public static RawImage ToRawImage(MiffFile file) {

    if (file.ColorClass == MiffColorClass.PseudoClass && file.Palette != null)
      return new() {
        Width = file.Width,
        Height = file.Height,
        Format = PixelFormat.Indexed8,
        PixelData = file.PixelData[..],
        Palette = file.Palette[..],
        PaletteCount = file.Palette.Length / 3,
      };

    var format = file.Type switch {
      "TrueColorAlpha" or "TrueColorMatte" => PixelFormat.Rgba32,
      "Grayscale" => PixelFormat.Gray8,
      _ => PixelFormat.Rgb24,
    };

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = format,
      PixelData = file.PixelData[..],
    };
  }

  public static MiffFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    switch (image.Format) {
      case PixelFormat.Rgb24:
        return new() {
          Width = image.Width,
          Height = image.Height,
          Depth = 8,
          ColorClass = MiffColorClass.DirectClass,
          Type = "TrueColor",
          PixelData = image.PixelData[..],
        };
      case PixelFormat.Rgba32:
        return new() {
          Width = image.Width,
          Height = image.Height,
          Depth = 8,
          ColorClass = MiffColorClass.DirectClass,
          Type = "TrueColorAlpha",
          PixelData = image.PixelData[..],
        };
      case PixelFormat.Gray8:
        return new() {
          Width = image.Width,
          Height = image.Height,
          Depth = 8,
          ColorClass = MiffColorClass.DirectClass,
          Type = "Grayscale",
          PixelData = image.PixelData[..],
        };
      case PixelFormat.Indexed8:
        return new() {
          Width = image.Width,
          Height = image.Height,
          Depth = 8,
          ColorClass = MiffColorClass.PseudoClass,
          Type = "TrueColor",
          PixelData = image.PixelData[..],
          Palette = image.Palette != null ? image.Palette[..] : null,
        };
      default:
        throw new ArgumentException($"Unsupported pixel format for MIFF: {image.Format}", nameof(image));
    }
  }
}
