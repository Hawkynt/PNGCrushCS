using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Jbig;

/// <summary>In-memory representation of a JBIG1 (ITU-T T.82) bi-level image.</summary>
public sealed class JbigFile : IImageFileFormat<JbigFile> {

  static string IImageFileFormat<JbigFile>.PrimaryExtension => ".jbg";
  static string[] IImageFileFormat<JbigFile>.FileExtensions => [".jbg", ".bie", ".jbig"];
  static FormatCapability IImageFileFormat<JbigFile>.Capabilities => FormatCapability.MonochromeOnly;
  static JbigFile IImageFileFormat<JbigFile>.FromFile(FileInfo file) => JbigReader.FromFile(file);
  static JbigFile IImageFileFormat<JbigFile>.FromBytes(byte[] data) => JbigReader.FromBytes(data);
  static JbigFile IImageFileFormat<JbigFile>.FromStream(Stream stream) => JbigReader.FromStream(stream);
  static RawImage IImageFileFormat<JbigFile>.ToRawImage(JbigFile file) => file.ToRawImage();
  static byte[] IImageFileFormat<JbigFile>.ToBytes(JbigFile file) => JbigWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }

  /// <summary>1bpp packed pixel data, MSB first, ceil(width/8) bytes per row.</summary>
  public byte[] PixelData { get; init; } = [];

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  public RawImage ToRawImage() => new() {
    Width = this.Width,
    Height = this.Height,
    Format = PixelFormat.Indexed1,
    PixelData = this.PixelData[..],
    Palette = _BlackWhitePalette[..],
    PaletteCount = 2,
  };

  public static JbigFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed1)
      throw new ArgumentException("RawImage must use PixelFormat.Indexed1.", nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
    };
  }
}
