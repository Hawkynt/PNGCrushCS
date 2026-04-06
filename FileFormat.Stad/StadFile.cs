using System;
using FileFormat.Core;

namespace FileFormat.Stad;

/// <summary>In-memory representation of a STAD compressed Atari ST high-res screen image.</summary>
public readonly record struct StadFile : IImageFormatReader<StadFile>, IImageToRawImage<StadFile>, IImageFormatWriter<StadFile> {

  /// <summary>Decompressed screen data size in bytes (640x400 monochrome = 32000 bytes).</summary>
  internal const int ScreenDataSize = 32000;

  /// <summary>Fixed pixel width.</summary>
  internal const int PixelWidth = 640;

  /// <summary>Fixed pixel height.</summary>
  internal const int PixelHeight = 400;

  /// <summary>Bytes per pixel row (640 / 8).</summary>
  internal const int BytesPerRow = PixelWidth / 8;

  static string IImageFormatMetadata<StadFile>.PrimaryExtension => ".pac";
  static string[] IImageFormatMetadata<StadFile>.FileExtensions => [".pac"];
  static StadFile IImageFormatReader<StadFile>.FromSpan(ReadOnlySpan<byte> data) => StadReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<StadFile>.Capabilities => FormatCapability.MonochromeOnly;
  static byte[] IImageFormatWriter<StadFile>.ToBytes(StadFile file) => StadWriter.ToBytes(file);

  /// <summary>Always 640.</summary>
  public int Width => PixelWidth;

  /// <summary>Always 400.</summary>
  public int Height => PixelHeight;

  /// <summary>Raw decompressed screen data (32000 bytes).</summary>
  public byte[] RawData { get; init; }

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  /// <summary>Converts the STAD screen to an Indexed1 raw image (640x400, B&amp;W palette).
  /// Bit=1 means black (palette index 0), bit=0 means white (palette index 1).</summary>
  public static RawImage ToRawImage(StadFile file) {

    var pixelData = new byte[BytesPerRow * PixelHeight];
    var len = Math.Min(file.RawData.Length, pixelData.Length);
    file.RawData.AsSpan(0, len).CopyTo(pixelData);

    return new() {
      Width = PixelWidth,
      Height = PixelHeight,
      Format = PixelFormat.Indexed1,
      PixelData = pixelData,
      Palette = _BlackWhitePalette[..],
      PaletteCount = 2,
    };
  }

}
