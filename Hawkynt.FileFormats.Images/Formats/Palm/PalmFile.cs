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
  private static readonly byte[] _Default2bppPalette = _MakeGrayRamp(4);
  private static readonly byte[] _Default1bppPalette = _MakeGrayRamp(2);

  /// <summary>Builds the grey ramp a Palm device shows when the file names no colours of its own.</summary>
  /// <remarks>
  /// It runs the opposite way from the obvious one: index 0 is white and the last index is black,
  /// because these are shades of ink on a grey screen rather than amounts of light. Built the other
  /// way round, every unpaletted Palm bitmap comes out as its own negative — 4bpp index 1, which a
  /// Palm shows at 238, was arriving as 17.
  /// </remarks>
  private static byte[] _MakeGrayRamp(int entries) {
    var p = new byte[entries * 3];
    for (var i = 0; i < entries; ++i) {
      var v = entries == 1 ? (byte)128 : (byte)(255 - (i * 255 / (entries - 1)));
      p[i * 3] = v; p[i * 3 + 1] = v; p[i * 3 + 2] = v;
    }
    return p;
  }

  /// <summary>Spreads two-bit indices out to one byte each, since there is no two-bit indexed format.</summary>
  private static byte[] _Expand2Bpp(byte[] packed, int width, int height) {
    var stride = (width + 3) / 4;
    var result = new byte[width * height];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var at = (y * stride) + (x >> 2);
        result[(y * width) + x] = at < packed.Length
          ? (byte)((packed[at] >> (6 - ((x & 3) * 2))) & 3)
          : (byte)0;
      }

    return result;
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
      // Two bits a pixel is what a Palm writer picks for anything up to four colours, and it is what
      // ImageMagick produces for an ordinary picture — but there is no two-bit indexed format to hand
      // it back in, so the indices are spread out to a byte each. Left out of this switch entirely,
      // the commonest Palm depth of all was refused.
      2 => new() {
        Width = file.Width,
        Height = file.Height,
        Format = PixelFormat.Indexed8,
        PixelData = _Expand2Bpp(file.PixelData, file.Width, file.Height),
        Palette = file.Palette is { Length: >= 12 } p2 ? p2[..12] : _Default2bppPalette[..],
        PaletteCount = 4,
      },
      1 => new() {
        Width = file.Width,
        Height = file.Height,
        Format = PixelFormat.Indexed1,
        PixelData = file.PixelData[..],
        Palette = file.Palette is { Length: >= 6 } p1 ? p1[..6] : _Default1bppPalette[..],
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
