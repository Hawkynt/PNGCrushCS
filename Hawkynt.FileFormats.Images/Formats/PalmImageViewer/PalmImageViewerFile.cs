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
public readonly record struct PalmImageViewerFile : IImageFormatReader<PalmImageViewerFile>, IImageToRawImage<PalmImageViewerFile> {

  static string IImageFormatMetadata<PalmImageViewerFile>.PrimaryExtension => ".pdb";
  static string[] IImageFormatMetadata<PalmImageViewerFile>.FileExtensions => [".pdb"];
  static PalmImageViewerFile IImageFormatReader<PalmImageViewerFile>.FromSpan(ReadOnlySpan<byte> data) => PalmImageViewerReader.FromSpan(data);
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
