using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.EdmicsC4;

/// <summary>In-memory representation of a EDMICS C4 fax image image.</summary>
public sealed class EdmicsC4File : IImageFileFormat<EdmicsC4File> {

  internal const int HeaderSize = 16;

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  static string IImageFileFormat<EdmicsC4File>.PrimaryExtension => ".c4";
  static string[] IImageFileFormat<EdmicsC4File>.FileExtensions => [".c4"];
  static FormatCapability IImageFileFormat<EdmicsC4File>.Capabilities => FormatCapability.MonochromeOnly;
  static EdmicsC4File IImageFileFormat<EdmicsC4File>.FromFile(FileInfo file) => EdmicsC4Reader.FromFile(file);
  static EdmicsC4File IImageFileFormat<EdmicsC4File>.FromBytes(byte[] data) => EdmicsC4Reader.FromBytes(data);
  static EdmicsC4File IImageFileFormat<EdmicsC4File>.FromStream(Stream stream) => EdmicsC4Reader.FromStream(stream);
  static byte[] IImageFileFormat<EdmicsC4File>.ToBytes(EdmicsC4File file) => EdmicsC4Writer.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(EdmicsC4File file) {
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

  public static EdmicsC4File FromRawImage(RawImage image) {
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
