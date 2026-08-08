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

  /// <summary>Pixels the stored width is always a whole number of.</summary>
  public const int WidthGranularity = 16;

  /// <summary>The depth written, which is the deepest of the three the format has.</summary>
  public const int WrittenBitsPerPixel = 4;

  static string IImageFormatMetadata<PalmImageViewerFile>.PrimaryExtension => ".pdb";
  static string[] IImageFormatMetadata<PalmImageViewerFile>.FileExtensions => [".pdb"];
  static PalmImageViewerFile IImageFormatReader<PalmImageViewerFile>.FromSpan(ReadOnlySpan<byte> data) => PalmImageViewerReader.FromSpan(data);
  static byte[] IImageFormatWriter<PalmImageViewerFile>.ToBytes(PalmImageViewerFile file) => PalmImageViewerWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<PalmImageViewerFile>.VideoModes => [
    new("Greyscale", [
      (new IntegerRange(WidthGranularity, MaximumExtent, WidthGranularity), new IntegerRange(1, MaximumExtent))
    ], [2, 4, 16])
  ];

  /// <summary>Image width in pixels, which is always a whole number of sixteen.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>One, two or four, which the record's own depth byte names.</summary>
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

  /// <summary>Reduces a picture to the sixteen greys the deepest of the three forms holds.</summary>
  /// <remarks>
  /// The width is a whole number of sixteen pixels and nothing else — the program rounded it up and
  /// stored the rounded number, so a picture 100 across is a picture 112 across as far as the record
  /// is concerned, and every reader shows it that way. A width that is not a multiple of sixteen is
  /// therefore sampled to one that is rather than refused.
  /// </remarks>
  public static PalmImageViewerFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var width = Math.Max((image.Width + WidthGranularity - 1) / WidthGranularity * WidthGranularity, WidthGranularity);
    var height = Math.Max(image.Height, 1);
    if (width > MaximumExtent || height > MaximumExtent)
      throw new ArgumentException(
        $"An ImageViewer record states its size in sixteen bits, so {width}x{height} cannot be written.",
        nameof(image));

    var source = image.Width == width && image.Height == height ? image : image.SampleTo(width, height);
    var entries = 1 << WrittenBitsPerPixel;
    var indexed = source.EnsureIndexed(PixelFormat.Indexed8, _GrayRamp(entries));

    return new() {
      Width = width,
      Height = height,
      BitsPerPixel = WrittenBitsPerPixel,
      Name = string.Empty,
      PixelData = PackedRows.Pack(indexed.PixelData, width, height, WrittenBitsPerPixel),
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
