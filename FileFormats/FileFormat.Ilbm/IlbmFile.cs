using System;
using FileFormat.Core;

namespace FileFormat.Ilbm;

/// <summary>In-memory representation of an IFF ILBM image.</summary>
[FormatMagicBytes([0x46, 0x4F, 0x52, 0x4D])]
public readonly record struct IlbmFile : IImageFormatReader<IlbmFile>, IImageToRawImage<IlbmFile>, IImageFromRawImage<IlbmFile>, IImageFormatWriter<IlbmFile> {

  static string IImageFormatMetadata<IlbmFile>.PrimaryExtension => ".lbm";
  static string[] IImageFormatMetadata<IlbmFile>.FileExtensions => [".lbm", ".ilbm", ".iff"];
  static IlbmFile IImageFormatReader<IlbmFile>.FromSpan(ReadOnlySpan<byte> data) => IlbmReader.FromSpan(data);

  static bool? IImageFormatMetadata<IlbmFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 12 && header[0] == 0x46 && header[1] == 0x4F && header[2] == 0x52 && header[3] == 0x4D
      && header[8] == 0x49 && header[9] == 0x4C && header[10] == 0x42 && header[11] == 0x4D;

  static byte[] IImageFormatWriter<IlbmFile>.ToBytes(IlbmFile file) => IlbmWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public int NumPlanes { get; init; }
  public IlbmCompression Compression { get; init; }
  public IlbmMasking Masking { get; init; }
  public int TransparentColor { get; init; }
  public byte XAspect { get; init; }
  public byte YAspect { get; init; }
  public int PageWidth { get; init; }
  public int PageHeight { get; init; }
  public byte[] PixelData { get; init; }
  public byte[]? Palette { get; init; }

  /// <summary>CAMG viewport mode bits (from the Amiga display hardware).</summary>
  public uint ViewportMode { get; init; }

  /// <summary>Whether the image uses Hold-And-Modify mode (HAM6 or HAM8).</summary>
  public bool IsHam => (ViewportMode & 0x800) != 0;

  /// <summary>Whether the image uses Extra Half-Brite mode.</summary>
  public bool IsEhb => (ViewportMode & 0x80) != 0;

  /// <summary>Converts this ILBM file to a format-independent <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(IlbmFile file) {

    // HAM mode: decode indexed data to RGB via HamDecoder
    if (file.IsHam && file.Palette is { } hamPalette) {
      var rgb = HamDecoder.Decode(file.PixelData, hamPalette, file.Width, file.Height, file.NumPlanes);
      return new() {
        Width = file.Width,
        Height = file.Height,
        Format = PixelFormat.Rgb24,
        PixelData = rgb,
      };
    }

    // EHB mode: expand 32-entry palette to 64 entries (entries 32..63 = half brightness)
    if (file.IsEhb && file.Palette is { } ehbPalette) {
      var basePaletteCount = Math.Min(ehbPalette.Length / 3, 32);
      var expandedPalette = new byte[64 * 3];
      ehbPalette.AsSpan(0, basePaletteCount * 3).CopyTo(expandedPalette.AsSpan(0));
      for (var i = 0; i < basePaletteCount; ++i) {
        expandedPalette[(i + 32) * 3] = (byte)(ehbPalette[i * 3] / 2);
        expandedPalette[(i + 32) * 3 + 1] = (byte)(ehbPalette[i * 3 + 1] / 2);
        expandedPalette[(i + 32) * 3 + 2] = (byte)(ehbPalette[i * 3 + 2] / 2);
      }

      return new() {
        Width = file.Width,
        Height = file.Height,
        Format = PixelFormat.Indexed8,
        PixelData = file.PixelData[..],
        Palette = expandedPalette,
        PaletteCount = 64,
      };
    }

    // Normal indexed mode
    var palette = file.Palette is { } p ? p[..] : null;
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

  /// <summary>Creates an <see cref="IlbmFile"/> from a format-independent <see cref="RawImage"/>.</summary>
  public static IlbmFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    byte[] pixelData;
    byte[]? palette;
    int numPlanes;

    switch (image.Format) {
      case PixelFormat.Indexed8:
        pixelData = image.PixelData[..];
        palette = image.Palette is { } p ? p[..] : null;
        numPlanes = Math.Max(1, (int)Math.Ceiling(Math.Log2(Math.Max(image.PaletteCount, 2))));
        break;
      case PixelFormat.Rgb24:
        // Quantize to indexed: collect unique colors up to 256
        var colorMap = new System.Collections.Generic.Dictionary<int, byte>();
        var indices = new byte[image.PixelData.Length / 3];
        for (var i = 0; i < indices.Length; ++i) {
          var r = image.PixelData[i * 3];
          var g = image.PixelData[i * 3 + 1];
          var b = image.PixelData[i * 3 + 2];
          var key = (r << 16) | (g << 8) | b;
          if (!colorMap.TryGetValue(key, out var idx)) {
            if (colorMap.Count >= 256)
              throw new ArgumentException("RGB24 image has more than 256 unique colors; quantization is not supported.", nameof(image));
            idx = (byte)colorMap.Count;
            colorMap[key] = idx;
          }
          indices[i] = idx;
        }

        var palData = new byte[colorMap.Count * 3];
        foreach (var (key, idx) in colorMap) {
          palData[idx * 3] = (byte)(key >> 16);
          palData[idx * 3 + 1] = (byte)((key >> 8) & 0xFF);
          palData[idx * 3 + 2] = (byte)(key & 0xFF);
        }

        pixelData = indices;
        palette = palData;
        numPlanes = Math.Max(1, (int)Math.Ceiling(Math.Log2(Math.Max(colorMap.Count, 2))));
        break;
      default:
        throw new ArgumentException($"Unsupported pixel format for ILBM: {image.Format}", nameof(image));
    }

    return new() {
      Width = image.Width,
      Height = image.Height,
      NumPlanes = numPlanes,
      Compression = IlbmCompression.None,
      Masking = IlbmMasking.None,
      TransparentColor = 0,
      XAspect = 1,
      YAspect = 1,
      PageWidth = image.Width,
      PageHeight = image.Height,
      PixelData = pixelData,
      Palette = palette,
      ViewportMode = 0,
    };
  }
}
