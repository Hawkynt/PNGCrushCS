using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.BrooktroutFax;

/// <summary>In-memory representation of a Brooktrout 301 fax image image.</summary>
public sealed class BrooktroutFaxFile : IImageFileFormat<BrooktroutFaxFile> {

  internal const int HeaderSize = 32;

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  static string IImageFileFormat<BrooktroutFaxFile>.PrimaryExtension => ".brk";
  static string[] IImageFileFormat<BrooktroutFaxFile>.FileExtensions => [".brk", ".301", ".brt"];
  static FormatCapability IImageFileFormat<BrooktroutFaxFile>.Capabilities => FormatCapability.MonochromeOnly;
  static BrooktroutFaxFile IImageFileFormat<BrooktroutFaxFile>.FromFile(FileInfo file) => BrooktroutFaxReader.FromFile(file);
  static BrooktroutFaxFile IImageFileFormat<BrooktroutFaxFile>.FromBytes(byte[] data) => BrooktroutFaxReader.FromBytes(data);
  static BrooktroutFaxFile IImageFileFormat<BrooktroutFaxFile>.FromStream(Stream stream) => BrooktroutFaxReader.FromStream(stream);
  static byte[] IImageFileFormat<BrooktroutFaxFile>.ToBytes(BrooktroutFaxFile file) => BrooktroutFaxWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(BrooktroutFaxFile file) {
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

  public static BrooktroutFaxFile FromRawImage(RawImage image) {
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
