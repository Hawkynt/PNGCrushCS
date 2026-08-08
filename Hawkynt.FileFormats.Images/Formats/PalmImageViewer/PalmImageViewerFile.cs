using System;
using FileFormat.Core;

namespace FileFormat.PalmImageViewer;

/// <summary>In-memory representation of a Palm ImageViewer picture (.pdb).</summary>
/// <remarks>
/// A Palm database can hold anything, and .pdb says only that — the type and creator inside say what.
/// This is the one written by ImageViewer, type <c>vIMG</c> and creator <c>View</c>, which stores the
/// picture as a single record. It is a different format from the other .pdb pictures here, which is
/// why it is a format of its own rather than a branch inside one.
/// </remarks>
public readonly record struct PalmImageViewerFile
  : IImageFormatReader<PalmImageViewerFile>, IImageToRawImage<PalmImageViewerFile>,
    IImageFromRawImage<PalmImageViewerFile>, IImageFormatWriter<PalmImageViewerFile> {

  /// <summary>The widest and tallest picture the record's sixteen-bit fields can state.</summary>
  public const int MaximumExtent = 65535;

  static string IImageFormatMetadata<PalmImageViewerFile>.PrimaryExtension => ".pdb";
  static string[] IImageFormatMetadata<PalmImageViewerFile>.FileExtensions => [".pdb"];
  static PalmImageViewerFile IImageFormatReader<PalmImageViewerFile>.FromSpan(ReadOnlySpan<byte> data) => PalmImageViewerReader.FromSpan(data);
  static byte[] IImageFormatWriter<PalmImageViewerFile>.ToBytes(PalmImageViewerFile file) => PalmImageViewerWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<PalmImageViewerFile>.VideoModes => [
    new("Greyscale", [(IntegerRange.Any, IntegerRange.Any)], [2, 4, 16])
  ];

  /// <summary>Image width in pixels; the format rounds it up to a multiple of sixteen.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>One, two or four, worked out from how much the record decompresses to.</summary>
  public int BitsPerPixel { get; init; }

  /// <summary>The record's own name, which is usually the file it was made from.</summary>
  public string Name { get; init; }

  /// <summary>The decompressed rows, packed at <see cref="BitsPerPixel"/> and padded to a byte.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(PalmImageViewerFile file) {
    var entries = 1 << file.BitsPerPixel;

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = PackedRows.Unpack(file.PixelData, file.Width, file.Height, file.BitsPerPixel),
      Palette = _GrayRamp(entries),
      PaletteCount = entries,
    };
  }

  /// <summary>
  /// Reduces a picture to the deepest grey ramp the record's own arithmetic can be read back at.
  /// </summary>
  /// <remarks>
  /// Nothing states the depth: the reader divides the row length by the width to find it, so a depth
  /// is only usable when that division comes back with the number it was written at. Four bits
  /// always does for a picture wider than four pixels; below that a shallower one is tried rather
  /// than the picture refused. One pixel across is the single width no depth survives, a row being a
  /// whole byte whatever is in it.
  /// </remarks>
  public static PalmImageViewerFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width > MaximumExtent || image.Height > MaximumExtent)
      throw new ArgumentException(
        $"An ImageViewer record states its size in sixteen bits, so {image.Width}x{image.Height} cannot be written.",
        nameof(image));

    var bits = 0;
    foreach (var candidate in (int[])[4, 2, 1])
      if (PackedRows.Stride(image.Width, candidate) * 8 / image.Width == candidate) {
        bits = candidate;
        break;
      }

    if (bits == 0)
      throw new ArgumentException(
        $"A picture {image.Width} pixels across leaves no depth an ImageViewer record can be read back at.",
        nameof(image));

    var entries = 1 << bits;
    var indexed = image.EnsureIndexed(PixelFormat.Indexed8, _GrayRamp(entries));

    return new() {
      Width = image.Width,
      Height = image.Height,
      BitsPerPixel = bits,
      Name = string.Empty,
      PixelData = PackedRows.Pack(indexed.PixelData, image.Width, image.Height, bits),
    };
  }

  /// <summary>The ramp runs from white at zero into black, as everything on that machine did.</summary>
  private static byte[] _GrayRamp(int entries) {
    var palette = new byte[entries * 3];
    for (var i = 0; i < entries; ++i) {
      var value = (byte)((entries - 1 - i) * 255 / (entries - 1));
      palette[i * 3] = palette[i * 3 + 1] = palette[i * 3 + 2] = value;
    }

    return palette;
  }
}
