using System;
using FileFormat.Core;

namespace FileFormat.Palm;

/// <summary>In-memory representation of a Palm OS Bitmap image.</summary>
public readonly record struct PalmFile : IImageFormatReader<PalmFile>, IImageToRawImage<PalmFile>, IImageFromRawImage<PalmFile>, IImageFormatWriter<PalmFile> {

  static string IImageFormatMetadata<PalmFile>.PrimaryExtension => ".palm";
  static string[] IImageFormatMetadata<PalmFile>.FileExtensions => [".palm", ".pdb"];
  static PalmFile IImageFormatReader<PalmFile>.FromSpan(ReadOnlySpan<byte> data) => PalmReader.FromSpan(data);
  static byte[] IImageFormatWriter<PalmFile>.ToBytes(PalmFile file) => PalmWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public int BitsPerPixel { get; init; }
  public PalmCompression Compression { get; init; }
  public byte TransparentIndex { get; init; }
  public byte[] PixelData { get; init; }
  public byte[]? Palette { get; init; }

  public static RawImage ToRawImage(PalmFile file) {

    return file.BitsPerPixel switch {
      16 => new() {
        Width = file.Width,
        Height = file.Height,
        Format = PixelFormat.Rgb565,
        PixelData = file.PixelData[..],
      },
      8 => new() {
        Width = file.Width,
        Height = file.Height,
        Format = PixelFormat.Indexed8,
        PixelData = file.PixelData[..],
        Palette = file.Palette is { } p8 ? p8[..] : null,
        PaletteCount = file.Palette is { } pal8 ? pal8.Length / 3 : 0,
      },
      4 => new() {
        Width = file.Width,
        Height = file.Height,
        Format = PixelFormat.Indexed4,
        PixelData = file.PixelData[..],
        Palette = file.Palette is { } p4 ? p4[..] : null,
        PaletteCount = file.Palette is { } pal4 ? pal4.Length / 3 : 0,
      },
      1 => new() {
        Width = file.Width,
        Height = file.Height,
        Format = PixelFormat.Indexed1,
        PixelData = file.PixelData[..],
        Palette = file.Palette is { } p1 ? p1[..] : [255, 255, 255, 0, 0, 0],
        PaletteCount = file.Palette is { } pal1 ? pal1.Length / 3 : 2,
      },
      _ => throw new ArgumentException($"Unsupported BitsPerPixel: {file.BitsPerPixel}", nameof(file))
    };
  }

  public static PalmFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    switch (image.Format) {
      case PixelFormat.Indexed8:
        return new() {
          Width = image.Width,
          Height = image.Height,
          BitsPerPixel = 8,
          Compression = PalmCompression.None,
          PixelData = image.PixelData[..],
          Palette = image.Palette is { } p8 ? p8[..] : null,
        };
      case PixelFormat.Indexed1:
        return new() {
          Width = image.Width,
          Height = image.Height,
          BitsPerPixel = 1,
          Compression = PalmCompression.None,
          PixelData = image.PixelData[..],
          Palette = image.Palette is { } p1 ? p1[..] : null,
        };
      case PixelFormat.Rgb565:
        return new() {
          Width = image.Width,
          Height = image.Height,
          BitsPerPixel = 16,
          Compression = PalmCompression.None,
          PixelData = image.PixelData[..],
        };
      default:
        throw new ArgumentException($"Unsupported pixel format for Palm: {image.Format}", nameof(image));
    }
  }
}
