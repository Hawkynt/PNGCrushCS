using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.H264Video;

/// <summary>
/// A raw H.264 byte stream: the access units it is a sequence of, as packets.
/// </summary>
/// <remarks>
/// There is no container here in the usual sense — a <c>.264</c> is the video half of an H.264 stream
/// with nothing wrapped round it, no index, no timestamps and no second stream. It is nevertheless a
/// container in the sense that matters: it declares one stream and it says where each packet begins
/// and ends, which is the whole of what a demuxer does.
/// <para/>
/// This is also the form the same coded pictures take when they are <em>not</em> in a file: a
/// transport stream and a program stream carry exactly these bytes, and so does a decoder handed a
/// stream over a socket. The MP4, Matroska and FLV form is the other one, with each NAL unit behind
/// its length instead of behind a start code. Both reach the same decoder and must produce the same
/// frames, which is a thing worth testing rather than assuming.
/// </remarks>
[FormatMimeType("video/H264", "video/h264", "video/x-h264")]
public sealed class H264VideoContainer : IVideoContainerReader<H264VideoContainer> {

  /// <summary>The stream bytes, as one window the packets are windows onto.</summary>
  public required ReadOnlyMemory<byte> Data { get; init; }

  /// <summary>
  /// <c>avc1</c>, the four-character code the ISO base media containers name this codec with.
  /// </summary>
  /// <remarks>
  /// A byte stream has no field to carry a code in, so this is the demuxer stating what it knows from
  /// the file's own structure rather than repeating something the file said. A code has to be stated
  /// all the same: it is the only thing that reaches a decoder, and a stream tagged with nothing would
  /// be offered to the uncompressed decoder, whose tag is zero.
  /// </remarks>
  private static readonly MediaStreamInfo[] _stream = [
    new() {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("avc1"),
      // No dimensions, no frame rate, no frame count and no codec private data: every one of them is
      // in a parameter set inside the stream, which is the decoder's to read. Leaving the private
      // data empty is also what tells the decoder these packets are in the byte stream form.
    },
  ];

  public static string PrimaryExtension => ".264";

  public static string[] FileExtensions => [".264", ".h264", ".avc", ".x264"];

  /// <summary>A file that opens with a start code introducing a unit a stream may be entered at.</summary>
  /// <remarks>
  /// Every MPEG elementary stream opens with <c>00 00 01</c>, so the prefix alone claims MPEG-1 and
  /// MPEG-4 part 2 files as well. What tells them apart is the byte after it, which for H.264 is a
  /// NAL unit header whose top bit is zero — and which for MPEG-1's sequence header (<c>B3</c>) and
  /// MPEG-4's visual object sequence (<c>B0</c>) is not.
  /// </remarks>
  public static bool? MatchesSignature(ReadOnlySpan<byte> header)
    => H264VideoReader.LooksLikeByteStream(header) ? true : null;

  public static H264VideoContainer FromSpan(ReadOnlySpan<byte> data) => H264VideoReader.FromSpan(data);

  /// <summary>Opens a stream over the caller's array, keeping it rather than copying it.</summary>
  public static H264VideoContainer FromBytes(byte[] data) => H264VideoReader.FromBytes(data);

  public static H264VideoContainer FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("H.264 video file not found.", file.FullName);

    return H264VideoReader.FromBytes(File.ReadAllBytes(file.FullName));
  }

  /// <summary>The one stream a raw H.264 file holds.</summary>
  public static IReadOnlyList<MediaStreamInfo> Streams(H264VideoContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    return _stream;
  }

  /// <summary>Walks the access units the stream is a sequence of, one at a time.</summary>
  public static IEnumerable<CodedPacket> ReadPackets(H264VideoContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    return H264VideoReader.Split(container.Data);
  }

  /// <summary>The stream has one stream, so anything but index zero walks nothing.</summary>
  public static IEnumerable<CodedPacket> ReadPackets(H264VideoContainer container, int streamIndex)
    => streamIndex == 0 ? ReadPackets(container) : [];

  /// <summary>
  /// Nothing. A byte stream has no header describing the file, so there is nowhere for a title or a
  /// date to have been written.
  /// </summary>
  public static VideoMetadata Metadata(H264VideoContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    return new() { Streams = [new(0, MediaStreamKind.Video, _stream[0].Codec)] };
  }
}
