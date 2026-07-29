using System;
using FileFormat.Core;

namespace FileFormat.FunGraphicsMachine;

/// <summary>In-memory representation of a Commodore 64 Fun Graphics Machine hires image.</summary>
public readonly record struct FunGraphicsMachineFile : IImageFormatReader<FunGraphicsMachineFile>, IImageToRawImage<FunGraphicsMachineFile>, IImageFormatWriter<FunGraphicsMachineFile> {

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
  public static RawImage ToRawImage(FunGraphicsMachineFile file)
    => Commodore64Graphics.DecodeHires(file.BitmapData, file.ScreenRam, FixedWidth, FixedHeight);

}
