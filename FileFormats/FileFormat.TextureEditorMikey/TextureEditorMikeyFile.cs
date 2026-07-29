using System;
using FileFormat.Core;

namespace FileFormat.TextureEditorMikey;

/// <summary>In-memory representation of Atari 8-bit Texture Editor by Mikey (.txe) screens.</summary>
/// <remarks>
/// A fixed 3840-byte file: 0 header bytes followed by an ANTIC mode F
/// ("Graphics 9") screen of 96 rows. Mode 9 gives 16 luminance levels of a single hue; each
/// stored nibble covers four screen pixels and each stored row four screen rows, so the picture is
/// displayed at 320x192.
/// </remarks>
public readonly record struct TextureEditorMikeyFile
  : IImageFormatReader<TextureEditorMikeyFile>, IImageToRawImage<TextureEditorMikeyFile>,
    IImageFromRawImage<TextureEditorMikeyFile>, IImageFormatWriter<TextureEditorMikeyFile> {

  /// <summary>Header bytes preceding the screen.</summary>
  public const int HeaderSize = 0;

  /// <summary>Screen width in stored pixels.</summary>
  public const int ScreenWidth = 320;

  /// <summary>Stored row count.</summary>
  public const int ScreenRows = 96;

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

  static string IImageFormatMetadata<TextureEditorMikeyFile>.PrimaryExtension => ".txe";
  static string[] IImageFormatMetadata<TextureEditorMikeyFile>.FileExtensions => [".txe"];
  static TextureEditorMikeyFile IImageFormatReader<TextureEditorMikeyFile>.FromSpan(ReadOnlySpan<byte> data) => TextureEditorMikeyReader.FromSpan(data);
  static byte[] IImageFormatWriter<TextureEditorMikeyFile>.ToBytes(TextureEditorMikeyFile file) => TextureEditorMikeyWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<TextureEditorMikeyFile>.VideoModes => [
    new("Graphics 9", [(DisplayWidth, DisplayHeight)], [ColorCount])
  ];

  /// <summary>Header bytes, preserved verbatim.</summary>
  public byte[] Header { get; init; }

  /// <summary>Packed Graphics 9 screen.</summary>
  public byte[] ScreenData { get; init; }

  public static RawImage ToRawImage(TextureEditorMikeyFile file) {
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

  public static TextureEditorMikeyFile FromRawImage(RawImage image) {
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
