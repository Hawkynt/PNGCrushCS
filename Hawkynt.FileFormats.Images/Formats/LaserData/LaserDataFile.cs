using System;
using FileFormat.Core;

namespace FileFormat.LaserData;

/// <summary>The coding used for the scan lines that follow a LaserData header.</summary>
public enum LaserDataCompression : byte {

  /// <summary>Packed 1bpp scan lines, no coding.</summary>
  Uncompressed = 0,

  /// <summary>CCITT Group 3 one-dimensional (T.4), one EOL in front of every line.</summary>
  Group3 = 2,

  /// <summary>CCITT Group 4 (T.6).</summary>
  Group4 = 5,

}

/// <summary>In-memory representation of a LaserData document image (.lda).</summary>
/// <remarks>
/// A bilevel document raster with a fixed 512 byte header, of which only six fields carry meaning.
/// Nothing published describes the layout, so it was read out of XnView's own reader and then
/// confirmed field by field against its converter: every field below was set to a distinct value in
/// a generated file, and the width, height, resolution and compression the converter reported were
/// compared with what had been encoded. The bytes the converter handed back for all three codings
/// matched the pixels that went in.
/// <para/>
/// The header carries thirteen more 16 bit fields that the reader loads and never looks at, so their
/// meaning cannot be recovered from it; they are preserved here only as the raw header bytes.
/// <para/>
/// On disk a set bit is white, which is the opposite of the sense used elsewhere in this library;
/// <see cref="PixelData"/> therefore holds the complement, a set bit being black, matching the other
/// fax-derived formats here.
/// </remarks>
public readonly record struct LaserDataFile : IImageFormatReader<LaserDataFile>, IImageToRawImage<LaserDataFile>, IImageFromRawImage<LaserDataFile>, IImageFormatWriter<LaserDataFile> {

  static string IImageFormatMetadata<LaserDataFile>.PrimaryExtension => ".lda";
  static string[] IImageFormatMetadata<LaserDataFile>.FileExtensions => [".lda"];
  static LaserDataFile IImageFormatReader<LaserDataFile>.FromSpan(ReadOnlySpan<byte> data) => LaserDataReader.FromSpan(data);
  static byte[] IImageFormatWriter<LaserDataFile>.ToBytes(LaserDataFile file) => LaserDataWriter.ToBytes(file);

  /// <summary>Magic value at offset 0, a 16 bit little-endian 0xDCDC, so the bytes 0xDC 0xDC.</summary>
  internal const ushort Magic = 0xDCDC;

  /// <summary>The header is a fixed 512 bytes; the scan lines start straight after it.</summary>
  internal const int HeaderSize = 512;

  /// <summary>Minimum valid file size.</summary>
  public const int MinFileSize = HeaderSize;

  /// <summary>Image width in pixels, from the 16 bit field at offset 8.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels, from the 16 bit field at offset 6.</summary>
  public int Height { get; init; }

  /// <summary>How the scan lines are coded, from the byte at offset 12.</summary>
  public LaserDataCompression Compression { get; init; }

  /// <summary>Whether the coded bits are stored most significant first, from the byte at offset 13 being non-zero.</summary>
  public bool IsMostSignificantBitFirst { get; init; }

  /// <summary>Horizontal resolution in dots per inch, from the 16 bit field at offset 18.</summary>
  public ushort HorizontalResolution { get; init; }

  /// <summary>Vertical resolution in dots per inch, from the 16 bit field at offset 16.</summary>
  public ushort VerticalResolution { get; init; }

  /// <summary>1bpp pixel data, most significant bit leftmost, rows padded to a byte boundary, a set bit being black.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Converts this LaserData image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(LaserDataFile file) {
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

  /// <summary>Creates a Group-4-compressed LaserData page from any source image.</summary>
  public static LaserDataFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width is < 1 or > ushort.MaxValue || image.Height is < 1 or > ushort.MaxValue)
      throw new ArgumentException($"LaserData dimensions must fit 16-bit fields; got {image.Width}x{image.Height}.", nameof(image));

    var mono = image.EnsureIndexed(PixelFormat.Indexed1, [255, 255, 255, 0, 0, 0]);
    return new() {
      Width = mono.Width,
      Height = mono.Height,
      Compression = LaserDataCompression.Group4,
      IsMostSignificantBitFirst = true,
      HorizontalResolution = 300,
      VerticalResolution = 300,
      PixelData = mono.PixelData[..],
    };
  }

}