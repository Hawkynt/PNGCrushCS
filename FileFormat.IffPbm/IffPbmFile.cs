using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.IffPbm;

/// <summary>In-memory representation of an IFF PBM (Packed Bitmap) image.</summary>
[FormatMagicBytes([0x46, 0x4F, 0x52, 0x4D])]
public sealed class IffPbmFile : IImageFileFormat<IffPbmFile> {

  static string IImageFileFormat<IffPbmFile>.PrimaryExtension => ".lbm";
  static string[] IImageFileFormat<IffPbmFile>.FileExtensions => [".lbm", ".pbm"];
  static FormatCapability IImageFileFormat<IffPbmFile>.Capabilities => FormatCapability.IndexedOnly;

  static bool? IImageFileFormat<IffPbmFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 12 && header[0] == 0x46 && header[1] == 0x4F && header[2] == 0x52 && header[3] == 0x4D
      && header[8] == 0x50 && header[9] == 0x42 && header[10] == 0x4D && header[11] == 0x20;

  static IffPbmFile IImageFileFormat<IffPbmFile>.FromFile(FileInfo file) => IffPbmReader.FromFile(file);
  static IffPbmFile IImageFileFormat<IffPbmFile>.FromBytes(byte[] data) => IffPbmReader.FromBytes(data);
  static IffPbmFile IImageFileFormat<IffPbmFile>.FromStream(Stream stream) => IffPbmReader.FromStream(stream);
  static byte[] IImageFileFormat<IffPbmFile>.ToBytes(IffPbmFile file) => IffPbmWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public IffPbmCompression Compression { get; init; }
  public int TransparentColor { get; init; }
  public byte XAspect { get; init; }
  public byte YAspect { get; init; }
  public int PageWidth { get; init; }
  public int PageHeight { get; init; }
  public byte[] PixelData { get; init; } = [];
  public byte[]? Palette { get; init; }

  /// <summary>Converts this PBM file to a format-independent <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(IffPbmFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var palette = file.Palette is { } p ? p[..] : null;
    var paletteCount = palette != null ? palette.Length / 3 : 256;

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
      Palette = palette,
      PaletteCount = paletteCount,
    };
  }

  /// <summary>Creates an <see cref="IffPbmFile"/> from a format-independent <see cref="RawImage"/>.</summary>
  public static IffPbmFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException($"Unsupported pixel format for IFF PBM: {image.Format}. Only Indexed8 is supported.", nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      Compression = IffPbmCompression.ByteRun1,
      TransparentColor = 0,
      XAspect = 1,
      YAspect = 1,
      PageWidth = image.Width,
      PageHeight = image.Height,
      PixelData = image.PixelData[..],
      Palette = image.Palette is { } p ? p[..] : null,
    };
  }
}
