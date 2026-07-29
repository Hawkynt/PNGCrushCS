using System;
using FileFormat.Core;

namespace FileFormat.HandyScanner;

/// <summary>In-memory representation of a Handy Scanner 2000 POSTERING scan (.hs2).</summary>
/// <remarks>
/// A bare bitmap at one bit per pixel, 840 pixels across and as many rows as the file has room
/// for — a hand scanner produces whatever length the operator rolled it over, so the height is the
/// file size divided by the 105 bytes a row takes. A set bit is what the scanner saw as light.
/// </remarks>
public readonly record struct HandyScannerFile
  : IImageFormatReader<HandyScannerFile>, IImageToRawImage<HandyScannerFile>,
    IImageFromRawImage<HandyScannerFile>, IImageFormatWriter<HandyScannerFile> {

  /// <summary>Scan width; the hardware's carriage is a fixed size.</summary>
  public const int Width = 840;

  /// <summary>Bytes one row occupies.</summary>
  public const int BytesPerRow = Width / 8;

  static string IImageFormatMetadata<HandyScannerFile>.PrimaryExtension => ".hs2";
  static string[] IImageFormatMetadata<HandyScannerFile>.FileExtensions => [".hs2"];
  static HandyScannerFile IImageFormatReader<HandyScannerFile>.FromSpan(ReadOnlySpan<byte> data)
    => HandyScannerReader.FromSpan(data);
  static byte[] IImageFormatWriter<HandyScannerFile>.ToBytes(HandyScannerFile file)
    => HandyScannerWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<HandyScannerFile>.VideoModes => [
    new("Scan", [(Width, 0)], [2])
  ];

  /// <summary>The bitmap, one bit per pixel, most significant bit leftmost.</summary>
  public byte[] BitmapData { get; init; }

  /// <summary>Rows the scan holds.</summary>
  public int Height => (this.BitmapData?.Length ?? 0) / BytesPerRow;

  public static RawImage ToRawImage(HandyScannerFile file)
    => MonochromePage.Decode(file.BitmapData ?? [], Width, file.Height, inkIsWhite: true);

  public static HandyScannerFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width != Width)
      throw new ArgumentException($"A scan is {Width} pixels wide, got {image.Width}.", nameof(image));
    if (image.Height < 1)
      throw new ArgumentException("A scan has at least one row.", nameof(image));

    return new() { BitmapData = MonochromePage.Encode(image, Width, image.Height, inkIsWhite: true) };
  }
}
