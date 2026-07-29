using System;
using FileFormat.Core;

namespace FileFormat.MsxScreen6;

/// <summary>In-memory representation of an MSX2 Screen 6 (.sc6) image.</summary>
/// <remarks>
/// A BSAVE header, then a 512-pixel-wide bitmap at two bits per pixel — four colours chosen from
/// the V9938's 512, stored as a palette near the end of the video page. The stored 212 lines are
/// shown on 424 scanlines, so a Screen 6 picture is 512x424 on screen.
/// </remarks>
[FormatMagicBytes([0xFE])]
public readonly record struct MsxScreen6File
  : IImageFormatReader<MsxScreen6File>, IImageToRawImage<MsxScreen6File>,
    IImageFromRawImage<MsxScreen6File>, IImageFormatWriter<MsxScreen6File> {

  /// <summary>BSAVE header magic byte.</summary>
  public const byte BsaveMagic = 0xFE;

  /// <summary>BSAVE header size in bytes.</summary>
  public const int BsaveHeaderSize = 7;

  /// <summary>Stored pixels per row.</summary>
  public const int StoredWidth = 512;

  /// <summary>Stored rows.</summary>
  public const int StoredHeight = 212;

  /// <summary>Bytes per stored row: four pixels per byte.</summary>
  public const int BytesPerRow = StoredWidth / 4;

  /// <summary>Size of the bitmap.</summary>
  public const int PixelDataSize = BytesPerRow * StoredHeight;

  /// <summary>Colours a Screen 6 picture can show at once.</summary>
  public const int ColorCount = 4;

  /// <summary>Size of the stored palette: two bytes per entry.</summary>
  public const int PaletteSize = ColorCount * 2;

  /// <summary>Offset of the palette within the file; it sits near the end of the video page.</summary>
  public const int PaletteOffset = 30343;

  /// <summary>Total size of a file carrying a palette.</summary>
  public const int FileSize = PaletteOffset + PaletteSize;

  /// <summary>Displayed width.</summary>
  public const int DisplayWidth = StoredWidth;

  /// <summary>Displayed height; every stored row is shown twice.</summary>
  public const int DisplayHeight = StoredHeight * 2;

  /// <summary>
  /// The end address the BSAVE header must carry. Readers derive the picture height from it, so it
  /// has to describe the whole bitmap rather than the whole file.
  /// </summary>
  public const int BsaveEndAddress = PixelDataSize - 1;

  static string IImageFormatMetadata<MsxScreen6File>.PrimaryExtension => ".sc6";
  static string[] IImageFormatMetadata<MsxScreen6File>.FileExtensions => [".sc6"];
  static MsxScreen6File IImageFormatReader<MsxScreen6File>.FromSpan(ReadOnlySpan<byte> data) => MsxScreen6Reader.FromSpan(data);
  static byte[] IImageFormatWriter<MsxScreen6File>.ToBytes(MsxScreen6File file) => MsxScreen6Writer.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<MsxScreen6File>.VideoModes => [
    new("Screen 6", [(DisplayWidth, DisplayHeight)], [ColorCount])
  ];

  /// <summary>The bitmap, four pixels per byte, most significant pair leftmost.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>The V9938 palette: two bytes per entry, 0RRR0BBB then 00000GGG.</summary>
  public byte[] Palette { get; init; }

  /// <summary>Converts a V9938 palette to RGB triplets.</summary>
  internal static byte[] PaletteToRgb(byte[]? palette) {
    var rgb = new byte[ColorCount * 3];
    if (palette == null)
      return rgb;

    for (var i = 0; i < ColorCount && i * 2 + 1 < palette.Length; ++i) {
      // Three bits per channel, red and blue sharing the first byte.
      rgb[i * 3] = ChannelScaling.Expand3((palette[i * 2] >> 4) & 7);
      rgb[i * 3 + 1] = ChannelScaling.Expand3(palette[i * 2 + 1] & 7);
      rgb[i * 3 + 2] = ChannelScaling.Expand3(palette[i * 2] & 7);
    }

    return rgb;
  }

  /// <summary>Converts RGB triplets back to a V9938 palette.</summary>
  internal static byte[] PaletteFromRgb(ReadOnlySpan<byte> rgb, int count) {
    var palette = new byte[PaletteSize];
    for (var i = 0; i < ColorCount && i < count; ++i) {
      palette[i * 2] = (byte)((((rgb[i * 3] * 7 + 127) / 255) << 4) | ((rgb[i * 3 + 2] * 7 + 127) / 255));
      palette[i * 2 + 1] = (byte)((rgb[i * 3 + 1] * 7 + 127) / 255);
    }

    return palette;
  }

  public static RawImage ToRawImage(MsxScreen6File file) {
    var data = file.PixelData ?? [];
    var pixels = new byte[DisplayWidth * DisplayHeight];

    for (var y = 0; y < DisplayHeight; ++y)
    for (var x = 0; x < DisplayWidth; ++x) {
      var index = (y >> 1) * BytesPerRow + (x >> 2);
      var b = index < data.Length ? data[index] : 0;
      pixels[y * DisplayWidth + x] = (byte)((b >> ((~x & 3) << 1)) & 3);
    }

    return new() {
      Width = DisplayWidth,
      Height = DisplayHeight,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = PaletteToRgb(file.Palette),
      PaletteCount = ColorCount,
    };
  }

  public static MsxScreen6File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width != DisplayWidth || image.Height != DisplayHeight)
      throw new ArgumentException($"Expected {DisplayWidth}x{DisplayHeight} but got {image.Width}x{image.Height}.", nameof(image));

    var bgra = PixelConverter.Convert(image, PixelFormat.Bgra32);
    var quantized = ColorQuantizer.Quantize(bgra.PixelData, DisplayWidth * DisplayHeight, ColorCount);

    var data = new byte[PixelDataSize];
    for (var y = 0; y < StoredHeight; ++y)
    for (var x = 0; x < StoredWidth; ++x) {
      // Only the first of each pair of displayed scanlines is stored.
      var value = quantized.Indices[y * 2 * DisplayWidth + x] & 3;
      data[y * BytesPerRow + (x >> 2)] |= (byte)(value << ((~x & 3) << 1));
    }

    return new() { PixelData = data, Palette = PaletteFromRgb(quantized.Palette, quantized.Count) };
  }
}
