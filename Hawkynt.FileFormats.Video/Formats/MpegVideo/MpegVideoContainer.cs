using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.MpegVideo;

/// <summary>
/// A raw MPEG-1 or MPEG-2 video elementary stream: the coded pictures it is a sequence of, as packets.
/// </summary>
/// <remarks>
/// There is no container here in the usual sense — a <c>.m1v</c> or <c>.m2v</c> is the video half of
/// an MPEG stream with nothing wrapped round it, no index, no timestamps and no second stream. It is
/// nevertheless a container in the sense that matters: it declares one stream and it says where each
/// packet begins and ends, which is the whole of what a demuxer does. Turning those packets into
/// pictures is <see cref="FileFormat.Codecs.Mpeg1VideoDecoder"/>'s and
/// <see cref="FileFormat.Codecs.Mpeg2VideoDecoder"/>'s, and this type knows nothing about how that is
/// done.
/// <para/>
/// It really does know almost nothing. The picture size, the frame rate, the aspect ratio and the
/// quantiser matrices are all in the sequence header, four bytes past a start code this walks
/// straight over, and none of them is read here. That is why <see cref="Streams"/> declares a stream
/// with no dimensions: the demuxer has not been told any, and copying a number out of the sequence
/// header to fill the field in would be the decoder's parse done twice, in two places, with two
/// chances to disagree. The same reasoning keeps <see cref="FileFormat.Mjpeg.MjpegContainer"/> silent
/// about its frames.
/// <para/>
/// The one exception is which of the two standards the stream is, and that exception is not a copied
/// field — it is the file's own structure. A stream has to be named with a four-character code
/// because a code is the only thing that reaches a decoder, and the two standards have different
/// ones. So the walk goes as far as the first start code after the sequence header and asks whether
/// it is a sequence extension: that is the whole of the difference between the two formats' opening
/// bytes, it is one byte to look at, and it is what every other tool uses to tell them apart.
/// </remarks>
[FormatMimeType("video/mpv", "video/x-mpeg1video", "video/mpeg2video", "video/x-mpeg2video")]
public sealed class MpegVideoContainer : IVideoContainerReader<MpegVideoContainer> {

  /// <summary>The stream bytes, as one window the packets are windows onto.</summary>
  public required ReadOnlyMemory<byte> Data { get; init; }

  /// <summary>
  /// <c>MPG1</c> and <c>MPG2</c>, the four-character codes AVI and Matroska name the two codecs with.
  /// </summary>
  /// <remarks>
  /// An elementary stream has no field to carry a code in, so this is the demuxer stating what it
  /// knows from the file's own structure rather than repeating something the file said. A code has to
  /// be stated all the same: it is the only thing that reaches a decoder, and a stream tagged with
  /// nothing would be offered to the uncompressed decoder, whose tag is zero.
  /// </remarks>
  private static readonly MediaStreamInfo[] _mpeg1 = [
    new() {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("MPG1"),
      // No dimensions, no frame rate, no frame count: every one of them is in the sequence header,
      // which is the decoder's to read.
    },
  ];

  private static readonly MediaStreamInfo[] _mpeg2 = [
    new() {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("MPG2"),
    },
  ];

  public static string PrimaryExtension => ".m1v";

  public static string[] FileExtensions =>
    [".m1v", ".m2v", ".mpv", ".mpeg1video", ".mpeg2video", ".m1v1", ".m2v1"];

  /// <summary>
  /// A file that opens with a sequence header start code.
  /// </summary>
  /// <remarks>
  /// <c>00 00 01 B3</c> and nothing shorter. The three-byte start-code prefix on its own is shared
  /// with every other MPEG codec down to H.264, and the fourth byte is what says this one is a
  /// sequence header rather than a picture, a slice or an access-unit delimiter.
  /// <para/>
  /// Both standards open with the same four bytes and are told apart by the sequence extension that
  /// follows the sequence header, which is a question about the file's second header rather than its
  /// first — so it is asked in <see cref="Streams"/>, where the answer is needed, and not here.
  /// </remarks>
  public static bool? MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 4
       && header[0] == 0x00 && header[1] == 0x00 && header[2] == 0x01
       && header[3] == MpegVideoReader.SequenceHeaderCode
      ? true
      : null;

  public static MpegVideoContainer FromSpan(ReadOnlySpan<byte> data) => MpegVideoReader.FromSpan(data);

  /// <summary>Opens a stream over the caller's array, keeping it rather than copying it.</summary>
  public static MpegVideoContainer FromBytes(byte[] data) => MpegVideoReader.FromBytes(data);

  public static MpegVideoContainer FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("MPEG video file not found.", file.FullName);

    return MpegVideoReader.FromBytes(File.ReadAllBytes(file.FullName));
  }

  /// <summary>The one stream a raw MPEG video file holds, named for whichever standard it is.</summary>
  public static IReadOnlyList<MediaStreamInfo> Streams(MpegVideoContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    return MpegVideoReader.CarriesSequenceExtension(container.Data) ? _mpeg2 : _mpeg1;
  }

  /// <summary>Walks the coded pictures the stream is a sequence of, one at a time.</summary>
  public static IEnumerable<CodedPacket> ReadPackets(MpegVideoContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    return MpegVideoReader.Split(container.Data);
  }

  /// <summary>The stream has one stream, so anything but index zero walks nothing.</summary>
  public static IEnumerable<CodedPacket> ReadPackets(MpegVideoContainer container, int streamIndex)
    => streamIndex == 0 ? ReadPackets(container) : [];

  /// <summary>
  /// Nothing. An elementary stream has no header describing the file, so there is nowhere for a title
  /// or a date to have been written.
  /// </summary>
  public static VideoMetadata Metadata(MpegVideoContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    return new() { Streams = [new(0, MediaStreamKind.Video, Streams(container)[0].Codec)] };
  }
}
