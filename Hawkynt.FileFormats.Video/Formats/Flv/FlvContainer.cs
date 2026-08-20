using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Flv;

/// <summary>
/// A Flash Video file taken apart into the streams its tags belong to and the packets they hold —
/// and nothing else.
/// </summary>
/// <remarks>
/// This container knows where every packet is and not one thing about what is inside any of them. It
/// does not decode, it does not report pictures, and it does not refuse a file for naming a codec
/// nothing here reads: an FLV full of H.264 is a perfectly good FLV, and copying its packets into
/// another container needs no decoder at all. Refusal by code still happens, at the moment a decoder
/// is asked for.
/// <para/>
/// What makes this container different from the other three is that it declares nothing. An AVI names
/// its streams in <c>hdrl</c> and an ISO base media file names them in <c>moov</c>, both before a
/// single packet; an FLV names them nowhere. Its nine-byte header says whether sound and pictures are
/// present and stops there, so the streams have to be discovered from the tags, and the codec of each
/// from the first tag belonging to it. That is <see cref="FlvReader"/>'s opening walk.
/// <para/>
/// Two of the payload shapes are not frames and are handled here rather than passed on. An AVC video
/// tag whose packet type is zero carries the decoder's configuration record, which becomes
/// <see cref="MediaStreamInfo.CodecPrivateData"/> and never a packet; an AAC audio tag whose packet
/// type is zero carries the audio specific config and does the same. Handing either out would put a
/// unit in the stream that decodes to nothing and would make the packet count disagree with every
/// other tool's.
/// </remarks>
[FormatMimeType("video/x-flv", "flv-application/octet-stream")]
public sealed class FlvContainer : IVideoContainerReader<FlvContainer> {

  /// <summary>The whole file, which every packet is a window onto.</summary>
  public required ReadOnlyMemory<byte> File { get; init; }

  /// <summary>Where the first tag begins, as the file header states it.</summary>
  /// <remarks>
  /// Internal, along with the two stream numbers below, because they are this reader's own
  /// bookkeeping rather than anything a caller has a use for — the same reason an ISO base media
  /// container keeps its sample tables to itself. What a caller wants is
  /// <see cref="Streams"/> and <see cref="ReadPackets(FlvContainer)"/>.
  /// </remarks>
  internal int FirstTagOffset { get; init; }

  /// <summary>Every stream the file's tags turn out to belong to, in order of first appearance.</summary>
  public required IReadOnlyList<MediaStreamInfo> StreamInfos { get; init; }

  /// <summary>What the file says about itself, from its <c>onMetaData</c>.</summary>
  public required VideoMetadata FileMetadata { get; init; }

  /// <summary>Which stream the audio tags belong to, or <c>-1</c> where the file has none.</summary>
  /// <remarks>
  /// An FLV holds at most one stream of each kind — a tag says whether it is sound or pictures and
  /// nothing finer — so the whole of the mapping from tag to stream is these two numbers.
  /// </remarks>
  internal int AudioStream { get; init; } = -1;

  /// <summary>Which stream the video tags belong to, or <c>-1</c> where the file has none.</summary>
  internal int VideoStream { get; init; } = -1;

  // -------- Format identity --------

  public static string PrimaryExtension => ".flv";

  /// <summary>
  /// The names an FLV is stored under.
  /// </summary>
  /// <remarks>
  /// <c>.f4v</c> is Adobe's later format and is really an ISO base media file, not this one — but the
  /// two were shipped by the same tools for the same purpose and files of these bytes turn up under
  /// that name. Claiming it costs nothing and loses nothing, because the extension is only ever a
  /// fallback: detection goes by the signature, so a genuine F4V is recognised by its <c>ftyp</c> and
  /// goes to the ISO base media reader whatever this list says.
  /// </remarks>
  public static string[] FileExtensions => [".flv", ".f4v"];

  /// <summary>
  /// A file beginning with <c>FLV</c> and the one version the format has.
  /// </summary>
  /// <remarks>
  /// The version is checked as well as the three letters because three letters is a weak thing to
  /// claim a format on, and because a fourth byte that is not 1 is not a file this reader would open
  /// anyway. A file whose first three bytes are right and whose version is not leaves the verdict
  /// undecided rather than claiming it, so a later format with the same three letters could still be
  /// recognised by whoever knows it.
  /// </remarks>
  public static bool? MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 4 && header[0] == (byte)'F' && header[1] == (byte)'L' && header[2] == (byte)'V' && header[3] == 1
      ? true
      : null;

  // -------- Demux --------

  public static FlvContainer FromSpan(ReadOnlySpan<byte> data) => FlvReader.FromSpan(data);

  /// <summary>Opens a file over the caller's array, keeping it rather than copying it.</summary>
  public static FlvContainer FromBytes(byte[] data) => FlvReader.FromBytes(data);

  public static FlvContainer FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("FLV file not found.", file.FullName);

    return FlvReader.FromBytes(System.IO.File.ReadAllBytes(file.FullName));
  }

  /// <summary>Every stream the container holds — the sound as well as the pictures.</summary>
  public static IReadOnlyList<MediaStreamInfo> Streams(FlvContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    return container.StreamInfos;
  }

  /// <summary>What the container says about itself.</summary>
  public static VideoMetadata Metadata(FlvContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    return container.FileMetadata;
  }

  /// <summary>Walks every packet of the file, in the order it stores them.</summary>
  /// <remarks>
  /// Lazy and re-runnable: the tag chain is walked again from the front, no payload is touched until
  /// a packet is asked for, and each packet's data is a window onto the buffer the file was read into
  /// rather than a copy of it.
  /// <para/>
  /// Storage order is playing order here without any merging, which is what makes this the simplest
  /// of the four walks: an FLV interleaves its sound and its pictures as one chain of tags, each
  /// carrying the moment it is due, so reading the chain forwards is reading the film in order.
  /// ffprobe reports the same order for the same file, sound and pictures alternating exactly as the
  /// tags do.
  /// </remarks>
  public static IEnumerable<CodedPacket> ReadPackets(FlvContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    return FlvReader.Walk(container, null);
  }

  /// <summary>Walks the packets of one stream, in storage order.</summary>
  public static IEnumerable<CodedPacket> ReadPackets(FlvContainer container, int streamIndex) {
    ArgumentNullException.ThrowIfNull(container);

    return FlvReader.Walk(container, streamIndex);
  }
}
