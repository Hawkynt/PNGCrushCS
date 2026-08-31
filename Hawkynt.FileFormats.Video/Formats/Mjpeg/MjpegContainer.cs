using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Mjpeg;

/// <summary>
/// A raw Motion JPEG stream: the JPEGs it is a concatenation of, as packets.
/// </summary>
/// <remarks>
/// There is no container here at all — a <c>.mjpg</c> is one complete JPEG after another, each
/// <c>FF D8</c> through <c>FF D9</c>. It is nevertheless a container in the sense that matters: it
/// declares one stream and it says where each packet begins and ends, which is the whole of what a
/// demuxer does. Decoding those packets is <see cref="FileFormat.Codecs.MotionJpegDecoder"/>'s, the
/// same decoder an <c>MJPG</c> AVI reaches, and neither container knows about JPEG.
/// <para/>
/// The format is only reached by extension. A single-frame <c>.mjpg</c> is a valid JPEG byte for
/// byte, so claiming <c>FF D8 FF</c> as a signature here would put this format in competition with
/// the JPEG reader for every photograph in existence, and win nothing: a one-frame stream read as a
/// JPEG is the same picture.
/// </remarks>
[FormatMimeType("video/x-motion-jpeg")]
public sealed class MjpegContainer : IVideoContainerReader<MjpegContainer> {
  /// <summary>Initializes a new instance of this type.</summary>
  public MjpegContainer() { }

  /// <summary>The stream bytes, as one window the packets are windows onto.</summary>
  public required ReadOnlyMemory<byte> Data { get; init; }

  private static readonly MediaStreamInfo[] _stream = [
    new() {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("MJPG"),
      // No dimensions, no frame rate, no frame count. A raw stream has no header to state any of
      // them, and every one of the three is in the JPEGs themselves — inventing values here would
      // mean a decoder could be handed a size the file never claimed.
    },
  ];

  /// <summary>Gets the primary file extension for this format.</summary>
  public static string PrimaryExtension => ".mjpg";

  /// <summary>Gets the file extensions supported by this format.</summary>
  public static string[] FileExtensions => [".mjpg", ".mjpeg"];

  /// <summary>Reads an instance from the specified byte span.</summary>
  public static MjpegContainer FromSpan(ReadOnlySpan<byte> data) => MjpegReader.FromSpan(data);

  /// <summary>Opens a stream over the caller's array, keeping it rather than copying it.</summary>
  public static MjpegContainer FromBytes(byte[] data) => MjpegReader.FromBytes(data);

  /// <summary>Reads an instance from the specified file.</summary>
  public static MjpegContainer FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("MJPEG file not found.", file.FullName);

    return MjpegReader.FromBytes(File.ReadAllBytes(file.FullName));
  }

  /// <summary>The one stream a raw Motion JPEG file holds.</summary>
  public static IReadOnlyList<MediaStreamInfo> Streams(MjpegContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    return _stream;
  }

  /// <summary>Walks the frames the stream is a concatenation of, one at a time.</summary>
  public static IEnumerable<CodedPacket> ReadPackets(MjpegContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    return MjpegReader.Split(container.Data);
  }

  /// <summary>The stream has one stream, so anything but index zero walks nothing.</summary>
  public static IEnumerable<CodedPacket> ReadPackets(MjpegContainer container, int streamIndex)
    => streamIndex == 0 ? ReadPackets(container) : [];

  /// <summary>
  /// Nothing. A raw Motion JPEG stream has no header, so there is nowhere for a title or a date to
  /// have been written.
  /// </summary>
  public static VideoMetadata Metadata(MjpegContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    return new() { Streams = [new(0, MediaStreamKind.Video, _stream[0].Codec)] };
  }
}
