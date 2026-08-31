using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.H265Video;

/// <summary>
/// A raw H.265 byte stream: the access units it is a sequence of, as packets.
/// </summary>
/// <remarks>
/// There is no container here in the usual sense — a <c>.265</c> is the video half of an HEVC stream
/// with nothing wrapped round it, no index, no timestamps and no second stream. It is nevertheless a
/// container in the sense that matters: it declares one stream and it says where each packet begins
/// and ends, which is the whole of what a demuxer does.
/// <para/>
/// This is also the form the same coded pictures take when they are <em>not</em> in a file: a
/// transport stream and a program stream carry exactly these bytes. The MP4 and Matroska form is the
/// other one, with each NAL unit behind its length instead of behind a start code. Both reach the
/// same decoder and must produce the same frames.
/// <para/>
/// It is checked before the H.264 byte stream because the two are told apart by the byte after the
/// start code and one HEVC unit type reads as a valid H.264 one. Ordering the check rather than
/// widening the signature keeps each format's own test honest about what it recognises.
/// </remarks>
[FormatMimeType("video/H265", "video/h265", "video/x-h265", "video/hevc")]
[FormatDetectionPriority(-1)]
public sealed class H265VideoContainer : IVideoContainerReader<H265VideoContainer> {
  /// <summary>Initializes a new instance of this type.</summary>
  public H265VideoContainer() { }

  /// <summary>The stream bytes, as one window the packets are windows onto.</summary>
  public required ReadOnlyMemory<byte> Data { get; init; }

  /// <summary>
  /// <c>hvc1</c>, the four-character code the ISO base media containers name this codec with.
  /// </summary>
  /// <remarks>
  /// A byte stream has no field to carry a code in, so this is the demuxer stating what it knows from
  /// the file's own structure rather than repeating something the file said. A code has to be stated
  /// all the same: it is the only thing that reaches a decoder, and a stream tagged with nothing
  /// would be offered to the uncompressed decoder, whose tag is zero.
  /// </remarks>
  private static readonly MediaStreamInfo[] _stream = [
    new() {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("hvc1"),
      // No dimensions, no frame rate, no frame count and no codec private data: every one of them is
      // in a parameter set inside the stream, which is the decoder's to read. Leaving the private
      // data empty is also what tells the decoder these packets are in the byte stream form.
    },
  ];

  /// <summary>Gets the primary file extension for this format.</summary>
  public static string PrimaryExtension => ".265";

  /// <summary>Gets the file extensions supported by this format.</summary>
  public static string[] FileExtensions => [".265", ".h265", ".hevc", ".x265"];

  /// <summary>A file that opens with a start code introducing a unit a stream may be entered at.</summary>
  public static bool? MatchesSignature(ReadOnlySpan<byte> header)
    => H265VideoReader.LooksLikeByteStream(header) ? true : null;

  /// <summary>Reads an instance from the specified byte span.</summary>
  public static H265VideoContainer FromSpan(ReadOnlySpan<byte> data) => H265VideoReader.FromSpan(data);

  /// <summary>Opens a stream over the caller's array, keeping it rather than copying it.</summary>
  public static H265VideoContainer FromBytes(byte[] data) => H265VideoReader.FromBytes(data);

  /// <summary>Reads an instance from the specified file.</summary>
  public static H265VideoContainer FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("H.265 video file not found.", file.FullName);

    return H265VideoReader.FromBytes(File.ReadAllBytes(file.FullName));
  }

  /// <summary>The one stream a raw H.265 file holds.</summary>
  public static IReadOnlyList<MediaStreamInfo> Streams(H265VideoContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    return _stream;
  }

  /// <summary>Walks the access units the stream is a sequence of, one at a time.</summary>
  public static IEnumerable<CodedPacket> ReadPackets(H265VideoContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    return H265VideoReader.Split(container.Data);
  }

  /// <summary>The stream has one stream, so anything but index zero walks nothing.</summary>
  public static IEnumerable<CodedPacket> ReadPackets(H265VideoContainer container, int streamIndex)
    => streamIndex == 0 ? ReadPackets(container) : [];

  /// <summary>
  /// Nothing. A byte stream has no header describing the file, so there is nowhere for a title or a
  /// date to have been written.
  /// </summary>
  public static VideoMetadata Metadata(H265VideoContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    return new() { Streams = [new(0, MediaStreamKind.Video, _stream[0].Codec)] };
  }
}
