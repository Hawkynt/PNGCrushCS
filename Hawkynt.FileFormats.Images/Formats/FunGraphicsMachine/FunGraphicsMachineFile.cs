using System;
using FileFormat.Core;

namespace FileFormat.FunGraphicsMachine;

/// <summary>In-memory representation of a Commodore 64 Fun Graphics Machine hires image.</summary>
public readonly record struct FunGraphicsMachineFile : IImageFormatReader<FunGraphicsMachineFile>, IImageToRawImage<FunGraphicsMachineFile>, IImageFromRawImage<FunGraphicsMachineFile>, IImageFormatWriter<FunGraphicsMachineFile> {

  static string IImageFormatMetadata<FunGraphicsMachineFile>.PrimaryExtension => ".fgs";
  static string[] IImageFormatMetadata<FunGraphicsMachineFile>.FileExtensions => [".fgs"];
  static FunGraphicsMachineFile IImageFormatReader<FunGraphicsMachineFile>.FromSpan(ReadOnlySpan<byte> data) => FunGraphicsMachineReader.FromSpan(data);
  static byte[] IImageFormatWriter<FunGraphicsMachineFile>.ToBytes(FunGraphicsMachineFile file) => FunGraphicsMachineWriter.ToBytes(file);

  /// <summary>The fixed width of a Fun Graphics Machine image in pixels.</summary>
  public const int FixedWidth = 320;

  /// <summary>The fixed height of a Fun Graphics Machine image in pixels.</summary>
  public const int FixedHeight = 200;

  /// <summary>The expected total file size in bytes (2 + 1000 + 8000 + 7).</summary>
  public const int ExpectedFileSize = 9009;

  /// <summary>
  /// The size of a picture that carries no screen RAM: a load address and the bitmap alone.
  /// </summary>
  /// <remarks>
  /// The only sample in the corpus is this form, and it was refused for not being the other one while
  /// RECOIL and XnView both drew it. With no screen RAM there is nothing to say what the two colours
  /// of a cell are, and both tools draw it white on black.
  /// </remarks>
  public const int BitmapOnlyFileSize = LoadAddressSize + BitmapDataSize;

  /// <summary>Ink white, paper black — the screen a picture that states none is drawn against.</summary>
  internal const byte DefaultScreenAttribute = 0x10;

  /// <summary>Size of the bitmap data section in bytes.</summary>
  internal const int BitmapDataSize = 8000;

  /// <summary>Size of the screen RAM section in bytes.</summary>
  internal const int ScreenRamSize = 1000;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Size of the trailing padding in bytes.</summary>
  internal const int PaddingSize = 7;

  /// <summary>Image width, always 320.</summary>
  public int Width => FixedWidth;

  /// <summary>Image height, always 200.</summary>
  public int Height => FixedHeight;

  /// <summary>C64 memory load address (2 bytes, little-endian).</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>Screen RAM (1000 bytes, upper nybble = foreground color, lower nybble = background color per cell).</summary>
  public byte[] ScreenRam { get; init; }

  /// <summary>Hires bitmap data (8000 bytes, 1 bit per pixel within 8x8 cells).</summary>
  public byte[] BitmapData { get; init; }

  /// <summary>Converts this Fun Graphics Machine image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  /// <summary>
  /// Reduces a picture to the bitmap-only form, which is what these files are.
  /// </summary>
  /// <remarks>
  /// The sample is 8002 bytes — a load address and the bitmap, with no screen memory — and RECOIL
  /// refuses the 9009-byte form with one. A file without a screen is drawn in the single attribute
  /// the machine defaults to, so the picture it can hold is black and white and nothing else, and
  /// choosing two colours a cell would be choosing something there is nowhere to write down.
  /// </remarks>
  public static FunGraphicsMachineFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(FixedWidth, FixedHeight).PixelData;
    var bitmap = new byte[BitmapDataSize];

    // A cell is eight lines together, so a row of the picture is not a row of the bitmap.
    for (var y = 0; y < FixedHeight; ++y)
      for (var x = 0; x < FixedWidth; ++x) {
        var at = (y * FixedWidth + x) * 3;
        var brightness = (rgb[at] * 299 + rgb[at + 1] * 587 + rgb[at + 2] * 114) / 1000;
        if (brightness < 128)
          continue;

        var cell = (y / 8) * (FixedWidth / 8) + x / 8;
        bitmap[cell * 8 + y % 8] |= (byte)(1 << (7 - (x % 8)));
      }

    var screen = new byte[ScreenRamSize];
    Array.Fill(screen, DefaultScreenAttribute);

    return new() { LoadAddress = 0x4000, BitmapData = bitmap, ScreenRam = screen };
  }

  public static RawImage ToRawImage(FunGraphicsMachineFile file)
    => Commodore64Graphics.DecodeHires(file.BitmapData, file.ScreenRam, FixedWidth, FixedHeight);

}
