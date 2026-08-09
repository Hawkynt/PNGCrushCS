using System;
using FileFormat.Core;

namespace FileFormat.CImage;

/// <summary>In-memory representation of a CImage document image (.dsi).</summary>
/// <remarks>
/// A bilevel document raster with a fixed 164 byte header. Nothing published describes the layout,
/// so it was read out of XnView's own reader and then confirmed against its converter: a generated
/// file reported back the width, height and resolutions that were encoded, and the pixels came back
/// unchanged for both codings.
/// <para/>
/// The reader only ever looks at four places in that header — it seeks to each of them in turn and
/// never reads what lies between — so the fields listed below are all that can be recovered; the
/// rest of the header is preserved here only as raw bytes.
/// <para/>
/// On disk a set bit is white; <see cref="PixelData"/> holds the complement, a set bit being black,
/// matching the other fax-derived formats here.
/// </remarks>
public readonly record struct CImageFile : IImageFormatReader<CImageFile>, IImageToRawImage<CImageFile> {

  static string IImageFormatMetadata<CImageFile>.PrimaryExtension => ".dsi";
  static string[] IImageFormatMetadata<CImageFile>.FileExtensions => [".dsi"];
  static CImageFile IImageFormatReader<CImageFile>.FromSpan(ReadOnlySpan<byte> data) => CImageReader.FromSpan(data);

  /// <summary>Magic bytes at offset 0: "DI" (0x44 0x49).</summary>
  internal static readonly byte[] Magic = [0x44, 0x49];

  /// <summary>The header is a fixed 164 bytes; the page data starts straight after it.</summary>
  internal const int HeaderSize = 0xA4;

  /// <summary>Offset of the horizontal resolution field.</summary>
  internal const int HorizontalResolutionOffset = 0x28;

  /// <summary>Offset of the vertical resolution field.</summary>
  internal const int VerticalResolutionOffset = 0x2A;

  /// <summary>Offset of the compression byte.</summary>
  internal const int CompressionOffset = 0x84;

  /// <summary>Offset of the 32 bit width field; the height follows it.</summary>
  internal const int WidthOffset = 0x86;

  /// <summary>Minimum valid file size.</summary>
  public const int MinFileSize = HeaderSize;

  /// <summary>Image width in pixels, from the 32 bit field at offset 0x86.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels, from the 32 bit field at offset 0x8A.</summary>
  public int Height { get; init; }

  /// <summary>Whether the page is CCITT Group 4 coded, from the byte at offset 0x84 being non-zero.</summary>
  public bool IsGroup4 { get; init; }

  /// <summary>Horizontal resolution in dots per inch, from the 16 bit field at offset 0x28.</summary>
  public ushort HorizontalResolution { get; init; }

  /// <summary>Vertical resolution in dots per inch, from the 16 bit field at offset 0x2A.</summary>
  public ushort VerticalResolution { get; init; }

  /// <summary>1bpp pixel data, most significant bit leftmost, rows padded to a byte boundary, a set bit being black.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Converts this CImage image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(CImageFile file) {
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

}
