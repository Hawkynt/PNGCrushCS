using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;
using FileFormat.Ico;

namespace FileFormat.Cur;

/// <summary>In-memory representation of a CUR file.</summary>
[FormatMagicBytes([0x00, 0x00, 0x02, 0x00])]
[FormatMimeType("image/vnd.microsoft.icon", "image/x-win-bitmap")]
public sealed class CurFile : IImageFormatReader<CurFile>, IImageToRawImage<CurFile>, IImageFromRawImage<CurFile>, IImageFormatWriter<CurFile>, IMultiImageFileFormat<CurFile> {

  static string IImageFormatMetadata<CurFile>.PrimaryExtension => ".cur";
  static string[] IImageFormatMetadata<CurFile>.FileExtensions => [".cur"];
  static CurFile IImageFormatReader<CurFile>.FromSpan(ReadOnlySpan<byte> data) => CurReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<CurFile>.Capabilities => FormatCapability.HasDedicatedOptimizer | FormatCapability.MultiImage;

  /// <summary>Any size up to 256 a side, a cursor being an icon in all but two fields.</summary>
  static VideoMode[] IImageFormatMetadata<CurFile>.VideoModes => [
    new("Cursor", [(new IntegerRange(1, IcoDib.MaximumSide), new IntegerRange(1, IcoDib.MaximumSide))])
  ];
  static byte[] IImageFormatWriter<CurFile>.ToBytes(CurFile file) => CurWriter.ToBytes(file);
  public IReadOnlyList<CurImage> Images { get; init; } = [];

  /// <summary>
  /// Builds a one-entry cursor holding the picture, its hotspot at the top left.
  /// </summary>
  /// <remarks>
  /// A cursor is an icon whose two unused directory fields carry the point the pointer actually
  /// points at. A picture arriving from anywhere else says nothing about where that is, and the top
  /// left corner is both the common choice and the only one that cannot be wrong by accident —
  /// guessing the middle would put the hotspot somewhere the drawing may not even be opaque.
  /// </remarks>
  public static CurFile FromRawImage(RawImage image) {
    var entry = IcoDib.FromRawImage(image);
    return new() {
      Images = [
        new CurImage {
          Width = entry.Width,
          Height = entry.Height,
          BitsPerPixel = entry.BitsPerPixel,
          Format = entry.Format,
          Data = entry.Data,
          HotspotX = 0,
          HotspotY = 0,
        }
      ]
    };
  }

  /// <summary>Returns the number of cursor entries in this CUR file.</summary>
  public static int ImageCount(CurFile file) => file.Images.Count;

  /// <summary>Converts the cursor entry at the given index to a <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(CurFile file, int index) {
    ArgumentNullException.ThrowIfNull(file);
    if ((uint)index >= (uint)file.Images.Count)
      throw new ArgumentOutOfRangeException(nameof(index));

    var entry = file.Images[index];
    var icoImage = new IcoImage {
      Width = entry.Width,
      Height = entry.Height,
      BitsPerPixel = entry.BitsPerPixel,
      Format = entry.Format,
      Data = entry.Data
    };
    return IcoFile.ToRawImage(new IcoFile { Images = [icoImage] });
  }

  /// <summary>Converts the largest cursor image entry to a <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(CurFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.Images.Count == 0)
      throw new ArgumentException("CUR file contains no images.", nameof(file));

    var best = file.Images
      .OrderByDescending(i => i.Width * i.Height)
      .ThenByDescending(i => i.BitsPerPixel)
      .First();

    var icoImage = new IcoImage {
      Width = best.Width,
      Height = best.Height,
      BitsPerPixel = best.BitsPerPixel,
      Format = best.Format,
      Data = best.Data
    };

    var icoFile = new IcoFile { Images = [icoImage] };
    return IcoFile.ToRawImage(icoFile);
  }

}
