using System;
using FileFormat.Core;

namespace FileFormat.WinFax;

/// <summary>In-memory representation of a WinFAX fax image image.</summary>
public readonly record struct WinFaxFile : IImageFormatReader<WinFaxFile>, IImageToRawImage<WinFaxFile>, IImageFromRawImage<WinFaxFile>, IImageFormatWriter<WinFaxFile> {

  /// <summary>The two bytes every one of these begins with.</summary>
  internal static ReadOnlySpan<byte> Signature => [0x0B, 0x23];

  /// <summary>Offset of the vertical resolution in dots per inch.</summary>
  internal const int ResolutionOffset = 2;

  /// <summary>Offset of the width, as a 16-bit little-endian count of pixels.</summary>
  internal const int WidthOffset = 3;

  /// <summary>Offset of the height, in the same shape.</summary>
  internal const int HeightOffset = 5;

  /// <summary>
  /// Bytes before the coded page. What follows the size fields is the title the sending program gave
  /// the document, and the page runs from there — the coded stream begins with a synchronising code,
  /// so the text ahead of it is skipped rather than decoded. Starting past the title instead was
  /// tried and decodes nothing at all.
  /// </summary>
  internal const int HeaderSize = 8;

  /// <summary>White first: a fax states runs of white before black, and zero means paper.</summary>
  private static readonly byte[] _BlackWhitePalette = [255, 255, 255, 0, 0, 0];

  static string IImageFormatMetadata<WinFaxFile>.PrimaryExtension => ".fxs";
  static string[] IImageFormatMetadata<WinFaxFile>.FileExtensions => [".fxs", ".fxo", ".fxr", ".fxd", ".fxm"];
  static WinFaxFile IImageFormatReader<WinFaxFile>.FromSpan(ReadOnlySpan<byte> data) => WinFaxReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<WinFaxFile>.VideoModes => [new("Default", [(IntegerRange.Any, IntegerRange.Any)], [2])];
  static byte[] IImageFormatWriter<WinFaxFile>.ToBytes(WinFaxFile file) => WinFaxWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(WinFaxFile file) {
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
    image = image.EnsureFormat(PixelFormat.Indexed1);
    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
    };
  }
}
