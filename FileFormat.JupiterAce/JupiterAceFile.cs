using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.JupiterAce;

/// <summary>In-memory representation of a Jupiter Ace character screen image.</summary>
public sealed class JupiterAceFile : IImageFileFormat<JupiterAceFile> {

  internal const int FixedWidth = 256;
  internal const int FixedHeight = 192;
  internal const int FileSize = 1536;

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  static string IImageFileFormat<JupiterAceFile>.PrimaryExtension => ".jac";
  static string[] IImageFileFormat<JupiterAceFile>.FileExtensions => [".jac", ".ace"];
  static FormatCapability IImageFileFormat<JupiterAceFile>.Capabilities => FormatCapability.MonochromeOnly;
  static JupiterAceFile IImageFileFormat<JupiterAceFile>.FromFile(FileInfo file) => JupiterAceReader.FromFile(file);
  static JupiterAceFile IImageFileFormat<JupiterAceFile>.FromBytes(byte[] data) => JupiterAceReader.FromBytes(data);
  static JupiterAceFile IImageFileFormat<JupiterAceFile>.FromStream(Stream stream) => JupiterAceReader.FromStream(stream);
  static byte[] IImageFileFormat<JupiterAceFile>.ToBytes(JupiterAceFile file) => JupiterAceWriter.ToBytes(file);

  public int Width => FixedWidth;
  public int Height => FixedHeight;
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(JupiterAceFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return new() {
      Width = FixedWidth,
      Height = FixedHeight,
      Format = PixelFormat.Indexed1,
      PixelData = file.PixelData[..],
      Palette = _BlackWhitePalette[..],
      PaletteCount = 2,
    };
  }

  public static JupiterAceFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed1)
      throw new ArgumentException($"Expected Indexed1 but got {image.Format}.", nameof(image));
    if (image.Width != FixedWidth || image.Height != FixedHeight)
      throw new ArgumentException($"Expected {FixedWidth}x{FixedHeight} but got {image.Width}x{image.Height}.", nameof(image));

    return new() { PixelData = image.PixelData[..] };
  }
}
