using System;
using FileFormat.Core;

namespace FileFormat.DrawIt;

/// <summary>In-memory representation of a DrawIt (.dit) Atari 8-bit picture.</summary>
/// <remarks>
/// A fixed 3845-byte file: a 3840-byte ANTIC mode D ("Graphics 7") bitmap followed by the five
/// GTIA colour registers PF0-PF3 and BAK. The bitmap holds 160x96 logical pixels, displayed at
/// 320x192 with every pixel doubled in both directions.
/// </remarks>
public readonly record struct DrawItFile : IImageFormatReader<DrawItFile>, IImageToRawImage<DrawItFile>, IImageFromRawImage<DrawItFile>, IImageFormatWriter<DrawItFile> {

  /// <summary>Logical bitmap width.</summary>
  public const int BitmapWidth = Atari8BitGraphics.Gr7Width;

  /// <summary>Number of stored scanlines.</summary>
  public const int BitmapHeight = 96;

  /// <summary>Displayed width; each logical pixel is two screen pixels wide.</summary>
  public const int DisplayWidth = BitmapWidth * 2;

  /// <summary>Displayed height; each stored scanline is shown twice.</summary>
  public const int DisplayHeight = BitmapHeight * 2;

  /// <summary>Size of the bitmap section.</summary>
  public const int BitmapDataSize = Atari8BitGraphics.Gr7BytesPerRow * BitmapHeight;

  /// <summary>Offset of the five colour registers, immediately after the bitmap.</summary>
  public const int ColorRegisterOffset = BitmapDataSize;

  /// <summary>Total file size.</summary>
  public const int FileSize = BitmapDataSize + Atari8BitGraphics.ColorRegisterCount;

  /// <summary>Colours a Graphics 7 screen can show at once.</summary>
  public const int ColorCount = 4;

  static string IImageFormatMetadata<DrawItFile>.PrimaryExtension => ".dit";
  static string[] IImageFormatMetadata<DrawItFile>.FileExtensions => [".dit"];
  static DrawItFile IImageFormatReader<DrawItFile>.FromSpan(ReadOnlySpan<byte> data) => DrawItReader.FromSpan(data);
  static byte[] IImageFormatWriter<DrawItFile>.ToBytes(DrawItFile file) => DrawItWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<DrawItFile>.VideoModes => [
    new("Graphics 7", [(DisplayWidth, DisplayHeight)], [ColorCount])
  ];

  /// <summary>Packed Graphics 7 bitmap (<see cref="BitmapDataSize"/> bytes).</summary>
  public byte[] BitmapData { get; init; }

  /// <summary>The five GTIA colour registers: PF0, PF1, PF2, PF3, BAK.</summary>
  public byte[] ColorRegisters { get; init; }

  public static RawImage ToRawImage(DrawItFile file) {
    var pixels = Atari8BitGraphics.UnpackGr7(file.BitmapData, 0, BitmapHeight);
    var gtia = Atari8BitGraphics.CreatePalette();

    // Four usable colours, each taken from the register mode D assigns to that pixel value.
    var palette = new byte[ColorCount * 3];
    for (var value = 0; value < ColorCount; ++value) {
      var register = Atari8BitGraphics.RegisterForPixel(value);
      var colorByte = register < file.ColorRegisters.Length ? file.ColorRegisters[register] : (byte)0;
      Array.Copy(gtia, colorByte * 3, palette, value * 3, 3);
    }

    // Expand to the displayed resolution so the result has the aspect the hardware shows.
    var scaled = new byte[DisplayWidth * DisplayHeight];
    for (var y = 0; y < DisplayHeight; ++y)
    for (var x = 0; x < DisplayWidth; ++x)
      scaled[y * DisplayWidth + x] = pixels[(y >> 1) * BitmapWidth + (x >> 1)];

    return new() {
      Width = DisplayWidth,
      Height = DisplayHeight,
      Format = PixelFormat.Indexed8,
      PixelData = scaled,
      Palette = palette,
      PaletteCount = ColorCount,
    };
  }

  public static DrawItFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width != DisplayWidth || image.Height != DisplayHeight)
      throw new ArgumentException($"Expected {DisplayWidth}x{DisplayHeight} but got {image.Width}x{image.Height}.", nameof(image));

    // Reduce to the four colours mode D can show, then express those as GTIA colour registers.
    var indexed = PixelConverter.Convert(image, PixelFormat.Indexed4);
    var palette = indexed.Palette ?? [];
    var gtia = Atari8BitGraphics.CreatePalette();

    var registers = new byte[Atari8BitGraphics.ColorRegisterCount];
    for (var value = 0; value < ColorCount && value < indexed.PaletteCount; ++value) {
      var register = Atari8BitGraphics.RegisterForPixel(value);
      registers[register] = Atari8BitGraphics.FindNearestColorByte(
        gtia, palette[value * 3], palette[value * 3 + 1], palette[value * 3 + 2]);
    }

    // Collapse the displayed image back to the stored 160x96 grid, sampling each 2x2 block once.
    var chunky = ColorQuantizer.PackIndices(_Unpack4(indexed, DisplayWidth * DisplayHeight), PixelFormat.Indexed8);
    var pixels = new byte[BitmapWidth * BitmapHeight];
    for (var y = 0; y < BitmapHeight; ++y)
    for (var x = 0; x < BitmapWidth; ++x) {
      var index = chunky[y * 2 * DisplayWidth + x * 2];
      pixels[y * BitmapWidth + x] = (byte)(index < ColorCount ? index : 0);
    }

    return new() {
      BitmapData = Atari8BitGraphics.PackGr7(pixels, BitmapHeight),
      ColorRegisters = registers,
    };
  }

  /// <summary>Expands 4-bit packed indices to one index per pixel.</summary>
  private static int[] _Unpack4(RawImage indexed, int count) {
    var result = new int[count];
    for (var i = 0; i < count; ++i) {
      var b = indexed.PixelData[i >> 1];
      result[i] = (i & 1) == 0 ? (b >> 4) & 0x0F : b & 0x0F;
    }

    return result;
  }
}
