using System;
using System.IO;
using System.IO.Compression;
using FileFormat.Core;

namespace FileFormat.Nifti;

/// <summary>GZip-wrapped single-file NIfTI (.nii.gz).</summary>
[FormatDetectionPriority(90)]
public sealed class NiftiGzipFile :
  IImageFormatReader<NiftiGzipFile>, IImageToRawImage<NiftiGzipFile>,
  IImageFromRawImage<NiftiGzipFile>, IImageFormatWriter<NiftiGzipFile> {

  static string IImageFormatMetadata<NiftiGzipFile>.PrimaryExtension => ".nii.gz";
  static string[] IImageFormatMetadata<NiftiGzipFile>.FileExtensions => [".nii.gz"];
  static NiftiGzipFile IImageFormatReader<NiftiGzipFile>.FromSpan(ReadOnlySpan<byte> data) => NiftiGzipReader.FromSpan(data);
  static byte[] IImageFormatWriter<NiftiGzipFile>.ToBytes(NiftiGzipFile file) => NiftiGzipWriter.ToBytes(file);

  public NiftiFile Nifti { get; init; } = new();

  public static bool? MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < 2 || header[0] != 0x1F || header[1] != 0x8B)
      return false;

    try {
      var decompressed = NiftiGzipReader.Decompress(header);
      if (decompressed.Length < NiftiHeader.StructSize)
        return null;
      var magic = NiftiHeader.ReadFrom(decompressed).Magic;
      return magic == "n+1" ? true : null;
    } catch {
      return null;
    }
  }

  public static RawImage ToRawImage(NiftiGzipFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return NiftiFile.ToRawImage(file.Nifti);
  }

  public static NiftiGzipFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    return new() { Nifti = NiftiFile.FromRawImage(image) };
  }
}

public static class NiftiGzipReader {

  public static NiftiGzipFile FromSpan(ReadOnlySpan<byte> data)
    => new() { Nifti = NiftiReader.FromBytes(Decompress(data)) };

  internal static byte[] Decompress(ReadOnlySpan<byte> data) {
    if (data.Length < 2 || data[0] != 0x1F || data[1] != 0x8B)
      throw new InvalidDataException("Data is not a GZip stream.");

    using var input = new MemoryStream(data.ToArray(), writable: false);
    using var gzip = new GZipStream(input, CompressionMode.Decompress);
    using var output = new MemoryStream();
    gzip.CopyTo(output);
    return output.ToArray();
  }
}

public static class NiftiGzipWriter {

  public static byte[] ToBytes(NiftiGzipFile file) {
    ArgumentNullException.ThrowIfNull(file);
    var uncompressed = NiftiWriter.ToBytes(file.Nifti);
    using var output = new MemoryStream();
    using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
      gzip.Write(uncompressed, 0, uncompressed.Length);
    return output.ToArray();
  }
}
