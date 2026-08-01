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

  /// <summary>Bytes from one row to the next, which the file states and need not be the tightest.</summary>
  public int BytesPerRow { get; init; }
  public byte[]? Palette { get; init; }

  private static readonly byte[] _Default8bppPalette = _MakeGrayRamp(256);
  private static readonly byte[] _Default4bppPalette = _MakeGrayRamp(16);
  private static readonly byte[] _Default2bppPalette = _MakeGrayRamp(4);

  /// <summary>The grey ramp a picture without a colour table is drawn with.</summary>
  /// <remarks>
  /// It runs backwards: index zero is white and the top index black. That is what the machine did —
  /// the display was reflective, so a bit that was "on" meant ink, and the ramp counts up into the
  /// dark. Building it the other way turns every such picture into its own negative, which the
  /// one-bit case already had right and the deeper ones did not.
  /// </remarks>
  private static byte[] _MakeGrayRamp(int entries) {
    var p = new byte[entries * 3];
    for (var i = 0; i < entries; ++i) {
      var v = entries == 1 ? (byte)128 : (byte)((entries - 1 - i) * 255 / (entries - 1));
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
        PixelData = PackedRows.Compact(file.PixelData, file.Width, file.Height, 2, file.BytesPerRow),
      },
      8 => new() {
        Width = file.Width,
        Height = file.Height,
        Format = PixelFormat.Indexed8,
        PixelData = PackedRows.Compact(file.PixelData, file.Width, file.Height, 1, file.BytesPerRow),
        Palette = file.Palette is { Length: >= 768 } p8 ? p8[..768] : _Default8bppPalette[..],
        PaletteCount = 256,
      },
      // Two bits a pixel has no packed format here, so it is spread to a byte each on the way out.
      2 => new() {
        Width = file.Width,
        Height = file.Height,
        Format = PixelFormat.Indexed8,
        PixelData = PackedRows.Unpack(file.PixelData, file.Width, file.Height, 2, file.BytesPerRow),
        Palette = file.Palette is { Length: >= 12 } p2 ? p2[..12] : _Default2bppPalette[..],
        PaletteCount = 4,
      },
      4 => new() {
        Width = file.Width,
        Height = file.Height,
        Format = PixelFormat.Indexed8,
        PixelData = PackedRows.Unpack(file.PixelData, file.Width, file.Height, 4, file.BytesPerRow),
        Palette = file.Palette is { Length: >= 48 } p4 ? p4[..48] : _Default4bppPalette[..],
        PaletteCount = 16,
      },
      1 => new() {
        Width = file.Width,
        Height = file.Height,
        Format = PixelFormat.Indexed8,
        PixelData = PackedRows.Unpack(file.PixelData, file.Width, file.Height, 1, file.BytesPerRow),
        Palette = file.Palette is { Length: >= 6 } p1 ? p1[..6] : [255, 255, 255, 0, 0, 0],
        PaletteCount = 2,
      },
      _ => throw new ArgumentException($"Unsupported BitsPerPixel: {file.BitsPerPixel}", nameof(file))
    };
  }

  /// <summary>The row stride Palm wants: the tightest that fits, rounded up to a whole word.</summary>
  public static int RowStride(int width, int bitsPerPixel) => (width * bitsPerPixel + 15) / 16 * 2;

  public static PalmFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureAnyFormat(PixelFormat.Rgb565, PixelFormat.Indexed8, PixelFormat.Indexed1);

    var stride = RowStride(image.Width, image.Format switch {
      PixelFormat.Rgb565 => 16,
      PixelFormat.Indexed1 => 1,
      _ => 8,
    });

    switch (image.Format) {
      case PixelFormat.Indexed8:
        return new() {
          Width = image.Width,
          Height = image.Height,
          BitsPerPixel = 8,
          Compression = PalmCompression.None,
          BytesPerRow = stride,
          PixelData = _Spread(image.PixelData, image.Width, image.Height, 1, stride),
          Palette = image.Palette is { } p8 ? p8[..] : null,
        };
      case PixelFormat.Indexed1:
        return new() {
          Width = image.Width,
          Height = image.Height,
          BitsPerPixel = 1,
          Compression = PalmCompression.None,
          BytesPerRow = stride,
          PixelData = PackedRows.Pack(
            BilevelRows.Threshold(image, setWhenDark: true), image.Width, image.Height, 1, stride),
          Palette = image.Palette is { } p1 ? p1[..] : null,
        };
      case PixelFormat.Rgb565:
        return new() {
          Width = image.Width,
          Height = image.Height,
          BitsPerPixel = 16,
          Compression = PalmCompression.None,
          BytesPerRow = stride,
          PixelData = _Spread(image.PixelData, image.Width, image.Height, 2, stride),
        };
      default:
        throw new ArgumentException($"Unsupported pixel format for Palm: {image.Format}", nameof(image));
    }
  }

  /// <summary>Lays tight rows of whole bytes out at a wider stride.</summary>
  private static byte[] _Spread(byte[] tight, int width, int height, int bytesPerPixel, int stride) {
    var rowBytes = width * bytesPerPixel;
    var result = new byte[stride * height];
    for (var y = 0; y < height; ++y) {
      var from = y * rowBytes;
      if (from + rowBytes > tight.Length)
        break;

      Array.Copy(tight, from, result, y * stride, rowBytes);
    }

    return result;
  }
}
