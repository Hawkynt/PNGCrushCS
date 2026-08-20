using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Mpeg1Video;

/// <summary>
/// A raw MPEG-1 video elementary stream: the coded pictures it is a sequence of, as packets.
/// </summary>
/// <remarks>
/// There is no container here in the usual sense — a <c>.m1v</c> is the video half of an MPEG-1
/// stream with nothing wrapped round it, no index, no timestamps and no second stream. It is
/// nevertheless a container in the sense that matters: it declares one stream and it says where each
/// packet begins and ends, which is the whole of what a demuxer does. Turning those packets into
/// pictures is <see cref="FileFormat.Codecs.Mpeg1VideoDecoder"/>'s, and this type knows nothing about
/// how that is done.
/// <para/>
/// It really does know nothing. The picture size, the frame rate, the aspect ratio and the quantiser
/// matrices are all in the sequence header, four bytes past a start code this walks straight over,
/// and none of them is read here. That is why <see cref="Streams"/> declares a stream with no
/// dimensions: the demuxer has not been told any, and copying a number out of the sequence header to
/// fill the field in would be the decoder's parse done twice, in two places, with two chances to
/// disagree. The same reasoning keeps <see cref="FileFormat.Mjpeg.MjpegContainer"/> silent about its
/// frames.
/// </remarks>
[FormatMimeType("video/mpv", "video/x-mpeg1video")]
public sealed class Mpeg1VideoContainer : IVideoContainerReader<Mpeg1VideoContainer> {

  /// <summary>The stream bytes, as one window the packets are windows onto.</summary>
  public required ReadOnlyMemory<byte> Data { get; init; }

  /// <summary>
  /// <c>MPG1</c>, the four-character code AVI and Matroska name this codec with.
  /// </summary>
  /// <remarks>
  /// An elementary stream has no field to carry a code in, so this is the demuxer stating what it
  /// knows from the file's own structure rather than repeating something the file said. A code has to
  /// be stated all the same: it is the only thing that reaches a decoder, and a stream tagged with
  /// nothing would be offered to the uncompressed decoder, whose tag is zero.
  /// </remarks>
  private static readonly MediaStreamInfo[] _stream = [
    new() {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("MPG1"),
      // No dimensions, no frame rate, no frame count: every one of them is in the sequence header,
      // which is the decoder's to read.
    },
  ];

  public static string PrimaryExtension => ".m1v";

  public static string[] FileExtensions => [".m1v", ".mpv", ".mpeg1video", ".m1v1"];

  /// <summary>
  /// A file that opens with a sequence header start code.
  /// </summary>
  /// <remarks>
  /// <c>00 00 01 B3</c> and nothing shorter. The three-byte start-code prefix on its own is shared
  /// with every other MPEG codec down to H.264, and the fourth byte is what says this one is a
  /// sequence header rather than a picture, a slice or an access-unit delimiter.
  /// <para/>
  /// An MPEG-2 stream opens with the same four bytes and is told apart only by the sequence extension
  /// that follows the sequence header. That distinction is made in the decoder, which reads both
  /// headers anyway and refuses the extension by name; making it here would mean walking to the
  /// second header of a file to answer a question about its first.
  /// </remarks>
  public static bool? MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 4
       && header[0] == 0x00 && header[1] == 0x00 && header[2] == 0x01
       && header[3] == Mpeg1VideoReader.SequenceHeaderCode
      ? true
      : null;

  public static Mpeg1VideoContainer FromSpan(ReadOnlySpan<byte> data) => Mpeg1VideoReader.FromSpan(data);

  /// <summary>Opens a stream over the caller's array, keeping it rather than copying it.</summary>
  public static Mpeg1VideoContainer FromBytes(byte[] data) => Mpeg1VideoReader.FromBytes(data);

  public static Mpeg1VideoContainer FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("MPEG-1 video file not found.", file.FullName);

    return Mpeg1VideoReader.FromBytes(File.ReadAllBytes(file.FullName));
  }

  /// <summary>The one stream a raw MPEG-1 video file holds.</summary>
  public static IReadOnlyList<MediaStreamInfo> Streams(Mpeg1VideoContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    return _stream;
  }

  /// <summary>Walks the coded pictures the stream is a sequence of, one at a time.</summary>
  public static IEnumerable<CodedPacket> ReadPackets(Mpeg1VideoContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    return Mpeg1VideoReader.Split(container.Data);
  }

  /// <summary>The stream has one stream, so anything but index zero walks nothing.</summary>
  public static IEnumerable<CodedPacket> ReadPackets(Mpeg1VideoContainer container, int streamIndex)
    => streamIndex == 0 ? ReadPackets(container) : [];

  /// <summary>
  /// Nothing. An elementary stream has no header describing the file, so there is nowhere for a title
  /// or a date to have been written.
  /// </summary>
  public static VideoMetadata Metadata(Mpeg1VideoContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    return new() { Streams = [new(0, MediaStreamKind.Video, _stream[0].Codec)] };
  }
}
