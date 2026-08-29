using System;
using FileFormat.Core;

namespace FileFormat.ImnetImage;

/// <summary>In-memory representation of an IMNET document image (.imt).</summary>
/// <remarks>
/// A bilevel document raster carrying one CCITT Group 4 page behind a 22 byte header. Nothing
/// published describes the layout, so it was read out of XnView's own reader and then confirmed
/// against its converter: a generated file reported back the width, height and resolution that were
/// encoded, and the pixels came back unchanged.
/// <para/>
/// The header is mixed-endian. The signature and the four bytes after it are big-endian, everything
/// after that is little-endian, which is what the two different 32 bit stream readers XnView calls
/// here amount to. The width is stored as a count of bytes per line rather than pixels, so the image
/// is always a multiple of eight pixels wide, and the height comes before it.
/// <para/>
/// As with the other fax-derived formats here, <see cref="PixelData"/> uses a set bit for black.
/// </remarks>
public readonly record struct ImnetImageFile : IImageFormatReader<ImnetImageFile>, IImageToRawImage<ImnetImageFile>, IImageFromRawImage<ImnetImageFile>, IImageFormatWriter<ImnetImageFile> {

  static string IImageFormatMetadata<ImnetImageFile>.PrimaryExtension => ".imt";
  static string[] IImageFormatMetadata<ImnetImageFile>.FileExtensions => [".imt"];
  static ImnetImageFile IImageFormatReader<ImnetImageFile>.FromSpan(ReadOnlySpan<byte> data) => ImnetImageReader.FromSpan(data);
  static byte[] IImageFormatWriter<ImnetImageFile>.ToBytes(ImnetImageFile file) => ImnetImageWriter.ToBytes(file);

  /// <summary>Magic value at offset 0, a 32 bit big-endian 0x27433100, so the bytes 0x27 0x43 0x31 0x00.</summary>
  internal const uint Magic = 0x27433100;

  /// <summary>The header is a fixed 22 bytes; the coded page starts straight after it.</summary>
  internal const int HeaderSize = 22;

  /// <summary>Minimum valid file size.</summary>
  public const int MinFileSize = HeaderSize;

  /// <summary>Image width in pixels, eight times the bytes-per-line field at offset 12.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels, from the 32 bit field at offset 8.</summary>
  public int Height { get; init; }

  /// <summary>Scan resolution in dots per inch, from the 16 bit field at offset 16; it applies to both axes.</summary>
  public ushort Resolution { get; init; }

  /// <summary>Whether the coded bits are stored most significant first, from the 16 bit field at offset 18 being zero.</summary>
  public bool IsMostSignificantBitFirst { get; init; }

  /// <summary>1bpp pixel data, most significant bit leftmost, rows padded to a byte boundary, a set bit being black.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Converts this IMNET image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(ImnetImageFile file) {
    var bytesPerRow = (file.Width + 7) / 8;
    var rgb = new byte[file.Width * file.Height * 3];

    for (var y = 0; y < file.Height; ++y)
      for (var x = 0; x < file.Width; ++x) {
        var bit = (file.PixelData[y * bytesPerRow + (x >> 3)] >> (7 - (x & 7))) & 1;
        var offset = (y * file.Width + x) * 3;
        var color = bit == 1 ? (byte)0 : (byte)255;
        rgb[offset] = color;
        rgb[offset + 1] = color;
        rgb[offset + 2] = color;
      }

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  /// <summary>
  /// Creates an IMNET Group-4 page from any source image. The format stores width as whole bytes, so
  /// a non-byte-aligned source is padded with white pixels on the right to the next multiple of eight.
  /// </summary>
  public static ImnetImageFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width <= 0 || image.Height <= 0)
      throw new ArgumentException("IMNET dimensions must be positive.", nameof(image));

    var paddedWidth = checked((image.Width + 7) & ~7);
    var rgb = image.EnsureFormat(PixelFormat.Rgb24);
    var padded = new byte[checked(paddedWidth * image.Height * 3)];
    Array.Fill(padded, (byte)255);
    for (var y = 0; y < image.Height; ++y)
      rgb.PixelData.AsSpan(y * image.Width * 3, image.Width * 3).CopyTo(padded.AsSpan(y * paddedWidth * 3));

    var paddedRaw = new RawImage {
      Width = paddedWidth,
      Height = image.Height,
      Format = PixelFormat.Rgb24,
      PixelData = padded,
      ColorInfo = image.ColorInfo,
      Metadata = image.Metadata,
    };
    var mono = paddedRaw.EnsureIndexed(PixelFormat.Indexed1, [255, 255, 255, 0, 0, 0]);
    return new() {
      Width = paddedWidth,
      Height = image.Height,
      Resolution = 300,
      IsMostSignificantBitFirst = true,
      PixelData = mono.PixelData[..],
    };
  }

}