using System;
using FileFormat.Core;

namespace FileFormat.HiresManager;

/// <summary>In-memory representation of a C64 Hires Manager by Cosmos (.him) image.</summary>
public readonly record struct HiresManagerFile : IImageFormatReader<HiresManagerFile>, IImageToRawImage<HiresManagerFile>, IImageFromRawImage<HiresManagerFile>, IImageFormatWriter<HiresManagerFile> {

  static string IImageFormatMetadata<HiresManagerFile>.PrimaryExtension => ".him";
  static string[] IImageFormatMetadata<HiresManagerFile>.FileExtensions => [".him"];
  static HiresManagerFile IImageFormatReader<HiresManagerFile>.FromSpan(ReadOnlySpan<byte> data) => HiresManagerReader.FromSpan(data);
  static byte[] IImageFormatWriter<HiresManagerFile>.ToBytes(HiresManagerFile file) => HiresManagerWriter.ToBytes(file);

  /// <summary>The fixed width of the image in pixels.</summary>
  public const int FixedWidth = 320;

  /// <summary>The fixed height of the image in pixels.</summary>
  public const int FixedHeight = 200;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Size of the bitmap data section in bytes.</summary>
  internal const int BitmapDataSize = 8000;

  /// <summary>Size of the screen RAM section in bytes.</summary>
  internal const int ScreenRamSize = 1000;

  /// <summary>Minimum payload size in bytes (bitmap + screen).</summary>
  internal const int MinPayloadSize = BitmapDataSize + ScreenRamSize;

  /// <summary>Image width, always 320.</summary>
  public int Width => FixedWidth;

  /// <summary>Image height, always 200.</summary>
  public int Height => FixedHeight;

  /// <summary>C64 memory load address (2 bytes, little-endian).</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>Raw payload data (entire file content after load address).</summary>
  public byte[] RawData { get; init; }

  /// <summary>Converts this Hires Manager image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(HiresManagerFile file) {

    const int width = FixedWidth;
    const int height = FixedHeight;
    var rgb = new byte[width * height * 3];

    var hasScreen = file.RawData.Length >= BitmapDataSize + ScreenRamSize;

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var cellX = x / 8;
        var cellY = y / 8;
        var cellIndex = cellY * 40 + cellX;
        var byteInCell = y % 8;
        var bitmapOffset = cellIndex * 8 + byteInCell;
        var bitmapByte = bitmapOffset < file.RawData.Length ? file.RawData[bitmapOffset] : (byte)0;
        var bitPosition = 7 - (x % 8);
        var bitValue = (bitmapByte >> bitPosition) & 1;

        int colorIndex;
        if (hasScreen) {
          var screenByte = file.RawData[BitmapDataSize + cellIndex];
          colorIndex = bitValue == 1
            ? (screenByte >> 4) & 0x0F
            : screenByte & 0x0F;
        } else
          colorIndex = bitValue == 1 ? 1 : 0;

        var color = Commodore64Graphics.HexColors[colorIndex];
        var offset = (y * width + x) * 3;
        rgb[offset] = (byte)((color >> 16) & 0xFF);
        rgb[offset + 1] = (byte)((color >> 8) & 0xFF);
        rgb[offset + 2] = (byte)(color & 0xFF);
      }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  /// <summary>Builds a Hires Manager screen, choosing two of the machine's colours per character cell.</summary>
  /// <remarks>
  /// The screen is 320 by 200 and nothing in the file says otherwise, so a picture of another size
  /// is brought to that one. The payload is the bitmap followed by the video matrix, which is what
  /// a real one holds behind its <c>$4000</c> load address.
  /// </remarks>
  public static HiresManagerFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(FixedWidth, FixedHeight);
    var payload = new byte[MinPayloadSize];
    Commodore64Graphics.EncodeHires(
      rgb.PixelData, FixedWidth, FixedHeight,
      payload.AsSpan(0, BitmapDataSize), payload.AsSpan(BitmapDataSize, ScreenRamSize));

    return new() { LoadAddress = 0x4000, RawData = payload };
  }

}
