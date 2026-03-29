using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.NokiaPictureMessage;

/// <summary>In-memory representation of a Nokia Picture Message (.npm) monochrome bitmap image.</summary>
public sealed class NokiaPictureMessageFile : IImageFileFormat<NokiaPictureMessageFile> {

  static string IImageFileFormat<NokiaPictureMessageFile>.PrimaryExtension => ".npm";
  static string[] IImageFileFormat<NokiaPictureMessageFile>.FileExtensions => [".npm"];
  static FormatCapability IImageFileFormat<NokiaPictureMessageFile>.Capabilities => FormatCapability.MonochromeOnly | FormatCapability.VariableResolution;
  static NokiaPictureMessageFile IImageFileFormat<NokiaPictureMessageFile>.FromFile(FileInfo file) => NokiaPictureMessageReader.FromFile(file);
  static NokiaPictureMessageFile IImageFileFormat<NokiaPictureMessageFile>.FromBytes(byte[] data) => NokiaPictureMessageReader.FromBytes(data);
  static NokiaPictureMessageFile IImageFileFormat<NokiaPictureMessageFile>.FromStream(Stream stream) => NokiaPictureMessageReader.FromStream(stream);
  static RawImage IImageFileFormat<NokiaPictureMessageFile>.ToRawImage(NokiaPictureMessageFile file) => file.ToRawImage();
  static byte[] IImageFileFormat<NokiaPictureMessageFile>.ToBytes(NokiaPictureMessageFile file) => NokiaPictureMessageWriter.ToBytes(file);

  /// <summary>Image width in pixels (1..255).</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels (1..255).</summary>
  public int Height { get; init; }

  /// <summary>1bpp packed pixel data, MSB first, ceil(width/8) bytes per row, no padding.</summary>
  public byte[] PixelData { get; init; } = [];

  // Nokia convention: 0=white, 1=black
  private static readonly byte[] _WhiteBlackPalette = [255, 255, 255, 0, 0, 0];

  public RawImage ToRawImage() => new() {
    Width = this.Width,
    Height = this.Height,
    Format = PixelFormat.Indexed1,
    PixelData = this.PixelData[..],
    Palette = _WhiteBlackPalette[..],
    PaletteCount = 2,
  };

  public static NokiaPictureMessageFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed1)
      throw new ArgumentException("RawImage must use PixelFormat.Indexed1.", nameof(image));
    if (image.Width is < 1 or > 255)
      throw new ArgumentOutOfRangeException(nameof(image), "NPM width must be in the range 1..255.");
    if (image.Height is < 1 or > 255)
      throw new ArgumentOutOfRangeException(nameof(image), "NPM height must be in the range 1..255.");

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
    };
  }
}
