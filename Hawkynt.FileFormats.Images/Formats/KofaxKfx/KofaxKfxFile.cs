using System;
using FileFormat.Core;

namespace FileFormat.KofaxKfx;

/// <summary>In-memory representation of a Kofax KFX image.</summary>
/// <remarks>
/// Not a Group 4 fax despite the name it was given here: the sample is a plain bitmap, one bit a
/// pixel, seven bytes a row, and its length is exactly seven times its height with nothing else in
/// it. Nothing in the file states a size, so the width is taken as the 56 pixels those seven bytes
/// hold and the height follows from the length — which is what RECOIL makes of the same file, pixel
/// for pixel.
/// <para/>
/// That rests on one sample and one tool, RECOIL being the only one here that reads it at all. A
/// second sample of a different size would say whether the width is really fixed or is stated
/// somewhere this does not look.
/// </remarks>
public readonly record struct KofaxKfxFile : IImageFormatReader<KofaxKfxFile>, IImageToRawImage<KofaxKfxFile>, IImageFromRawImage<KofaxKfxFile>, IImageFormatWriter<KofaxKfxFile> {

  /// <summary>Bytes one row takes, which is what fixes the width.</summary>
  internal const int BytesPerRow = 7;

  /// <summary>Pixels a row holds.</summary>
  internal const int RowWidth = BytesPerRow * 8;

  internal const int HeaderSize = 0;

  /// <summary>Black and the light grey the picture is drawn in, which is what RECOIL renders.</summary>
  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 0xEE, 0xEE, 0xEE];

  static string IImageFormatMetadata<KofaxKfxFile>.PrimaryExtension => ".kfx";
  static string[] IImageFormatMetadata<KofaxKfxFile>.FileExtensions => [".kfx"];
  static KofaxKfxFile IImageFormatReader<KofaxKfxFile>.FromSpan(ReadOnlySpan<byte> data) => KofaxKfxReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<KofaxKfxFile>.VideoModes => [new("Default", [(IntegerRange.Any, IntegerRange.Any)], [2])];
  static byte[] IImageFormatWriter<KofaxKfxFile>.ToBytes(KofaxKfxFile file) => KofaxKfxWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(KofaxKfxFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed1,
      PixelData = file.PixelData[..],
      Palette = _BlackWhitePalette[..],
      PaletteCount = 2,
    };
  }

  public static KofaxKfxFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureFormat(PixelFormat.Indexed1);
    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
    };
  }
}
