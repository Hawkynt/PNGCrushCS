using System;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.Ipg;

/// <summary>A Mindjongg IPG tileset containing several embedded raster images.</summary>
/// <remarks>
/// IPG stores complete BMP, GIF, JPEG or TGA files behind an index. It is therefore exposed as a
/// multi-image container. No single <see cref="RawImage"/> contains enough information to recreate
/// the tileset roles and metadata, so the registry deliberately has no writer for this format.
/// </remarks>
public sealed class IpgFile : IImageFormatReader<IpgFile>, IImageToRawImage<IpgFile>, IMultiImageFileFormat<IpgFile> {

  static string IImageFormatMetadata<IpgFile>.PrimaryExtension => ".ipg";
  static string[] IImageFormatMetadata<IpgFile>.FileExtensions => [".ipg"];
  static IpgFile IImageFormatReader<IpgFile>.FromSpan(ReadOnlySpan<byte> data) => IpgReader.FromSpan(data);
  static bool? IImageFormatMetadata<IpgFile>.MatchesSignature(ReadOnlySpan<byte> header) => IpgReader.MatchesSignature(header);
  static VideoMode[] IImageFormatMetadata<IpgFile>.VideoModes => [
    new("Embedded image", [(IntegerRange.Any, IntegerRange.Any)])
  ];

  /// <summary>Mindjongg container generation: 1 for IPK01, 3 for IPK03.</summary>
  public int Version { get; init; }

  public List<RawImage> Images { get; init; } = [];

  public static RawImage ToRawImage(IpgFile file)
    => file.Images.Count == 0 ? throw new InvalidOperationException("The IPG tileset has no embedded image.") : file.Images[0];

  public static int ImageCount(IpgFile file) => file.Images.Count;

  public static RawImage ToRawImage(IpgFile file, int index)
    => (uint)index >= (uint)file.Images.Count ? throw new ArgumentOutOfRangeException(nameof(index)) : file.Images[index];

  public static IReadOnlyList<RawImage> ToRawImages(IpgFile file) => file.Images;
}
