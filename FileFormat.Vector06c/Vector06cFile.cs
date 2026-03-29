using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Vector06c;

/// <summary>In-memory representation of a Vector-06C screen image.</summary>
public sealed class Vector06cFile : IImageFileFormat<Vector06cFile> {

  internal const int FixedWidth = 256;
  internal const int FixedHeight = 256;
  internal const int FileSize = 16384;

  private static readonly byte[] _DefaultPalette = [0, 0, 0, 0, 0, 255, 0, 255, 0, 255, 0, 0];

  static string IImageFileFormat<Vector06cFile>.PrimaryExtension => ".v06";
  static string[] IImageFileFormat<Vector06cFile>.FileExtensions => [".v06", ".scr"];
  static FormatCapability IImageFileFormat<Vector06cFile>.Capabilities => FormatCapability.IndexedOnly;
  static Vector06cFile IImageFileFormat<Vector06cFile>.FromFile(FileInfo file) => Vector06cReader.FromFile(file);
  static Vector06cFile IImageFileFormat<Vector06cFile>.FromBytes(byte[] data) => Vector06cReader.FromBytes(data);
  static Vector06cFile IImageFileFormat<Vector06cFile>.FromStream(Stream stream) => Vector06cReader.FromStream(stream);
  static byte[] IImageFileFormat<Vector06cFile>.ToBytes(Vector06cFile file) => Vector06cWriter.ToBytes(file);

  public int Width => FixedWidth;
  public int Height => FixedHeight;
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(Vector06cFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return new() {
      Width = FixedWidth,
      Height = FixedHeight,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
      Palette = _DefaultPalette[..],
      PaletteCount = 4,
    };
  }

  public static Vector06cFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException($"Expected Indexed8 but got {image.Format}.", nameof(image));
    if (image.Width != FixedWidth || image.Height != FixedHeight)
      throw new ArgumentException($"Expected {FixedWidth}x{FixedHeight} but got {image.Width}x{image.Height}.", nameof(image));

    return new() { PixelData = image.PixelData[..] };
  }
}
