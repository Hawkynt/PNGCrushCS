using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Gem;

/// <summary>Serialises GEM metafiles and builds a portable VDI recording from a raster.</summary>
/// <remarks>
/// GEM records are signed sixteen-bit little-endian words. The standard record stores only the VDI
/// opcode, point count, integer count and sub-opcode followed by PTSIN and INTIN; the terminating
/// <c>-1</c> is not itself a record.
/// <para/>
/// A raster is represented as solid horizontal bars after mapping it to the VDI workstation's
/// standard sixteen-colour palette. This deliberately avoids <c>v_cellarray</c>: that VDI call keeps
/// row length, used elements, row count and writing mode in extra CONTRL words which the metafile
/// record format does not carry. The other historic raster path, <c>v_bit_image</c>, names a separate
/// IMG file and therefore cannot be emitted by a one-file image writer.
/// </remarks>
public static class GemWriter {

  private const int _Version = 101;

  /// <summary>Writes a GEM metafile using the standard 24-word header.</summary>
  public static byte[] ToBytes(GemFile file) {
    var records = file.Records ?? throw new ArgumentException("A GEM metafile with no records cannot be written.", nameof(file));

    using var output = new MemoryStream();
    using var writer = new BinaryWriter(output);

    writer.Write(GemFile.Magic);
    writer.Write((short)GemFile.StandardHeaderWords);
    _Word(writer, file.Version, nameof(file.Version));
    _Word(writer, file.CoordinateFlag, nameof(file.CoordinateFlag));
    _Word(writer, file.Extent.X1, "extent x1");
    _Word(writer, file.Extent.Y1, "extent y1");
    _Word(writer, file.Extent.X2, "extent x2");
    _Word(writer, file.Extent.Y2, "extent y2");
    _Word(writer, file.PageSize.Width, "page width");
    _Word(writer, file.PageSize.Height, "page height");
    _Word(writer, file.Window.X1, "window x1");
    _Word(writer, file.Window.Y1, "window y1");
    _Word(writer, file.Window.X2, "window x2");
    _Word(writer, file.Window.Y2, "window y2");
    writer.Write((short)(file.HasBitImage ? 1 : 0));
    for (var i = 15; i < GemFile.StandardHeaderWords; ++i)
      writer.Write((short)0);

    foreach (var record in records)
      _Record(writer, record);

    writer.Write(GemFile.Magic);
    return output.ToArray();
  }

  /// <summary>Creates a self-contained raster-coordinate metafile from an image.</summary>
  public static GemFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width < 1 || image.Height < 1)
      throw new ArgumentOutOfRangeException(nameof(image), $"A GEM metafile picture of {image.Width} by {image.Height} has nothing in it.");

    if (image.Width > short.MaxValue || image.Height > short.MaxValue)
      throw new ArgumentOutOfRangeException(nameof(image),
        $"GEM metafile coordinates are signed sixteen-bit words, and {image.Width} by {image.Height} does not fit in them.");

    var width = image.Width;
    var height = image.Height;
    var rgb = PixelConverter.Convert(image, PixelFormat.Rgb24);
    var opaque = PixelConverter.Convert(rgb, PixelFormat.Bgra32);
    var quantized = ColorQuantizer.MapToPalette(opaque.PixelData, width * height, _Palette());
    var records = new System.Collections.Generic.List<GemRecord> {
      new(GemOpcode.SetFillInterior, 0, [], [(short)GemAttributes.InteriorSolid]),
      new(GemOpcode.SetFillPerimeter, 0, [], [0])
    };

    var currentPen = -1;
    for (var y = 0; y < height; ++y) {
      var row = y * width;
      for (var x = 0; x < width;) {
        var pen = quantized.Indices[row + x];
        var end = x + 1;
        while (end < width && quantized.Indices[row + end] == pen)
          ++end;

        if (pen != currentPen) {
          records.Add(new(GemOpcode.SetFillColour, 0, [], [(short)pen]));
          currentPen = pen;
        }

        // Treat the raster as a coordinate grid: each pixel occupies one unit square. That makes
        // adjacent runs share an edge without leaving a gap, and the extent is exactly width×height.
        records.Add(new(
          GemOpcode.GeneralisedPrimitive,
          GemPrimitive.Bar,
          [(short)x, (short)y, (short)end, (short)(y + 1)],
          []
        ));

        x = end;
      }
    }

    return new() {
      Version = _Version,
      CoordinateFlag = GemFile.RasterCoordinates,
      Extent = (0, 0, width, height),
      PageSize = (0, 0),
      // In raster coordinates the origin is the upper-left corner, so the header's lower-left
      // point has the greater y and its upper-right point has y zero.
      Window = (0, height, width, 0),
      HasBitImage = false,
      Records = records
    };
  }

  private static void _Record(BinaryWriter writer, GemRecord record) {
    var points = record.Points ?? throw new ArgumentException("A GEM record has no point array.", nameof(record));
    var integers = record.Integers ?? throw new ArgumentException("A GEM record has no integer array.", nameof(record));
    if ((points.Length & 1) != 0)
      throw new ArgumentException($"A GEM record has {points.Length} coordinate words instead of complete x/y pairs.", nameof(record));

    var pointCount = points.Length / 2;
    if (pointCount > short.MaxValue || integers.Length > short.MaxValue)
      throw new ArgumentException("A GEM record count does not fit in its signed sixteen-bit word.", nameof(record));

    _Word(writer, record.Opcode, "record opcode");
    writer.Write((short)pointCount);
    writer.Write((short)integers.Length);
    _Word(writer, record.SubOpcode, "record sub-opcode");

    foreach (var point in points)
      writer.Write(point);
    foreach (var integer in integers)
      writer.Write(integer);
  }

  private static void _Word(BinaryWriter writer, int value, string name) {
    if (value is < short.MinValue or > short.MaxValue)
      throw new ArgumentOutOfRangeException(name, value, "A GEM metafile word is a signed sixteen-bit value.");

    writer.Write((short)value);
  }

  private static byte[] _Palette() {
    var colors = GemAttributes.Palette;
    var result = new byte[colors.Length * 3];
    for (var i = 0; i < colors.Length; ++i) {
      result[i * 3] = colors[i].R;
      result[i * 3 + 1] = colors[i].G;
      result[i * 3 + 2] = colors[i].B;
    }

    return result;
  }
}
