using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.MgrBitmap;

/// <summary>In-memory representation of an MGR (MGR Window Manager) bitmap image.</summary>
public sealed class MgrBitmapFile : IImageFileFormat<MgrBitmapFile> {

  static string IImageFileFormat<MgrBitmapFile>.PrimaryExtension => ".mgr";
  static string[] IImageFileFormat<MgrBitmapFile>.FileExtensions => [".mgr"];
  static FormatCapability IImageFileFormat<MgrBitmapFile>.Capabilities => FormatCapability.MonochromeOnly;
  static MgrBitmapFile IImageFileFormat<MgrBitmapFile>.FromFile(FileInfo file) => MgrBitmapReader.FromFile(file);
  static MgrBitmapFile IImageFileFormat<MgrBitmapFile>.FromBytes(byte[] data) => MgrBitmapReader.FromBytes(data);
  static MgrBitmapFile IImageFileFormat<MgrBitmapFile>.FromStream(Stream stream) => MgrBitmapReader.FromStream(stream);
  static RawImage IImageFileFormat<MgrBitmapFile>.ToRawImage(MgrBitmapFile file) => ToRawImage(file);
  static byte[] IImageFileFormat<MgrBitmapFile>.ToBytes(MgrBitmapFile file) => MgrBitmapWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }

  /// <summary>1bpp packed pixel data, MSB first, ceil(width/8) bytes per row.</summary>
  public byte[] PixelData { get; init; } = [];

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  public static RawImage ToRawImage(MgrBitmapFile file) {
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

  public static MgrBitmapFile FromRawImage(RawImage image) => throw new NotSupportedException("MGR writing from raw image is not supported.");
}
