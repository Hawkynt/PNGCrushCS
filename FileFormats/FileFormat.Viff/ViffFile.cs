using System;
using FileFormat.Core;

namespace FileFormat.Viff;

/// <summary>In-memory representation of a VIFF (Khoros Visualization Image File Format) image.</summary>
public readonly record struct ViffFile : IImageFormatReader<ViffFile>, IImageToRawImage<ViffFile>, IImageFromRawImage<ViffFile>, IImageFormatWriter<ViffFile> {

  static string IImageFormatMetadata<ViffFile>.PrimaryExtension => ".viff";
  static string[] IImageFormatMetadata<ViffFile>.FileExtensions => [".viff", ".xv"];
  static ViffFile IImageFormatReader<ViffFile>.FromSpan(ReadOnlySpan<byte> data) => ViffReader.FromSpan(data);
  static byte[] IImageFormatWriter<ViffFile>.ToBytes(ViffFile file) => ViffWriter.ToBytes(file);

  static bool? IImageFormatMetadata<ViffFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 2 && header[0] == 0xAB && header[1] == 0x01
      ? true : null;

  /// <summary>Image width in pixels (RowSize).</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels (ColSize).</summary>
  public int Height { get; init; }

  /// <summary>Number of data bands (SubRowSize).</summary>
  public int Bands { get; init; }

  /// <summary>Pixel data storage type.</summary>
  public ViffStorageType StorageType { get; init; }

  /// <summary>Color space model.</summary>
  public ViffColorSpaceModel ColorSpaceModel { get; init; }

  /// <summary>512-byte ASCII comment from the header.</summary>
  public string Comment { get; init; }

  /// <summary>Raw pixel data bytes (band-interleaved).</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Optional color map data.</summary>
  public byte[]? MapData { get; init; }

  /// <summary>Color map type.</summary>
  public ViffMapType MapType { get; init; }

  /// <summary>Number of map rows.</summary>
  public int MapRowSize { get; init; }

  /// <summary>Number of map columns.</summary>
  public int MapColSize { get; init; }

  /// <summary>Map storage type.</summary>
  public ViffStorageType MapStorageType { get; init; }

  public static RawImage ToRawImage(ViffFile file) {

    if (file.StorageType != ViffStorageType.Byte)
      throw new ArgumentException($"Only Byte storage is supported for conversion, got {file.StorageType}.", nameof(file));

    if (file.Bands == 1)
      return new() {
        Width = file.Width,
        Height = file.Height,
        Format = PixelFormat.Gray8,
        PixelData = file.PixelData[..],
      };

    if (file.Bands == 3) {
      var pixelCount = file.Width * file.Height;
      var result = new byte[pixelCount * 3];
      for (var i = 0; i < pixelCount; ++i) {
        result[i * 3] = file.PixelData[i];
        result[i * 3 + 1] = file.PixelData[pixelCount + i];
        result[i * 3 + 2] = file.PixelData[pixelCount * 2 + i];
      }

      return new() {
        Width = file.Width,
        Height = file.Height,
        Format = PixelFormat.Rgb24,
        PixelData = result,
      };
    }

    throw new ArgumentException($"Unsupported band count for conversion: {file.Bands}", nameof(file));
  }

  public static ViffFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    switch (image.Format) {
      case PixelFormat.Gray8:
        return new() {
          Width = image.Width,
          Height = image.Height,
          Bands = 1,
          StorageType = ViffStorageType.Byte,
          ColorSpaceModel = ViffColorSpaceModel.None,
          PixelData = image.PixelData[..],
        };
      case PixelFormat.Rgb24: {
        var pixelCount = image.Width * image.Height;
        var bandSeq = new byte[pixelCount * 3];
        for (var i = 0; i < pixelCount; ++i) {
          bandSeq[i] = image.PixelData[i * 3];
          bandSeq[pixelCount + i] = image.PixelData[i * 3 + 1];
          bandSeq[pixelCount * 2 + i] = image.PixelData[i * 3 + 2];
        }

        return new() {
          Width = image.Width,
          Height = image.Height,
          Bands = 3,
          StorageType = ViffStorageType.Byte,
          ColorSpaceModel = ViffColorSpaceModel.Rgb,
          PixelData = bandSeq,
        };
      }
      default:
        throw new ArgumentException($"Unsupported pixel format for VIFF: {image.Format}", nameof(image));
    }
  }
}
