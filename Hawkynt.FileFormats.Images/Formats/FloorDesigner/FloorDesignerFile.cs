using System;
using FileFormat.Core;

namespace FileFormat.FloorDesigner;

/// <summary>In-memory representation of Atari 8-bit Floor Designer (.fge) screens.</summary>
/// <remarks>
/// A fixed 1286-byte file: 6 header bytes followed by an ANTIC mode F
/// ("Graphics 9") screen of 40 rows. Mode 9 gives 16 luminance levels of a single hue; each
/// stored nibble covers four screen pixels and each stored row four screen rows, so the picture is
/// displayed at 256x160.
/// </remarks>
public readonly record struct FloorDesignerFile
  : IImageFormatReader<FloorDesignerFile>, IImageToRawImage<FloorDesignerFile>,
    IImageFromRawImage<FloorDesignerFile>, IImageFormatWriter<FloorDesignerFile> {

  /// <summary>Header bytes preceding the screen.</summary>
  public const int HeaderSize = 6;

  /// <summary>Screen width in stored pixels.</summary>
  public const int ScreenWidth = 256;

  /// <summary>Stored row count.</summary>
  public const int ScreenRows = 40;

  /// <summary>Bytes per stored row.</summary>
  public const int BytesPerRow = ScreenWidth / 8;

  /// <summary>Size of the screen section.</summary>
  public const int ScreenDataSize = BytesPerRow * ScreenRows;

  /// <summary>Total file size.</summary>
  public const int FileSize = HeaderSize + ScreenDataSize;

  /// <summary>Displayed width.</summary>
  public const int DisplayWidth = 256;

  /// <summary>Displayed height.</summary>
  public const int DisplayHeight = 160;

  /// <summary>Luminance levels a Graphics 9 screen can show.</summary>
  public const int ColorCount = 16;

  static string IImageFormatMetadata<FloorDesignerFile>.PrimaryExtension => ".fge";
  static string[] IImageFormatMetadata<FloorDesignerFile>.FileExtensions => [".fge"];
  static FloorDesignerFile IImageFormatReader<FloorDesignerFile>.FromSpan(ReadOnlySpan<byte> data) => FloorDesignerReader.FromSpan(data);
  static byte[] IImageFormatWriter<FloorDesignerFile>.ToBytes(FloorDesignerFile file) => FloorDesignerWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<FloorDesignerFile>.VideoModes => [
    new("Graphics 9", [(DisplayWidth, DisplayHeight)], [ColorCount])
  ];

  /// <summary>Header bytes, preserved verbatim.</summary>
  public byte[] Header { get; init; }

  /// <summary>Packed Graphics 9 screen.</summary>
  public byte[] ScreenData { get; init; }

  public static RawImage ToRawImage(FloorDesignerFile file) {
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

  public static FloorDesignerFile FromRawImage(RawImage image) {
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
