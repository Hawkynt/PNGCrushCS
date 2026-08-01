using System;
using FileFormat.Core;

namespace FileFormat.PalmPdb;

/// <summary>In-memory representation of a Palm Image Viewer picture, carried in a PDB database.</summary>
/// <remarks>
/// A PDB is only a container — a Palm database of records — and what makes one an image is the type
/// it declares at offset 60. That type is <c>vIMG</c>, with creator <c>View</c>, and the record
/// inside opens with a 58-byte descriptor of its own before any pixels.
///
/// This was written against a format that does not exist: type <c>Img&#32;</c>, and a record holding
/// nothing but a width, a height and RGB triples. Nothing that produced a PDB could be read, and
/// nothing that reads one could use what this produced.
/// </remarks>
[FormatMagicBytes([0x76, 0x49, 0x4D, 0x47], offset: 60)]
public readonly record struct PalmPdbFile : IImageFormatReader<PalmPdbFile>, IImageToRawImage<PalmPdbFile>, IImageFromRawImage<PalmPdbFile>, IImageFormatWriter<PalmPdbFile> {

  static string IImageFormatMetadata<PalmPdbFile>.PrimaryExtension => ".pdb";
  static string[] IImageFormatMetadata<PalmPdbFile>.FileExtensions => [".pdb"];
  static PalmPdbFile IImageFormatReader<PalmPdbFile>.FromSpan(ReadOnlySpan<byte> data) => PalmPdbReader.FromSpan(data);
  static byte[] IImageFormatWriter<PalmPdbFile>.ToBytes(PalmPdbFile file) => PalmPdbWriter.ToBytes(file);

  /// <summary>Image width in pixels, always a multiple of sixteen.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Database name (up to 31 characters, null-terminated in file).</summary>
  public string Name { get; init; }

  /// <summary>Two-bits-a-pixel indices, MSB first, ceil(width/4) bytes per row.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>The four greys a Palm shows, lightest first: index 0 is white and index 3 is black.</summary>
  private static readonly byte[] _GrayPalette = [255, 255, 255, 170, 170, 170, 85, 85, 85, 0, 0, 0];

  /// <summary>The Image Viewer stores whole tiles, so a picture is widened to a multiple of this.</summary>
  internal const int WidthMultiple = 16;

  public static RawImage ToRawImage(PalmPdbFile file) {
    var stride = (file.Width + 3) / 4;
    var indices = new byte[file.Width * file.Height];
    for (var y = 0; y < file.Height; ++y)
      for (var x = 0; x < file.Width; ++x) {
        var at = (y * stride) + (x >> 2);
        indices[(y * file.Width) + x] = at < file.PixelData.Length
          ? (byte)((file.PixelData[at] >> (6 - ((x & 3) * 2))) & 3)
          : (byte)0;
      }

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = indices,
      Palette = _GrayPalette[..],
      PaletteCount = 4,
    };
  }

  public static PalmPdbFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    // Widths are rounded up to a whole tile, and the columns that adds are left white.
    var width = ((image.Width + WidthMultiple - 1) / WidthMultiple) * WidthMultiple;
    var stride = (width + 3) / 4;
    var rgb = image.ToRgb24();
    var packed = new byte[stride * image.Height];

    for (var y = 0; y < image.Height; ++y)
      for (var x = 0; x < image.Width; ++x) {
        var at = ((y * image.Width) + x) * 3;
        var grey = ((rgb[at] * 77) + (rgb[at + 1] * 151) + (rgb[at + 2] * 28)) >> 8;

        // Index 0 is white, so the darker the pixel the higher the index.
        var index = (255 - grey) * 3 / 255;
        packed[(y * stride) + (x >> 2)] |= (byte)(index << (6 - ((x & 3) * 2)));
      }

    return new() {
      Width = width,
      Height = image.Height,
      Name = string.Empty,
      PixelData = packed,
    };
  }
}
