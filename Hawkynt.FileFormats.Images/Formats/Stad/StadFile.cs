using System;
using FileFormat.Core;

namespace FileFormat.Stad;

/// <summary>In-memory representation of a STAD compressed Atari ST high-res screen image.</summary>
public readonly record struct StadFile : IImageFormatReader<StadFile>, IImageToRawImage<StadFile>, IImageFromRawImage<StadFile>, IImageFormatWriter<StadFile> {

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
  static VideoMode[] IImageFormatMetadata<StadFile>.VideoModes => [new("Default", [(IntegerRange.Any, IntegerRange.Any)], [2])];
  static byte[] IImageFormatWriter<StadFile>.ToBytes(StadFile file) => StadWriter.ToBytes(file);

  /// <summary>Always 640.</summary>
  public int Width => PixelWidth;

  /// <summary>Always 400.</summary>
  public int Height => PixelHeight;

  /// <summary>Raw decompressed screen data (32000 bytes).</summary>
  public byte[] RawData { get; init; }

  /// <summary>Paper then ink: a clear bit is white, a set bit is black.</summary>
  /// <remarks>
  /// These were the other way round, so every STAD came out as its own negative — which the comment
  /// below already said was wrong, having claimed a set bit meant black while the palette put white
  /// there. All three samples now agree with RECOIL and XnView rather than inverting them.
  /// </remarks>
  private static readonly byte[] _BlackWhitePalette = [255, 255, 255, 0, 0, 0];

  /// <summary>Converts the STAD screen to an Indexed1 raw image (640x400, B&amp;W palette).
  /// Bit=1 means black (palette index 0), bit=0 means white (palette index 1).</summary>
  /// <summary>
  /// Reduces a picture to the monochrome screen, a set bit standing for black.
  /// </summary>
  /// <remarks>
  /// The screen is uncompressed here; the packing that makes a STAD a STAD is the writer's, and it
  /// picks its escape bytes from what the screen turns out to contain.
  /// <para/>
  /// The threshold is on brightness rather than on any one channel, so a coloured picture comes out
  /// as the light and dark of itself.
  /// </remarks>
  public static StadFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(PixelWidth, PixelHeight).PixelData;
    var screen = new byte[ScreenDataSize];

    for (var y = 0; y < PixelHeight; ++y)
      for (var x = 0; x < PixelWidth; ++x) {
        var at = (y * PixelWidth + x) * 3;
        var brightness = (rgb[at] * 299 + rgb[at + 1] * 587 + rgb[at + 2] * 114) / 1000;
        if (brightness < 128)
          screen[y * BytesPerRow + x / 8] |= (byte)(1 << (7 - (x % 8)));
      }

    return new() { RawData = screen };
  }

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
