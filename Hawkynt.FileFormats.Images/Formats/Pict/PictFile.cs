using System;
using FileFormat.Core;

namespace FileFormat.Pict;

/// <summary>In-memory representation of a PICT image (raster subset).</summary>
public readonly record struct PictFile : IImageFormatReader<PictFile>, IImageToRawImage<PictFile>, IImageFromRawImage<PictFile>, IImageFormatWriter<PictFile> {

  /// <summary>Bytes of Macintosh file header before the picture itself.</summary>
  private const int _MAC_HEADER_SIZE = 512;

  /// <summary>Where the version 2 opcode sits: past the header, the size word and the bounds.</summary>
  private const int _VERSION_OPCODE_OFFSET = _MAC_HEADER_SIZE + 10;

  /// <summary>
  /// Recognises a QuickDraw picture by the opcode that opens it rather than by its name.
  /// </summary>
  /// <remarks>
  /// These carry no signature at offset zero — a Macintosh file header of 512 bytes comes first, and
  /// it is whatever the creating program left there. So detection fell back to the extension, and
  /// two of these in the corpus are named .16 and .jpg, which sent them to readers for a Sinclair
  /// screen and a JPEG. Both refused, and the picture went unread by anything.
  /// <para/>
  /// Version 2 opens 0x0011 0x02FF after the size word and the bounds rectangle, which is specific
  /// enough to name the format and is checked here.
  /// </remarks>
  static bool? IImageFormatMetadata<PictFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < _VERSION_OPCODE_OFFSET + 4)
      return null;

    var at = header[_VERSION_OPCODE_OFFSET..];
    return at[0] == 0x00 && at[1] == 0x11 && at[2] == 0x02 && at[3] == 0xFF ? true : null;
  }

  static string IImageFormatMetadata<PictFile>.PrimaryExtension => ".pict";
  /// <summary>Also <c>.pict2</c>, which is the same picture with its version in the name.</summary>
  static string[] IImageFormatMetadata<PictFile>.FileExtensions => [".pict", ".pct", ".pict2"];
  static PictFile IImageFormatReader<PictFile>.FromSpan(ReadOnlySpan<byte> data) => PictReader.FromSpan(data);
  static byte[] IImageFormatWriter<PictFile>.ToBytes(PictFile file) => PictWriter.ToBytes(file);
  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }
  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }
  /// <summary>Bits per pixel (8 for indexed, 24 for direct RGB).</summary>
  public int BitsPerPixel { get; init; }
  /// <summary>Pixel data: RGB interleaved for 24bpp, indexed for 8bpp.</summary>
  public byte[] PixelData { get; init; }
  /// <summary>Optional palette for indexed images (R,G,B triplets).</summary>
  public byte[]? Palette { get; init; }

  private static readonly byte[] _DefaultPalette = _MakeGrayRamp(256);

  private static byte[] _MakeGrayRamp(int entries) {
    var p = new byte[entries * 3];
    for (var i = 0; i < entries; ++i) {
      var v = entries == 1 ? (byte)128 : (byte)(i * 255 / (entries - 1));
      p[i * 3] = v; p[i * 3 + 1] = v; p[i * 3 + 2] = v;
    }
    return p;
  }

  /// <summary>Converts this PICT file to a format-independent <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(PictFile file) {

    if (file.BitsPerPixel == 24)
      return new() {
        Width = file.Width,
        Height = file.Height,
        Format = PixelFormat.Rgb24,
        PixelData = file.PixelData[..],
      };

    // Indexed (8bpp)
    var palette = file.Palette is { Length: >= 768 } p ? p[..768] : _DefaultPalette[..];
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
      Palette = palette,
      PaletteCount = 256,
    };
  }

  /// <summary>Creates a <see cref="PictFile"/> from a format-independent <see cref="RawImage"/>.</summary>
  public static PictFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureAnyFormat(PixelFormat.Rgb24, PixelFormat.Indexed8);

    switch (image.Format) {
      case PixelFormat.Rgb24:
        return new() {
          Width = image.Width,
          Height = image.Height,
          BitsPerPixel = 24,
          PixelData = image.PixelData[..],
        };
      case PixelFormat.Indexed8:
        return new() {
          Width = image.Width,
          Height = image.Height,
          BitsPerPixel = 8,
          PixelData = image.PixelData[..],
          Palette = image.Palette is { } p ? p[..] : null,
        };
      default:
        throw new ArgumentException($"Unsupported pixel format for PICT: {image.Format}", nameof(image));
    }
  }
}
