using System;
using FileFormat.Core;

namespace FileFormat.Cdxl;

/// <summary>
/// Amiga CDXL (CDTV/CD32 video-stream) container, first frame as a still image.
/// CDXL is an interleaved video+audio stream of fixed-size frames; this implementation reads/writes
/// the first frame's palette + planar bitmap. The 32-byte chunk header layout matches the public spec
/// commonly attributed to the Commodore CDTV team (big-endian, planar bitplanes, optional audio block).
/// </summary>
public readonly record struct CdxlFile : IImageFormatReader<CdxlFile>, IImageFormatWriter<CdxlFile>, IImageToRawImage<CdxlFile>, IImageFromRawImage<CdxlFile> {

  static string IImageFormatMetadata<CdxlFile>.PrimaryExtension => ".cdxl";
  static string[] IImageFormatMetadata<CdxlFile>.FileExtensions => [".cdxl"];
  static CdxlFile IImageFormatReader<CdxlFile>.FromSpan(ReadOnlySpan<byte> data) => CdxlReader.FromSpan(data);
  static byte[] IImageFormatWriter<CdxlFile>.ToBytes(CdxlFile file) => CdxlWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<CdxlFile>.VideoModes => [
    new("CDXL frame", [(IntegerRange.Any, IntegerRange.Any)], [new IntegerRange(2, 256)])
  ];

  public int Width { get; init; }
  public int Height { get; init; }
  public int BitPlanes { get; init; }
  public byte[] Palette { get; init; }
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(CdxlFile file) {
    ArgumentNullException.ThrowIfNull(file.PixelData);
    ArgumentNullException.ThrowIfNull(file.Palette);

    var paletteRgb = _DepalToRgb(file.Palette);
    var paletteCount = paletteRgb.Length / 3;
    var rowBytes = (file.Width + 7) >> 3;
    var indices = new byte[file.Width * file.Height];
    var planeSize = rowBytes * file.Height;

    for (var y = 0; y < file.Height; ++y) {
      var rowOff = y * rowBytes;
      for (var x = 0; x < file.Width; ++x) {
        var byteIx = rowOff + (x >> 3);
        var bit = 7 - (x & 7);
        byte v = 0;
        for (var p = 0; p < file.BitPlanes; ++p) {
          var planeOff = p * planeSize;
          if ((file.PixelData[planeOff + byteIx] & (1 << bit)) != 0)
            v |= (byte)(1 << p);
        }
        indices[y * file.Width + x] = v;
      }
    }

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = indices,
      Palette = paletteRgb,
      PaletteCount = paletteCount,
    };
  }

  public static CdxlFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    // Reduced rather than refused: asking the caller to hand over an already-indexed picture
    // makes converting into this format someone else's problem, which is the one thing a
    // converter cannot delegate.
    image = image.EnsureIndexedAtMost(256);

    var paletteCount = image.PaletteCount > 0 ? image.PaletteCount : image.Palette.Length / 3;
    var planes = _BitPlanesForColours(paletteCount);
    var rowBytes = (image.Width + 7) >> 3;
    var planeSize = rowBytes * image.Height;
    var bitmap = new byte[planes * planeSize];

    for (var y = 0; y < image.Height; ++y) {
      var rowOff = y * rowBytes;
      for (var x = 0; x < image.Width; ++x) {
        var v = image.PixelData[y * image.Width + x];
        var byteIx = rowOff + (x >> 3);
        var bit = 7 - (x & 7);
        for (var p = 0; p < planes; ++p)
          if ((v & (1 << p)) != 0)
            bitmap[p * planeSize + byteIx] |= (byte)(1 << bit);
      }
    }

    return new() {
      Width = image.Width,
      Height = image.Height,
      BitPlanes = planes,
      Palette = _RgbToDepal(image.Palette, paletteCount),
      PixelData = bitmap,
    };
  }

  // Amiga 12-bit OCS/ECS palette: each entry is a big-endian 16-bit word with low 12 bits = 0x0RGB.
  internal static byte[] _DepalToRgb(byte[] amigaPalette) {
    var n = amigaPalette.Length / 2;
    var rgb = new byte[n * 3];
    for (var i = 0; i < n; ++i) {
      var hi = amigaPalette[i * 2];
      var lo = amigaPalette[i * 2 + 1];
      var r4 = hi & 0x0F;
      var g4 = (lo >> 4) & 0x0F;
      var b4 = lo & 0x0F;
      rgb[i * 3 + 0] = (byte)((r4 << 4) | r4);
      rgb[i * 3 + 1] = (byte)((g4 << 4) | g4);
      rgb[i * 3 + 2] = (byte)((b4 << 4) | b4);
    }
    return rgb;
  }

  internal static byte[] _RgbToDepal(byte[] rgb, int paletteCount) {
    var pal = new byte[paletteCount * 2];
    for (var i = 0; i < paletteCount; ++i) {
      var r4 = rgb[i * 3 + 0] >> 4;
      var g4 = rgb[i * 3 + 1] >> 4;
      var b4 = rgb[i * 3 + 2] >> 4;
      pal[i * 2] = (byte)(r4 & 0x0F);
      pal[i * 2 + 1] = (byte)((g4 << 4) | (b4 & 0x0F));
    }
    return pal;
  }

  internal static int _BitPlanesForColours(int n) {
    var p = 1;
    while ((1 << p) < n) ++p;
    return p;
  }
}
