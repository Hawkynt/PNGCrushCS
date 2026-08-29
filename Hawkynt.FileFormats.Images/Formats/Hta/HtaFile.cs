using System;
using System.Collections.Generic;
using FileFormat.Core;
using FileFormat.Png;

namespace FileFormat.Hta;

/// <summary>A Hemera Thumbs file (.hta): a directory of whole PNG files carried inside one file.</summary>
public sealed class HtaFile
  : IImageFormatReader<HtaFile>, IImageToRawImage<HtaFile>, IImageFromRawImage<HtaFile>, IImageFormatWriter<HtaFile>, IMultiImageFileFormat<HtaFile> {

  public static ReadOnlySpan<byte> Magic => [0x89, (byte)'H', (byte)'T', (byte)'A', 0x0D, 0x0A, 0x1A, 0x0A];
  public const int SupportedVersion = 100;
  public const int DirectoryOffset = 16;
  public const int DirectoryEntrySize = 8;
  public const int FirstMemberOffset = 64;
  public const int MaximumMemberCount = 65536;

  static string IImageFormatMetadata<HtaFile>.PrimaryExtension => ".hta";
  static string[] IImageFormatMetadata<HtaFile>.FileExtensions => [".hta"];
  static FormatCapability IImageFormatMetadata<HtaFile>.Capabilities => FormatCapability.MultiImage;
  static HtaFile IImageFormatReader<HtaFile>.FromSpan(ReadOnlySpan<byte> data) => HtaReader.FromSpan(data);
  static byte[] IImageFormatWriter<HtaFile>.ToBytes(HtaFile file) => HtaWriter.ToBytes(file);

  public IReadOnlyList<byte[]> Members { get; init; } = [];
  public int Version { get; init; } = SupportedVersion;

  public static RawImage ToRawImage(HtaFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return ToRawImage(file, 0);
  }

  public static int ImageCount(HtaFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return file.Members.Count;
  }

  public static RawImage ToRawImage(HtaFile file, int index) {
    ArgumentNullException.ThrowIfNull(file);
    if ((uint)index >= (uint)file.Members.Count)
      throw new ArgumentOutOfRangeException(nameof(index), $"A Hemera Thumbs file of {file.Members.Count} members has no member {index}.");

    return PngFile.ToRawImage(PngReader.FromBytes(file.Members[index]));
  }

  /// <summary>Creates a one-member HTA containing a standards-valid PNG of the source image.</summary>
  public static HtaFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    return new() {
      Version = SupportedVersion,
      Members = [PngWriter.ToBytes(PngFile.FromRawImage(image))],
    };
  }
}
