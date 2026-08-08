using System;
using FileFormat.Core;

namespace FileFormat.Ffli;

/// <summary>In-memory representation of a C64 Full FLI multicolor image.</summary>
public readonly record struct FfliFile
  : IImageFormatReader<FfliFile>, IImageToRawImage<FfliFile>,
    IImageFromRawImage<FfliFile>, IImageFormatWriter<FfliFile> {

  static string IImageFormatMetadata<FfliFile>.PrimaryExtension => ".ffli";
  static string[] IImageFormatMetadata<FfliFile>.FileExtensions => [".ffli", ".ffl"];
  static FfliFile IImageFormatReader<FfliFile>.FromSpan(ReadOnlySpan<byte> data) => FfliReader.FromSpan(data);
  static byte[] IImageFormatWriter<FfliFile>.ToBytes(FfliFile file) => FfliWriter.ToBytes(file);

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

  /// <summary>Converts this FFLI image to a platform-independent <see cref="RawImage"/> in Rgb24 format using FLI multicolor decode.</summary>
  public static RawImage ToRawImage(FfliFile file)
    => Commodore64Graphics.DecodeFliMulticolor(
      file.RawData, FixedWidth, FixedHeight,
      MinPayloadSize, BitmapSize, ScreenBankCount, ScreenBankSize, TotalScreenSize);

  /// <summary>Default load address, the one the format's own display routine expects.</summary>
  internal const ushort DefaultLoadAddress = 0x4000;

  /// <summary>Encodes a picture as Full FLI, scaling it to 160x200 first.</summary>
  /// <remarks>
  /// The inverse of <see cref="ToRawImage"/>, laid out the way it reads: the bitmap, then the eight
  /// video matrices, then colour memory. Pattern 00 is encoded as black because the file has no
  /// register to say otherwise and the decoder resolves it that way.
  /// </remarks>
  public static FfliFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(FixedWidth, FixedHeight).PixelData;
    var raw = new byte[MinPayloadSize];
    Commodore64Graphics.EncodeMulticolorFli(
      rgb, FixedWidth, FixedHeight, 0,
      raw.AsSpan(0, BitmapSize),
      raw.AsSpan(BitmapSize, TotalScreenSize), ScreenBankSize,
      raw.AsSpan(BitmapSize + TotalScreenSize, ColorRamSize));

    return new() { LoadAddress = DefaultLoadAddress, RawData = raw };
  }

}
