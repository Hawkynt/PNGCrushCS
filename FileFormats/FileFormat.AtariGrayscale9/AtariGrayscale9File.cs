using System;
using FileFormat.Core;

namespace FileFormat.AtariGrayscale9;

/// <summary>In-memory representation of Atari 8-bit Graphics 9 greyscale (.bg9/.g09) screens.</summary>
/// <remarks>
/// A fixed 7680-byte file: 0 header bytes followed by an ANTIC mode F
/// ("Graphics 9") screen of 192 rows. Mode 9 gives 16 luminance levels of a single hue; each
/// stored nibble covers four screen pixels and each stored row four screen rows, so the picture is
/// displayed at 320x192.
/// </remarks>
public readonly record struct AtariGrayscale9File
  : IImageFormatReader<AtariGrayscale9File>, IImageToRawImage<AtariGrayscale9File>,
    IImageFromRawImage<AtariGrayscale9File>, IImageFormatWriter<AtariGrayscale9File> {

  /// <summary>Header bytes preceding the screen.</summary>
  public const int HeaderSize = 0;

  /// <summary>Screen width in stored pixels.</summary>
  public const int ScreenWidth = 320;

  /// <summary>Stored row count.</summary>
  public const int ScreenRows = 192;

  /// <summary>Bytes per stored row.</summary>
  public const int BytesPerRow = ScreenWidth / 8;

  /// <summary>Size of the screen section.</summary>
  public const int ScreenDataSize = BytesPerRow * ScreenRows;

  /// <summary>Total file size.</summary>
  public const int FileSize = HeaderSize + ScreenDataSize;

  /// <summary>Displayed width.</summary>
  public const int DisplayWidth = 320;

  /// <summary>Displayed height.</summary>
  public const int DisplayHeight = 192;

  /// <summary>Luminance levels a Graphics 9 screen can show.</summary>
  public const int ColorCount = 16;

  static string IImageFormatMetadata<AtariGrayscale9File>.PrimaryExtension => ".bg9";
  static string[] IImageFormatMetadata<AtariGrayscale9File>.FileExtensions => [".bg9", ".g09"];
  static AtariGrayscale9File IImageFormatReader<AtariGrayscale9File>.FromSpan(ReadOnlySpan<byte> data) => AtariGrayscale9Reader.FromSpan(data);
  static byte[] IImageFormatWriter<AtariGrayscale9File>.ToBytes(AtariGrayscale9File file) => AtariGrayscale9Writer.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<AtariGrayscale9File>.VideoModes => [
    new("Graphics 9", [(DisplayWidth, DisplayHeight)], [ColorCount])
  ];

  /// <summary>Header bytes, preserved verbatim.</summary>
  public byte[] Header { get; init; }

  /// <summary>Packed Graphics 9 screen.</summary>
  public byte[] ScreenData { get; init; }

  public static RawImage ToRawImage(AtariGrayscale9File file) {
    var pixels = Atari8BitGraphics.UnpackGr9(file.ScreenData, 0, ScreenWidth, ScreenRows);

    // Mode 9 renders 16 luminance steps of one hue; grey is the neutral choice without a
    // colour register to tell us which hue the artwork assumed.
    var palette = new byte[ColorCount * 3];
    for (var level = 0; level < ColorCount; ++level) {
      var v = (byte)(level * 255 / (ColorCount - 1));
      palette[level * 3] = v;
      palette[level * 3 + 1] = v;
      palette[level * 3 + 2] = v;
    }

    var scaled = new byte[DisplayWidth * DisplayHeight];
    for (var y = 0; y < DisplayHeight; ++y)
    for (var x = 0; x < DisplayWidth; ++x)
      scaled[y * DisplayWidth + x] = pixels[y * ScreenRows / DisplayHeight * ScreenWidth + x * ScreenWidth / DisplayWidth];

    return new() {
      Width = DisplayWidth,
      Height = DisplayHeight,
      Format = PixelFormat.Indexed8,
      PixelData = scaled,
      Palette = palette,
      PaletteCount = ColorCount,
    };
  }

  public static AtariGrayscale9File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width != DisplayWidth || image.Height != DisplayHeight)
      throw new ArgumentException($"Expected {DisplayWidth}x{DisplayHeight} but got {image.Width}x{image.Height}.", nameof(image));

    // Luminance only: take the grey level of each sampled pixel.
    var grey = PixelConverter.Convert(image, PixelFormat.Gray8);
    var pixels = new byte[ScreenWidth * ScreenRows];
    for (var y = 0; y < ScreenRows; ++y)
    for (var x = 0; x < ScreenWidth; ++x) {
      var sourceX = x * DisplayWidth / ScreenWidth;
      var sourceY = y * DisplayHeight / ScreenRows;
      pixels[y * ScreenWidth + x] = (byte)(grey.PixelData[sourceY * DisplayWidth + sourceX] * (ColorCount - 1) / 255);
    }

    return new() {
      Header = new byte[HeaderSize],
      ScreenData = Atari8BitGraphics.PackGr9(pixels, ScreenWidth, ScreenRows),
    };
  }
}
