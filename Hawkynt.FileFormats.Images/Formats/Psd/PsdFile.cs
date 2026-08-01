using System;
using FileFormat.Core;

namespace FileFormat.Psd;

/// <summary>In-memory representation of a PSD image (flat composite only).</summary>
[FormatMimeType("image/vnd.adobe.photoshop", "application/x-photoshop", "image/x-psd")]
public readonly record struct PsdFile : IImageFormatReader<PsdFile>, IImageToRawImage<PsdFile>, IImageFromRawImage<PsdFile>, IImageFormatWriter<PsdFile> {

  static string IImageFormatMetadata<PsdFile>.PrimaryExtension => ".psd";
  static string[] IImageFormatMetadata<PsdFile>.FileExtensions => [".psd"];
  static PsdFile IImageFormatReader<PsdFile>.FromSpan(ReadOnlySpan<byte> data) => PsdReader.FromSpan(data);

  static bool? IImageFormatMetadata<PsdFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 6 && header[0] == 0x38 && header[1] == 0x42 && header[2] == 0x50 && header[3] == 0x53
      && header[4] == 0x00 && header[5] == 0x01
      ? true : null;

  static byte[] IImageFormatWriter<PsdFile>.ToBytes(PsdFile file) => PsdWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public int Channels { get; init; }
  public int Depth { get; init; }
  public PsdColorMode ColorMode { get; init; }
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

  public static RawImage ToRawImage(PsdFile file) {
    // A sixteen-bit document is ordinary rather than exotic — a scanner or a colour-managed edit
    // produces one — so it is narrowed to the byte carrying the magnitude rather than refused. The
    // format stores samples most significant byte first, whatever the machine.
    if (file.Depth == 16)
      file = file with { Depth = 8, PixelData = _NarrowSamples(file.PixelData) };

    if (file.Depth != 8)
      throw new NotSupportedException($"Only 8- and 16-bit depths are supported, got {file.Depth}.");

    var width = file.Width;
    var height = file.Height;
    var planeSize = width * height;

    switch (file.ColorMode) {
      case PsdColorMode.Grayscale when file.Channels >= 1:
        return new() {
          Width = width,
          Height = height,
          Format = PixelFormat.Gray8,
          PixelData = file.PixelData[..planeSize],
        };
      case PsdColorMode.RGB when file.Channels == 3:
        return new() {
          Width = width,
          Height = height,
          Format = PixelFormat.Rgb24,
          PixelData = _Deplanarize(file.PixelData, planeSize, 3),
        };
      case PsdColorMode.RGB when file.Channels >= 4:
        return new() {
          Width = width,
          Height = height,
          Format = PixelFormat.Rgba32,
          PixelData = _Deplanarize(file.PixelData, planeSize, 4),
        };
      case PsdColorMode.Indexed: {
        var palette = file.Palette is { Length: >= 768 } p ? p[..768] : _DefaultPalette[..];
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
        throw new NotSupportedException($"PSD color mode {file.ColorMode} with {file.Channels} channels is not supported.");
    }
  }

  public static PsdFile FromRawImage(RawImage image) {
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
          ColorMode = PsdColorMode.Grayscale,
          PixelData = image.PixelData[..],
        };
      case PixelFormat.Rgb24:
        return new() {
          Width = width,
          Height = height,
          Channels = 3,
          Depth = 8,
          ColorMode = PsdColorMode.RGB,
          PixelData = _Planarize(image.PixelData, planeSize, 3),
        };
      case PixelFormat.Rgba32:
        return new() {
          Width = width,
          Height = height,
          Channels = 4,
          Depth = 8,
          ColorMode = PsdColorMode.RGB,
          PixelData = _Planarize(image.PixelData, planeSize, 4),
        };
      case PixelFormat.Indexed8:
        return new() {
          Width = width,
          Height = height,
          Channels = 1,
          Depth = 8,
          ColorMode = PsdColorMode.Indexed,
          PixelData = image.PixelData[..],
          Palette = image.Palette != null ? image.Palette[..] : null,
        };
      default:
        throw new ArgumentException($"Pixel format {image.Format} is not supported by PSD.", nameof(image));
    }
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

  /// <summary>Narrows sixteen-bit samples to eight, keeping the high byte.</summary>
  /// <remarks>
  /// The low byte is dropped rather than rounded into the high one: the difference is under half a
  /// level at eight bits, and dropping cannot carry a sample past its neighbour the way rounding
  /// can.
  /// </remarks>
  private static byte[] _NarrowSamples(ReadOnlySpan<byte> data) {
    var narrowed = new byte[data.Length / 2];
    for (var i = 0; i < narrowed.Length; ++i)
      narrowed[i] = data[i * 2];

    return narrowed;
  }
}
