using System;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.Sdg;

/// <summary>A StarOffice/LibreOffice Gallery data file represented by the thumbnails its SGA objects carry.</summary>
/// <remarks>
/// An SDG is one part of a gallery theme (beside THM and SDV), not a standalone raster image. The
/// image contract therefore exposes its embedded thumbnails and deliberately has no raster writer.
/// </remarks>
[FormatMagicBytes([(byte)'S', (byte)'G', (byte)'A', (byte)'3'])]
public sealed class SdgFile : IImageFormatReader<SdgFile>, IImageToRawImage<SdgFile>, IMultiImageFileFormat<SdgFile> {

  static string IImageFormatMetadata<SdgFile>.PrimaryExtension => ".sdg";
  static string[] IImageFormatMetadata<SdgFile>.FileExtensions => [".sdg"];
  static SdgFile IImageFormatReader<SdgFile>.FromSpan(ReadOnlySpan<byte> data) => SdgReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<SdgFile>.VideoModes => [
    new("Gallery thumbnail", [(IntegerRange.Any, IntegerRange.Any)])
  ];

  public List<RawImage> Images { get; init; } = [];

  public static RawImage ToRawImage(SdgFile file)
    => file.Images.Count == 0 ? throw new InvalidOperationException("The SDG file has no bitmap thumbnail.") : file.Images[0];

  public static int ImageCount(SdgFile file) => file.Images.Count;

  public static RawImage ToRawImage(SdgFile file, int index)
    => (uint)index >= (uint)file.Images.Count ? throw new ArgumentOutOfRangeException(nameof(index)) : file.Images[index];

  public static IReadOnlyList<RawImage> ToRawImages(SdgFile file) => file.Images;
}
