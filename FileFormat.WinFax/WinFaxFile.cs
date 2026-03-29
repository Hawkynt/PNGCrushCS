using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.WinFax;

/// <summary>In-memory representation of a WinFAX fax image image.</summary>
public sealed class WinFaxFile : IImageFileFormat<WinFaxFile> {

  internal const int HeaderSize = 16;

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  static string IImageFileFormat<WinFaxFile>.PrimaryExtension => ".fxs";
  static string[] IImageFileFormat<WinFaxFile>.FileExtensions => [".fxs", ".fxo", ".fxr", ".fxd", ".fxm"];
  static FormatCapability IImageFileFormat<WinFaxFile>.Capabilities => FormatCapability.MonochromeOnly;
  static WinFaxFile IImageFileFormat<WinFaxFile>.FromFile(FileInfo file) => WinFaxReader.FromFile(file);
  static WinFaxFile IImageFileFormat<WinFaxFile>.FromBytes(byte[] data) => WinFaxReader.FromBytes(data);
  static WinFaxFile IImageFileFormat<WinFaxFile>.FromStream(Stream stream) => WinFaxReader.FromStream(stream);
  static byte[] IImageFileFormat<WinFaxFile>.ToBytes(WinFaxFile file) => WinFaxWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(WinFaxFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed1,
      PixelData = file.PixelData[..],
      Palette = _BlackWhitePalette[..],
      PaletteCount = 2,
    };
  }

  public static WinFaxFile FromRawImage(RawImage image) {
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
