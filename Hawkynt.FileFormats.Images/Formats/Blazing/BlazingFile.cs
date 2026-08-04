using System;
using FileFormat.Core;

namespace FileFormat.Blazing;

/// <summary>In-memory representation of a Blazing Paddles hires image (C64, 320x200, 1bpp cell-based).</summary>
public readonly record struct BlazingFile : IImageFormatReader<BlazingFile>, IImageToRawImage<BlazingFile>, IImageFromRawImage<BlazingFile>, IImageFormatWriter<BlazingFile> {

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Size of the bitmap data section in bytes (320x200 / 8 = 8000).</summary>
  internal const int BitmapDataSize = 8000;

  /// <summary>Size of the screen RAM section in bytes (40x25 = 1000).</summary>
  internal const int ScreenDataSize = 1000;

  /// <summary>Padding bytes at end of file.</summary>
  internal const int PaddingSize = 7;

  /// <summary>Expected file size: 2 + 8000 + 1000 + 7 = 9009.</summary>
  public const int ExpectedFileSize = LoadAddressSize + BitmapDataSize + ScreenDataSize + PaddingSize;

  /// <summary>
  /// The size of the multicolour form: a load address and three sections rounded up to kilobytes.
  /// </summary>
  /// <remarks>
  /// Both Blazing Paddles pictures in the corpus are this form, and neither was read — the hires one
  /// above wants 9009 bytes and they are 10242. The sections are padded out: the bitmap takes 8192 of
  /// the 8000 it uses, and the screen and colour RAM a kilobyte each of their thousand bytes, which is
  /// 10240 after the load address and the file to the byte.
  /// <para/>
  /// Nothing states a background; both samples come out right against RECOIL with it black.
  /// </remarks>
  public const int MulticolorFileSize = LoadAddressSize + 8192 + 1024 + 1024;

  /// <summary>Where the video matrix starts in the multicolour form.</summary>
  internal const int MulticolorScreenOffset = LoadAddressSize + 8192;

  /// <summary>Where the colour RAM starts in the multicolour form.</summary>
  internal const int MulticolorColorOffset = MulticolorScreenOffset + 1024;

  /// <summary>Default load address ($2000).</summary>
  internal const ushort DefaultLoadAddress = 0x2000;

  /// <summary>Image width in pixels.</summary>
  internal const int PixelWidth = 320;

  /// <summary>Image height in pixels.</summary>
  internal const int PixelHeight = 200;

  static string IImageFormatMetadata<BlazingFile>.PrimaryExtension => ".blz";
  /// <summary>Also .pi, which both samples in the corpus carry.</summary>
  static string[] IImageFormatMetadata<BlazingFile>.FileExtensions => [".blz", ".pi"];
  static BlazingFile IImageFormatReader<BlazingFile>.FromSpan(ReadOnlySpan<byte> data) => BlazingReader.FromSpan(data);
  static byte[] IImageFormatWriter<BlazingFile>.ToBytes(BlazingFile file) => BlazingWriter.ToBytes(file);

  /// <summary>Always 320.</summary>
  public int Width => PixelWidth;

  /// <summary>Always 200.</summary>
  public int Height => PixelHeight;

  /// <summary>C64 memory load address (2 bytes, little-endian).</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>Bitmap data (8000 bytes).</summary>
  public byte[] BitmapData { get; init; }

  /// <summary>Screen RAM / video matrix (1000 bytes). Upper nybble = foreground color, lower nybble = background color per 8x8 cell.</summary>
  public byte[] ScreenData { get; init; }

  /// <summary>Colour RAM, which only the multicolour form has.</summary>
  public byte[] ColorData { get; init; }

  /// <summary>
  /// Reduces a picture to the multicolour screen, which is the form Blazing Paddles saved.
  /// </summary>
  /// <remarks>
  /// The high-resolution form is written too when a file already holds one, but nothing produces one
  /// from a picture: both samples are multicolour and RECOIL refuses the hires length at either of
  /// these extensions, so a hires file built here would be one nothing could open.
  /// <para/>
  /// Pattern 00 is told to use black rather than left to choose. The format keeps no register for
  /// the background — there is nowhere in the file to record what was picked — and black is what
  /// both samples come out right against RECOIL with.
  /// </remarks>
  public static BlazingFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(PixelWidth / 2, PixelHeight);
    var bitmap = new byte[BitmapDataSize];
    var screen = new byte[ScreenDataSize];
    var colors = new byte[ScreenDataSize];
    Commodore64Graphics.EncodeMulticolor(
      rgb.PixelData, PixelWidth / 2, PixelHeight, bitmap, screen, colors, fixedBackground: 0);

    return new() {
      LoadAddress = DefaultLoadAddress,
      BitmapData = bitmap,
      ScreenData = screen,
      ColorData = colors,
    };
  }

  /// <summary>Converts this Blazing Paddles image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(BlazingFile file) {
    if (file.ColorData != null)
      return Commodore64Graphics.DecodeMulticolor(file.BitmapData, file.ScreenData, file.ColorData, 0, PixelWidth / 2, PixelHeight);

    var rgb = new byte[PixelWidth * PixelHeight * 3];

    for (var y = 0; y < PixelHeight; ++y)
      for (var x = 0; x < PixelWidth; ++x) {
        var cellX = x / 8;
        var cellY = y / 8;
        var cellIndex = cellY * 40 + cellX;
        var byteInCell = y % 8;
        var bitmapByte = file.BitmapData[cellIndex * 8 + byteInCell];
        var bitPosition = 7 - (x % 8);
        var bitValue = (bitmapByte >> bitPosition) & 1;

        var screenByte = file.ScreenData[cellIndex];
        var colorIndex = bitValue == 1
          ? (screenByte >> 4) & 0x0F
          : screenByte & 0x0F;

        var color = Commodore64Graphics.HexColors[colorIndex];
        var offset = (y * PixelWidth + x) * 3;
        rgb[offset] = (byte)((color >> 16) & 0xFF);
        rgb[offset + 1] = (byte)((color >> 8) & 0xFF);
        rgb[offset + 2] = (byte)(color & 0xFF);
      }

    return new() {
      Width = PixelWidth,
      Height = PixelHeight,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

}
