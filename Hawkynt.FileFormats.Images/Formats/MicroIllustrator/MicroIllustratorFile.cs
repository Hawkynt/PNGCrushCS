using System;
using FileFormat.Core;

namespace FileFormat.MicroIllustrator;

/// <summary>In-memory representation of a Commodore 64 Micro Illustrator multicolor image.</summary>
public readonly record struct MicroIllustratorFile : IImageFormatReader<MicroIllustratorFile>, IImageToRawImage<MicroIllustratorFile>, IImageFormatWriter<MicroIllustratorFile> {

  static string IImageFormatMetadata<MicroIllustratorFile>.PrimaryExtension => ".mil";
  static string[] IImageFormatMetadata<MicroIllustratorFile>.FileExtensions => [".mil"];
  static MicroIllustratorFile IImageFormatReader<MicroIllustratorFile>.FromSpan(ReadOnlySpan<byte> data) => MicroIllustratorReader.FromSpan(data);
  static byte[] IImageFormatWriter<MicroIllustratorFile>.ToBytes(MicroIllustratorFile file) => MicroIllustratorWriter.ToBytes(file);

  /// <summary>The fixed width of a Micro Illustrator image in pixels.</summary>
  public const int FixedWidth = 160;

  /// <summary>The fixed height of a Micro Illustrator image in pixels.</summary>
  public const int FixedHeight = 200;

  /// <summary>The expected total file size in bytes (2 + 8000 + 1000 + 1000 + 1).</summary>
  public const int ExpectedFileSize = 10003;

  /// <summary>Size of the bitmap data section in bytes.</summary>
  internal const int BitmapDataSize = 8000;

  /// <summary>Size of the video matrix section in bytes.</summary>
  internal const int VideoMatrixSize = 1000;

  /// <summary>Size of the color RAM section in bytes.</summary>
  internal const int ColorRamSize = 1000;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Image width, always 160.</summary>
  public int Width => FixedWidth;

  /// <summary>Image height, always 200.</summary>
  public int Height => FixedHeight;

  /// <summary>C64 memory load address, typically 0x6000.</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>Multicolor bitmap data (8000 bytes, 2 bits per pixel).</summary>
  public byte[] BitmapData { get; init; }

  /// <summary>Video matrix / screen RAM (1000 bytes, upper/lower nybble = 2 colors per cell).</summary>
  public byte[] VideoMatrix { get; init; }

  /// <summary>Color RAM (1000 bytes, lower nybble = 3rd color per cell).</summary>
  public byte[] ColorRam { get; init; }

  /// <summary>Background color index (0-15).</summary>
  public byte BackgroundColor { get; init; }

  /// <summary>Converts this Micro Illustrator image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(MicroIllustratorFile file)
    => Commodore64Graphics.DecodeMulticolor(
      file.BitmapData, file.VideoMatrix, file.ColorRam, file.BackgroundColor, FixedWidth, FixedHeight);

}
