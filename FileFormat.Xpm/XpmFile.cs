using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Xpm;

/// <summary>In-memory representation of an XPM image.</summary>
public sealed class XpmFile : IImageFileFormat<XpmFile> {

  static string IImageFileFormat<XpmFile>.PrimaryExtension => ".xpm";
  static string[] IImageFileFormat<XpmFile>.FileExtensions => [".xpm"];
  static FormatCapability IImageFileFormat<XpmFile>.Capabilities => FormatCapability.IndexedOnly;
  static XpmFile IImageFileFormat<XpmFile>.FromFile(FileInfo file) => XpmReader.FromFile(file);
  static XpmFile IImageFileFormat<XpmFile>.FromBytes(byte[] data) => XpmReader.FromBytes(data);
  static XpmFile IImageFileFormat<XpmFile>.FromStream(Stream stream) => XpmReader.FromStream(stream);
  static byte[] IImageFileFormat<XpmFile>.ToBytes(XpmFile file) => XpmWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public int CharsPerPixel { get; init; }
  public string Name { get; init; } = "image";
  public byte[] Palette { get; init; } = [];
  public int PaletteColorCount { get; init; }
  public int? TransparentIndex { get; init; }
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(XpmFile file) {
    ArgumentNullException.ThrowIfNull(file);
    byte[]? alphaTable = null;
    if (file.TransparentIndex.HasValue) {
      alphaTable = new byte[file.PaletteColorCount];
      Array.Fill(alphaTable, (byte)255);
      alphaTable[file.TransparentIndex.Value] = 0;
    }
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
      Palette = file.Palette[..],
      PaletteCount = file.PaletteColorCount,
      AlphaTable = alphaTable,
    };
  }

  public static XpmFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException($"Expected {PixelFormat.Indexed8} but got {image.Format}.", nameof(image));

    int? transparentIndex = null;
    if (image.AlphaTable != null) {
      for (var i = 0; i < image.AlphaTable.Length; ++i) {
        if (image.AlphaTable[i] == 0) {
          transparentIndex = i;
          break;
        }
      }
    }

    var paletteCount = image.PaletteCount;
    var charsPerPixel = paletteCount <= 92 ? 1 : 2;

    return new() {
      Width = image.Width,
      Height = image.Height,
      CharsPerPixel = charsPerPixel,
      Palette = image.Palette != null ? image.Palette[..] : [],
      PaletteColorCount = paletteCount,
      TransparentIndex = transparentIndex,
      PixelData = image.PixelData[..],
    };
  }
}
