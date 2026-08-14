using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Core;
using FileFormat.Jpeg;

namespace FileFormat.Avi;

/// <summary>In-memory representation of the video track of an AVI file.</summary>
/// <remarks>
/// A video container is here for one reason: two of the ways an AVI stores its frames are formats
/// this library already decodes whole. A Motion JPEG frame chunk is a JPEG; an uncompressed frame
/// chunk is the pixel array of a Windows bitmap. Reading those needs the container taken apart and
/// nothing else — no codec, no inter-frame prediction, no motion compensation.
/// <para/>
/// Everything else an AVI can hold does need one of those, and is refused by name rather than half
/// decoded. See <see cref="AviReader"/>.
/// </remarks>
[FormatMimeType("video/avi", "video/msvideo", "video/x-msvideo")]
public sealed class AviFile : IImageFormatReader<AviFile>, IImageToRawImage<AviFile>, IMultiImageFileFormat<AviFile> {

  static string IImageFormatMetadata<AviFile>.PrimaryExtension => ".avi";
  static string[] IImageFormatMetadata<AviFile>.FileExtensions => [".avi"];
  static AviFile IImageFormatReader<AviFile>.FromSpan(ReadOnlySpan<byte> data) => AviReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<AviFile>.Capabilities => FormatCapability.MultiImage;

  /// <summary>
  /// A RIFF file of form <c>AVI </c>. The form type is checked as well as the signature because
  /// WAVE, ANI and WebP are all RIFF too, and the first four bytes alone do not tell them apart.
  /// </summary>
  static bool? IImageFormatMetadata<AviFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 12
       && header[0] == (byte)'R' && header[1] == (byte)'I' && header[2] == (byte)'F' && header[3] == (byte)'F'
       && header[8] == (byte)'A' && header[9] == (byte)'V' && header[10] == (byte)'I' && header[11] == (byte)' '
      ? true
      : null;

  /// <summary>The <c>avih</c> header of the file.</summary>
  public required AviMainHeader Header { get; init; }

  /// <summary>The video stream whose frames <see cref="Frames"/> holds.</summary>
  public required AviVideoStream Video { get; init; }

  /// <summary>One entry per non-empty frame chunk of <see cref="Video"/>, in the order written.</summary>
  public IReadOnlyList<byte[]> Frames { get; init; } = [];

  /// <summary>Returns the number of frames in this file.</summary>
  public static int ImageCount(AviFile file) {
    ArgumentNullException.ThrowIfNull(file);

    return file.Frames.Count;
  }

  /// <summary>Converts the frame at the given index to a <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(AviFile file, int index) {
    ArgumentNullException.ThrowIfNull(file);
    if ((uint)index >= (uint)file.Frames.Count)
      throw new ArgumentOutOfRangeException(nameof(index));

    var frame = file.Frames[index];
    switch (file.Video.Coding) {
      case AviVideoCoding.MotionJpeg:
        return JpegFile.ToRawImage(JpegReader.FromSpan(frame));

      case AviVideoCoding.Uncompressed: {
        var wanted = AviReader.UncompressedFrameSize(file.Video);
        if (frame.Length < wanted)
          throw new InvalidDataException(
            $"Frame {index} holds {frame.Length} bytes where {file.Video.Width}x{file.Video.Height} at {file.Video.BitsPerPixel} bits needs {wanted}.");

        return BmpFile.ToRawImage(BmpReader.FromSpan(AviReader.ToBitmapFile(file.Video, frame)));
      }

      default:
        throw new NotSupportedException($"Unhandled AVI video coding {file.Video.Coding}.");
    }
  }

  /// <summary>Converts the first frame of the file to a <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(AviFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.Frames.Count == 0)
      throw new InvalidDataException("AVI file contains no video frames.");

    return ToRawImage(file, 0);
  }
}
