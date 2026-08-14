using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using FileFormat.Jpeg;

namespace FileFormat.Mjpeg;

/// <summary>In-memory representation of a raw Motion JPEG stream: the frames it concatenates.</summary>
/// <remarks>
/// The name is only reached by extension. A single-frame <c>.mjpg</c> is a valid JPEG byte for byte,
/// so claiming <c>FF D8 FF</c> as a signature here would put this format in competition with the
/// JPEG reader for every photograph in existence, and win nothing: a one-frame stream read as a
/// JPEG is the same picture.
/// </remarks>
[FormatMimeType("video/x-motion-jpeg")]
public sealed class MjpegFile : IImageFormatReader<MjpegFile>, IImageToRawImage<MjpegFile>, IMultiImageFileFormat<MjpegFile> {

  static string IImageFormatMetadata<MjpegFile>.PrimaryExtension => ".mjpg";
  static string[] IImageFormatMetadata<MjpegFile>.FileExtensions => [".mjpg", ".mjpeg"];
  static MjpegFile IImageFormatReader<MjpegFile>.FromSpan(ReadOnlySpan<byte> data) => MjpegReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<MjpegFile>.Capabilities => FormatCapability.MultiImage;

  /// <summary>The complete JPEGs the stream is made of, in order.</summary>
  public IReadOnlyList<byte[]> Frames { get; init; } = [];

  /// <summary>Returns the number of frames in this stream.</summary>
  public static int ImageCount(MjpegFile file) {
    ArgumentNullException.ThrowIfNull(file);

    return file.Frames.Count;
  }

  /// <summary>Converts the frame at the given index to a <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(MjpegFile file, int index) {
    ArgumentNullException.ThrowIfNull(file);
    if ((uint)index >= (uint)file.Frames.Count)
      throw new ArgumentOutOfRangeException(nameof(index));

    return JpegFile.ToRawImage(JpegReader.FromSpan(file.Frames[index]));
  }

  /// <summary>Converts the first frame of the stream to a <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(MjpegFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.Frames.Count == 0)
      throw new InvalidDataException("MJPEG stream contains no frames.");

    return ToRawImage(file, 0);
  }
}
