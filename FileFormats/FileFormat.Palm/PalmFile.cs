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

  private static readonly byte[] _Default8bppPalette = _MakeGrayRamp(256);
  private static readonly byte[] _Default4bppPalette = _MakeGrayRamp(16);

  private static byte[] _MakeGrayRamp(int entries) {
    var p = new byte[entries * 3];
    for (var i = 0; i < entries; ++i) {
      var v = entries == 1 ? (byte)128 : (byte)(i * 255 / (entries - 1));
      p[i * 3] = v; p[i * 3 + 1] = v; p[i * 3 + 2] = v;
    }
    return p;
  }

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
        Palette = file.Palette is { Length: >= 768 } p8 ? p8[..768] : _Default8bppPalette[..],
        PaletteCount = 256,
      },
      4 => new() {
        Width = file.Width,
        Height = file.Height,
        Format = PixelFormat.Indexed4,
        PixelData = file.PixelData[..],
        Palette = file.Palette is { Length: >= 48 } p4 ? p4[..48] : _Default4bppPalette[..],
        PaletteCount = 16,
      },
      1 => new() {
        Width = file.Width,
        Height = file.Height,
        Format = PixelFormat.Indexed1,
        PixelData = file.PixelData[..],
        Palette = file.Palette is { Length: >= 6 } p1 ? p1[..6] : [255, 255, 255, 0, 0, 0],
        PaletteCount = 2,
      },
      _ => throw new ArgumentException($"Unsupported BitsPerPixel: {file.BitsPerPixel}", nameof(file))
    };
  }

  public static PalmFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureAnyFormat(PixelFormat.Rgb565, PixelFormat.Indexed8, PixelFormat.Indexed1);

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
