using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using FileFormat.Jpeg;

namespace FileFormat.Mpo;

/// <summary>In-memory representation of an MPO (Multi-Picture Object) file.</summary>
public sealed class MpoFile : IImageFileFormat<MpoFile>, IMultiImageFileFormat<MpoFile> {

  static string IImageFileFormat<MpoFile>.PrimaryExtension => ".mpo";
  static string[] IImageFileFormat<MpoFile>.FileExtensions => [".mpo"];
  static FormatCapability IImageFileFormat<MpoFile>.Capabilities => FormatCapability.MultiImage;
  static MpoFile IImageFileFormat<MpoFile>.FromFile(FileInfo file) => MpoReader.FromFile(file);
  static MpoFile IImageFileFormat<MpoFile>.FromBytes(byte[] data) => MpoReader.FromBytes(data);
  static MpoFile IImageFileFormat<MpoFile>.FromStream(Stream stream) => MpoReader.FromStream(stream);
  static byte[] IImageFileFormat<MpoFile>.ToBytes(MpoFile file) => MpoWriter.ToBytes(file);

  /// <summary>The individual JPEG images contained in this MPO file, each as a complete JPEG byte array.</summary>
  public IReadOnlyList<byte[]> Images { get; init; } = [];

  /// <summary>Converts the first image of an MPO file to a <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(MpoFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.Images.Count == 0)
      throw new ArgumentException("MPO file contains no images.", nameof(file));

    var jpeg = JpegReader.FromBytes(file.Images[0]);
    return JpegFile.ToRawImage(jpeg);
  }

  /// <summary>Returns the number of images in the MPO file.</summary>
  public static int ImageCount(MpoFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return file.Images.Count;
  }

  /// <summary>Converts a specific image at the given index to a <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(MpoFile file, int index) {
    ArgumentNullException.ThrowIfNull(file);
    if ((uint)index >= (uint)file.Images.Count)
      throw new ArgumentOutOfRangeException(nameof(index));

    var jpeg = JpegReader.FromBytes(file.Images[index]);
    return JpegFile.ToRawImage(jpeg);
  }

  /// <summary>Creates a single-image MPO file from a <see cref="RawImage"/>.</summary>
  public static MpoFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var jpeg = JpegFile.FromRawImage(image);
    var jpegBytes = JpegWriter.ToBytes(jpeg);

    return new MpoFile {
      Images = [jpegBytes]
    };
  }
}
