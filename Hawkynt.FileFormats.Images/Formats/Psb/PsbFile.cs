using System;
using FileFormat.Core;

namespace FileFormat.Psb;

/// <summary>In-memory representation of a PSB (Photoshop Big) image (flat composite only).</summary>
[FormatDetectionPriority(50)]
public readonly record struct PsbFile : IImageFormatReader<PsbFile>, IImageToRawImage<PsbFile>, IImageFromRawImage<PsbFile>, IImageFormatWriter<PsbFile> {

  static string IImageFormatMetadata<PsbFile>.PrimaryExtension => ".psb";
  static string[] IImageFormatMetadata<PsbFile>.FileExtensions => [".psb"];
  static PsbFile IImageFormatReader<PsbFile>.FromSpan(ReadOnlySpan<byte> data) => PsbReader.FromSpan(data);

  static bool? IImageFormatMetadata<PsbFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 6 && header[0] == 0x38 && header[1] == 0x42 && header[2] == 0x50 && header[3] == 0x53
      && header[4] == 0x00 && header[5] == 0x02
      ? true : null;

  static byte[] IImageFormatWriter<PsbFile>.ToBytes(PsbFile file) => PsbWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public int Channels { get; init; }
  public int Depth { get; init; }
  public PsbColorMode ColorMode { get; init; }
  public byte[] PixelData { get; init; }
  public byte[]? Palette { get; init; }
  public byte[]? ImageResources { get; init; }
  public byte[]? LayerMaskInfo { get; init; }

  private static readonly byte[] _DefaultPalette = _MakeGrayRamp(256);

  private static byte[] _MakeGrayRamp(int entries) {
    var p = new byte[entries * 3];
    for (var i = 0; i < entries; ++i) {
      var v = entries == 1 ? (byte)128 : (byte)(i * 255 / (entries - 1));
      p[i * 3] = v; p[i * 3 + 1] = v; p[i * 3 + 2] = v;
    }
    return p;
  }

  public static RawImage ToRawImage(PsbFile file) {
    if (file.Depth != 8)
      throw new NotSupportedException($"Only Depth=8 is supported, got {file.Depth}.");

    var width = file.Width;
    var height = file.Height;
    var planeSize = width * height;

    switch (file.ColorMode) {
      case PsbColorMode.Grayscale when file.Channels >= 1:
        return new() {
          Width = width,
          Height = height,
          Format = PixelFormat.Gray8,
          PixelData = file.PixelData[..planeSize],
        };
      case PsbColorMode.RGB when file.Channels == 3:
        return new() {
          Width = width,
          Height = height,
          Format = PixelFormat.Rgb24,
          PixelData = _Deplanarize(file.PixelData, planeSize, 3),
        };
      case PsbColorMode.RGB when file.Channels >= 4:
        return new() {
          Width = width,
          Height = height,
          Format = PixelFormat.Rgba32,
          PixelData = _Deplanarize(file.PixelData, planeSize, 4),
        };
      case PsbColorMode.Indexed: {
        var palette = file.Palette is { Length: >= 768 } p ? _PaletteFromPlanar(p) : _DefaultPalette[..];
        return new() {
          Width = width,
          Height = height,
          Format = PixelFormat.Indexed8,
          PixelData = file.PixelData[..],
          Palette = palette,
          PaletteCount = 256,
        };
      }
      default:
        throw new NotSupportedException($"PSB color mode {file.ColorMode} with {file.Channels} channels is not supported.");
    }
  }

  public static PsbFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var width = image.Width;
    var height = image.Height;
    var planeSize = width * height;

    switch (image.Format) {
      case PixelFormat.Gray8:
        return new() {
          Width = width,
          Height = height,
          Channels = 1,
          Depth = 8,
          ColorMode = PsbColorMode.Grayscale,
          PixelData = image.PixelData[..],
        };
      case PixelFormat.Rgb24:
        return new() {
          Width = width,
          Height = height,
          Channels = 3,
          Depth = 8,
          ColorMode = PsbColorMode.RGB,
          PixelData = _Planarize(image.PixelData, planeSize, 3),
        };
      case PixelFormat.Rgba32:
        return new() {
          Width = width,
          Height = height,
          Channels = 4,
          Depth = 8,
          ColorMode = PsbColorMode.RGB,
          PixelData = _Planarize(image.PixelData, planeSize, 4),
        };
      case PixelFormat.Indexed8:
        return new() {
          Width = width,
          Height = height,
          Channels = 1,
          Depth = 8,
          ColorMode = PsbColorMode.Indexed,
          PixelData = image.PixelData[..],
          Palette = _PaletteToPlanar(image.Palette, image.PaletteCount),
        };
      default:
        throw new ArgumentException($"Pixel format {image.Format} is not supported by PSB.", nameof(image));
    }
  }

  /// <summary>
  /// Turns a Photoshop colour table into the RGB triplets a <see cref="RawImage"/> palette is made of.
  /// </summary>
  /// <remarks>
  /// Photoshop stores the table a channel at a time — 256 reds, then 256 greens, then 256 blues —
  /// the same way it stores the pixels. Handed straight on as triplets it made entry 0 the first
  /// three reds and entry 1 the next three, so an indexed file came back in shades of red: the
  /// four-colour test image arrived as four reds.
  /// </remarks>
  private static byte[] _PaletteFromPlanar(byte[] planar) {
    var palette = new byte[768];
    for (var i = 0; i < 256; ++i) {
      palette[i * 3] = planar[i];
      palette[(i * 3) + 1] = planar[256 + i];
      palette[(i * 3) + 2] = planar[512 + i];
    }

    return palette;
  }

  /// <summary>Lays an RGB-triplet palette out the way Photoshop's colour table is stored.</summary>
  /// <remarks>
  /// Always the full 768 bytes, however few entries are in use: the colour mode section of an indexed
  /// PSD is that size or the file is malformed. Passing a shorter one through unchanged left the
  /// header claiming an indexed image with no table at all, which Photoshop and ImageMagick alike
  /// refuse with "improper image header".
  /// </remarks>
  private static byte[] _PaletteToPlanar(byte[]? palette, int count) {
    var planar = new byte[768];
    if (palette is null)
      return planar;

    var entries = Math.Min(count > 0 ? count : palette.Length / 3, Math.Min(256, palette.Length / 3));
    for (var i = 0; i < entries; ++i) {
      planar[i] = palette[i * 3];
      planar[256 + i] = palette[(i * 3) + 1];
      planar[512 + i] = palette[(i * 3) + 2];
    }

    return planar;
  }

  private static byte[] _Deplanarize(byte[] planar, int planeSize, int channels) {
    var result = new byte[planeSize * channels];
    for (var i = 0; i < planeSize; ++i)
      for (var c = 0; c < channels; ++c)
        result[i * channels + c] = planar[c * planeSize + i];
    return result;
  }

  private static byte[] _Planarize(byte[] interleaved, int planeSize, int channels) {
    var result = new byte[planeSize * channels];
    for (var i = 0; i < planeSize; ++i)
      for (var c = 0; c < channels; ++c)
        result[c * planeSize + i] = interleaved[i * channels + c];
    return result;
  }
}
