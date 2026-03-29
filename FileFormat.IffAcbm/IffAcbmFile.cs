using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.IffAcbm;

/// <summary>In-memory representation of an IFF ACBM (Amiga Contiguous Bitmap) image.</summary>
[FormatMagicBytes([0x46, 0x4F, 0x52, 0x4D])]
public sealed class IffAcbmFile : IImageFileFormat<IffAcbmFile> {

  static string IImageFileFormat<IffAcbmFile>.PrimaryExtension => ".acbm";
  static string[] IImageFileFormat<IffAcbmFile>.FileExtensions => [".acbm", ".iff"];
  static FormatCapability IImageFileFormat<IffAcbmFile>.Capabilities => FormatCapability.IndexedOnly;

  static bool? IImageFileFormat<IffAcbmFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 12 && header[0] == 0x46 && header[1] == 0x4F && header[2] == 0x52 && header[3] == 0x4D
      && header[8] == 0x41 && header[9] == 0x43 && header[10] == 0x42 && header[11] == 0x4D;

  static IffAcbmFile IImageFileFormat<IffAcbmFile>.FromFile(FileInfo file) => IffAcbmReader.FromFile(file);
  static IffAcbmFile IImageFileFormat<IffAcbmFile>.FromBytes(byte[] data) => IffAcbmReader.FromBytes(data);
  static IffAcbmFile IImageFileFormat<IffAcbmFile>.FromStream(Stream stream) => IffAcbmReader.FromStream(stream);
  static byte[] IImageFileFormat<IffAcbmFile>.ToBytes(IffAcbmFile file) => IffAcbmWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Number of bitplanes (1-8).</summary>
  public byte NumPlanes { get; init; }

  /// <summary>Indexed pixel data (one byte per pixel, each byte is a palette index).</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>Palette as RGB triplets (3 bytes per entry).</summary>
  public byte[] Palette { get; init; } = [];

  /// <summary>X aspect ratio from BMHD.</summary>
  public byte XAspect { get; init; }

  /// <summary>Y aspect ratio from BMHD.</summary>
  public byte YAspect { get; init; }

  /// <summary>Page width from BMHD.</summary>
  public int PageWidth { get; init; }

  /// <summary>Page height from BMHD.</summary>
  public int PageHeight { get; init; }

  /// <summary>Transparent color index from BMHD.</summary>
  public int TransparentColor { get; init; }

  /// <summary>Converts this ACBM file to a format-independent <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(IffAcbmFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var palette = file.Palette.Length > 0 ? file.Palette[..] : null;
    var paletteCount = palette != null ? palette.Length / 3 : 1 << file.NumPlanes;

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
      Palette = palette,
      PaletteCount = paletteCount,
    };
  }

  /// <summary>Creates an <see cref="IffAcbmFile"/> from a format-independent <see cref="RawImage"/>.</summary>
  public static IffAcbmFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException($"Unsupported pixel format for ACBM: {image.Format}", nameof(image));

    var pixelData = image.PixelData[..];
    var palette = image.Palette is { } p ? p[..] : [];
    var paletteCount = image.PaletteCount > 0 ? image.PaletteCount : (palette.Length / 3);
    var numPlanes = (byte)Math.Max(1, (int)Math.Ceiling(Math.Log2(Math.Max(paletteCount, 2))));

    return new() {
      Width = image.Width,
      Height = image.Height,
      NumPlanes = numPlanes,
      PixelData = pixelData,
      Palette = palette,
      XAspect = 1,
      YAspect = 1,
      PageWidth = image.Width,
      PageHeight = image.Height,
      TransparentColor = 0,
    };
  }
}
