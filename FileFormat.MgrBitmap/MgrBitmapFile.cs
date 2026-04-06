using System;
using FileFormat.Core;

namespace FileFormat.MgrBitmap;

/// <summary>In-memory representation of an MGR (MGR Window Manager) bitmap image.</summary>
public readonly record struct MgrBitmapFile : IImageFormatReader<MgrBitmapFile>, IImageToRawImage<MgrBitmapFile>, IImageFormatWriter<MgrBitmapFile> {

  static string IImageFormatMetadata<MgrBitmapFile>.PrimaryExtension => ".mgr";
  static string[] IImageFormatMetadata<MgrBitmapFile>.FileExtensions => [".mgr"];
  static MgrBitmapFile IImageFormatReader<MgrBitmapFile>.FromSpan(ReadOnlySpan<byte> data) => MgrBitmapReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<MgrBitmapFile>.Capabilities => FormatCapability.MonochromeOnly;
  static byte[] IImageFormatWriter<MgrBitmapFile>.ToBytes(MgrBitmapFile file) => MgrBitmapWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }

  /// <summary>1bpp packed pixel data, MSB first, ceil(width/8) bytes per row.</summary>
  public byte[] PixelData { get; init; }

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  public static RawImage ToRawImage(MgrBitmapFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed1,
      PixelData = file.PixelData[..],
      Palette = _BlackWhitePalette[..],
      PaletteCount = 2,
    };
  }

}
