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
public readonly record struct CImageFile : IImageFormatReader<CImageFile>, IImageToRawImage<CImageFile>, IImageFromRawImage<CImageFile>, IImageFormatWriter<CImageFile> {

  static string IImageFormatMetadata<CImageFile>.PrimaryExtension => ".dsi";
  static string[] IImageFormatMetadata<CImageFile>.FileExtensions => [".dsi"];
  static CImageFile IImageFormatReader<CImageFile>.FromSpan(ReadOnlySpan<byte> data) => CImageReader.FromSpan(data);
  static byte[] IImageFormatWriter<CImageFile>.ToBytes(CImageFile file) => CImageWriter.ToBytes(file);

  internal static readonly byte[] Magic = [0x44, 0x49];
  internal const int HeaderSize = 0xA4;
  internal const int HorizontalResolutionOffset = 0x28;
  internal const int VerticalResolutionOffset = 0x2A;
  internal const int CompressionOffset = 0x84;
  internal const int WidthOffset = 0x86;
  public const int MinFileSize = HeaderSize;

  public int Width { get; init; }
  public int Height { get; init; }
  public bool IsGroup4 { get; init; }
  public ushort HorizontalResolution { get; init; }
  public ushort VerticalResolution { get; init; }

  /// <summary>1bpp pixel data, most significant bit leftmost, rows padded to a byte boundary, a set bit being black.</summary>
  public byte[] PixelData { get; init; }

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

    return new() { Width = file.Width, Height = file.Height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  /// <summary>Creates a Group-4-compressed bilevel CImage page from any source picture.</summary>
  public static CImageFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width <= 0 || image.Height <= 0)
      throw new ArgumentException("CImage dimensions must be positive.", nameof(image));

    var indices = BilevelRows.Threshold(image, setWhenDark: true);
    return new() {
      Width = image.Width,
      Height = image.Height,
      IsGroup4 = true,
      HorizontalResolution = 300,
      VerticalResolution = 300,
      PixelData = BilevelRows.Pack(indices, image.Width, image.Height),
    };
  }
}
