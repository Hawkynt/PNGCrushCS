using System;
using FileFormat.Core;

namespace FileFormat.FliEditor;

/// <summary>In-memory representation of a C64 FLI Editor multicolor image.</summary>
public readonly record struct FliEditorFile : IImageFormatReader<FliEditorFile>, IImageToRawImage<FliEditorFile>, IImageFormatWriter<FliEditorFile> {

  static string IImageFormatMetadata<FliEditorFile>.PrimaryExtension => ".fed";
  static string[] IImageFormatMetadata<FliEditorFile>.FileExtensions => [".fed"];
  static FliEditorFile IImageFormatReader<FliEditorFile>.FromSpan(ReadOnlySpan<byte> data) => FliEditorReader.FromSpan(data);
  static byte[] IImageFormatWriter<FliEditorFile>.ToBytes(FliEditorFile file) => FliEditorWriter.ToBytes(file);

  /// <summary>The fixed width of the image in pixels.</summary>
  public const int FixedWidth = 160;

  /// <summary>The fixed height of the image in pixels.</summary>
  public const int FixedHeight = 200;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Size of the bitmap data section in bytes.</summary>
  internal const int BitmapSize = 8000;

  /// <summary>Number of screen RAM banks (one per char row group for FLI).</summary>
  internal const int ScreenBankCount = 8;

  /// <summary>Size of each screen RAM bank in bytes.</summary>
  internal const int ScreenBankSize = 1000;

  /// <summary>Total size of all screen RAM banks.</summary>
  internal const int TotalScreenSize = ScreenBankCount * ScreenBankSize;

  /// <summary>Size of the color RAM section in bytes.</summary>
  internal const int ColorRamSize = 1000;

  /// <summary>Minimum payload size (bitmap + 8 screens + color).</summary>
  internal const int MinPayloadSize = BitmapSize + TotalScreenSize + ColorRamSize;

  /// <summary>Image width, always 160.</summary>
  public int Width => FixedWidth;

  /// <summary>Image height, always 200.</summary>
  public int Height => FixedHeight;

  /// <summary>C64 memory load address (2 bytes, little-endian).</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>Raw payload data (entire file content after load address).</summary>
  public byte[] RawData { get; init; }

  /// <summary>Converts this FLI Editor image to a platform-independent <see cref="RawImage"/> in Rgb24 format using FLI multicolor decode.</summary>
  public static RawImage ToRawImage(FliEditorFile file)
    => Commodore64Graphics.DecodeFliMulticolor(
      file.RawData, FixedWidth, FixedHeight,
      MinPayloadSize, BitmapSize, ScreenBankCount, ScreenBankSize, TotalScreenSize);

}
