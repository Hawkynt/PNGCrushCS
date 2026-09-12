using System;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.Sdg;

/// <summary>A StarOffice/LibreOffice Gallery data file represented by the bitmap thumbnails its SGA objects carry.</summary>
/// <remarks>
/// An SDG is the object-data member of a gallery theme. Objects are stored consecutively and may
/// carry bitmap or metafile thumbnails; the image contract exposes the bitmap thumbnails. Writing a
/// <see cref="RawImage"/> creates a bitmap gallery object, and a multi-image file writes one such
/// object for every image.
/// </remarks>
[FormatMagicBytes([(byte)'S', (byte)'G', (byte)'A', (byte)'3'])]
public sealed class SdgFile :
  IImageFormatReader<SdgFile>, IImageToRawImage<SdgFile>, IImageFromRawImage<SdgFile>, IImageFormatWriter<SdgFile>,
  IMultiImageFileFormat<SdgFile> {

  static string IImageFormatMetadata<SdgFile>.PrimaryExtension => ".sdg";
  static string[] IImageFormatMetadata<SdgFile>.FileExtensions => [".sdg"];
  static SdgFile IImageFormatReader<SdgFile>.FromSpan(ReadOnlySpan<byte> data) => SdgReader.FromSpan(data);
  static byte[] IImageFormatWriter<SdgFile>.ToBytes(SdgFile file) => SdgWriter.ToBytes(file);
  static FormatCapability IImageFormatMetadata<SdgFile>.Capabilities => FormatCapability.MultiImage;
  static VideoMode[] IImageFormatMetadata<SdgFile>.VideoModes => [
    new("Gallery thumbnail", [(IntegerRange.Any, IntegerRange.Any)])
  ];

  public List<RawImage> Images { get; init; } = [];

  public static SdgFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    return new SdgFile { Images = [image] };
  }

  public static RawImage ToRawImage(SdgFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return file.Images.Count == 0
      ? throw new InvalidOperationException("The SDG file has no bitmap thumbnail.")
      : file.Images[0];
  }

  public static int ImageCount(SdgFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return file.Images.Count;
  }

  public static RawImage ToRawImage(SdgFile file, int index) {
    ArgumentNullException.ThrowIfNull(file);
    return (uint)index >= (uint)file.Images.Count
      ? throw new ArgumentOutOfRangeException(nameof(index))
      : file.Images[index];
  }

  public static IReadOnlyList<RawImage> ToRawImages(SdgFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return file.Images;
  }
}
