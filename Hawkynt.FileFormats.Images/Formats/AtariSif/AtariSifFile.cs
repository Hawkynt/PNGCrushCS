using System;
using FileFormat.Core;

namespace FileFormat.AtariSif;

/// <summary>
/// Atari 8-bit SIF (Standard Image Format) bitmap. 8-byte header "SIF\0" magic + width (big-endian 16),
/// height (big-endian 16), ANTIC graphics mode (1 byte), reserved (1 byte). Pixel data follows as a
/// raw packed bitmap whose layout is determined by the ANTIC mode.
/// </summary>
[FormatMagicBytes([0x53, 0x49, 0x46, 0x00])]
public readonly record struct AtariSifFile : IImageFormatReader<AtariSifFile>, IImageFormatWriter<AtariSifFile>, IImageToRawImage<AtariSifFile>, IImageFromRawImage<AtariSifFile> {

  static string IImageFormatMetadata<AtariSifFile>.PrimaryExtension => ".sif";
  static string[] IImageFormatMetadata<AtariSifFile>.FileExtensions => [".sif"];
  static AtariSifFile IImageFormatReader<AtariSifFile>.FromSpan(ReadOnlySpan<byte> data) => AtariSifReader.FromSpan(data);
  static byte[] IImageFormatWriter<AtariSifFile>.ToBytes(AtariSifFile file) => AtariSifWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<AtariSifFile>.VideoModes => [
    new("ANTIC 8 (GR.7,  160x96, 4-colour)",  [(160, 96)],  [4]),
    new("ANTIC 9 (GR.8,  320x192, 2-colour)", [(320, 192)], [2]),
    new("ANTIC 15 (GR.15, 160x192, 4-colour)", [(160, 192)], [4]),
  ];

  public int Width { get; init; }
  public int Height { get; init; }
  public byte AnticMode { get; init; }
  public byte[] PixelData { get; init; }

  public int BitsPerPixel => AnticMode == 9 ? 1 : 2;

  // Atari ANTIC standard palette stub (3 colours/2bpp + black for monochrome modes).
  private static readonly byte[] _MonoPalette = [0, 0, 0, 255, 255, 255];
  private static readonly byte[] _FourColorPalette = [
    0,   0,   0,
    192, 32,  32,
    32, 192,  32,
    255, 255, 96,
  ];

  public static RawImage ToRawImage(AtariSifFile file) {
    ArgumentNullException.ThrowIfNull(file.PixelData);
    var bpp = file.BitsPerPixel;
    var rowBytes = (file.Width * bpp + 7) >> 3;
    var indices = new byte[file.Width * file.Height];
    var mask = (1 << bpp) - 1;
    for (var y = 0; y < file.Height; ++y) {
      var rowOff = y * rowBytes;
      for (var x = 0; x < file.Width; ++x) {
        var bitIx = x * bpp;
        var byteIx = rowOff + (bitIx >> 3);
        var shift = 8 - bpp - (bitIx & 7);
        indices[y * file.Width + x] = (byte)((file.PixelData[byteIx] >> shift) & mask);
      }
    }
    var palette = bpp == 1 ? _MonoPalette : _FourColorPalette;
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = bpp == 1 ? PixelFormat.Indexed1 : PixelFormat.Indexed8,
      PixelData = bpp == 1 ? file.PixelData : indices,
      Palette = (byte[])palette.Clone(),
      PaletteCount = 1 << bpp,
    };
  }

  public static AtariSifFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    // The size picks the mode, and the mode decides how many colours it has room for. Which pixel
    // format that needs is this writer's business rather than the caller's, so the picture is
    // converted into it instead of being turned away for arriving as anything else.
    var (mode, bpp) = (image.Width, image.Height) switch {
      (160, 96) => ((byte)8, 2),
      (320, 192) => ((byte)9, 1),
      (160, 192) => ((byte)15, 2),
      _ => throw new ArgumentException($"Unsupported Atari SIF geometry {image.Width}x{image.Height}.", nameof(image)),
    };

    image = bpp == 1 ? image.EnsureFormat(PixelFormat.Indexed1) : image.EnsureIndexedAtMost(1 << bpp);
    if (bpp == 1)
      return new() { Width = image.Width, Height = image.Height, AnticMode = mode, PixelData = (byte[])image.PixelData.Clone() };

    var rowBytes = (image.Width * bpp + 7) >> 3;
    var packed = new byte[rowBytes * image.Height];
    for (var y = 0; y < image.Height; ++y) {
      var rowOff = y * rowBytes;
      for (var x = 0; x < image.Width; ++x) {
        var bitIx = x * bpp;
        var byteIx = rowOff + (bitIx >> 3);
        var shift = 8 - bpp - (bitIx & 7);
        packed[byteIx] |= (byte)((image.PixelData[y * image.Width + x] & 0x03) << shift);
      }
    }
    return new() { Width = image.Width, Height = image.Height, AnticMode = mode, PixelData = packed };
  }
}
