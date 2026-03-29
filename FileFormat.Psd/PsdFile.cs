using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Psd;

/// <summary>In-memory representation of a PSD image (flat composite only).</summary>
public sealed class PsdFile : IImageFileFormat<PsdFile> {

  static string IImageFileFormat<PsdFile>.PrimaryExtension => ".psd";
  static string[] IImageFileFormat<PsdFile>.FileExtensions => [".psd"];

  static bool? IImageFileFormat<PsdFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 6 && header[0] == 0x38 && header[1] == 0x42 && header[2] == 0x50 && header[3] == 0x53
      && header[4] == 0x00 && header[5] == 0x01
      ? true : null;

  static PsdFile IImageFileFormat<PsdFile>.FromFile(FileInfo file) => PsdReader.FromFile(file);
  static PsdFile IImageFileFormat<PsdFile>.FromBytes(byte[] data) => PsdReader.FromBytes(data);
  static PsdFile IImageFileFormat<PsdFile>.FromStream(Stream stream) => PsdReader.FromStream(stream);
  static byte[] IImageFileFormat<PsdFile>.ToBytes(PsdFile file) => PsdWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public int Channels { get; init; }
  public int Depth { get; init; }
  public PsdColorMode ColorMode { get; init; }
  public byte[] PixelData { get; init; } = [];
  public byte[]? Palette { get; init; }
  public byte[]? ImageResources { get; init; }
  public byte[]? LayerMaskInfo { get; init; }

  public static RawImage ToRawImage(PsdFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.Depth != 8)
      throw new NotSupportedException($"Only Depth=8 is supported, got {file.Depth}.");

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
      case PsdColorMode.Indexed when file.Palette != null:
        return new() {
          Width = width,
          Height = height,
          Format = PixelFormat.Indexed8,
          PixelData = file.PixelData[..],
          Palette = file.Palette[..],
          PaletteCount = file.Palette.Length / 3,
        };
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
}
