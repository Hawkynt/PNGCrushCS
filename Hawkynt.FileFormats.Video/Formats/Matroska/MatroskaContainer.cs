using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Matroska;

/// <summary>
/// A Matroska document — <c>.mkv</c>, <c>.mka</c>, <c>.webm</c> — taken apart into the tracks it
/// declares and the frames its clusters hold, and nothing else.
/// </summary>
/// <remarks>
/// This container knows where every packet is and not one thing about what is inside any of them. It
/// does not decode, it does not report pictures, and it does not refuse a file for naming a codec
/// nothing here reads: a WebM full of VP9 is a perfectly good WebM, and copying its packets into
/// another container needs no decoder at all. Refusal by name still happens, at the moment a decoder
/// is asked for.
/// <para/>
/// One reader for Matroska and WebM because they are one format. WebM is a Matroska document with a
/// <c>DocType</c> that says so and a shorter list of codecs allowed inside it — and which codecs are
/// allowed is the business of whoever is asked for a decoder, not of the thing finding the packets.
/// The elements, the identifiers, the block layout and the timing are the same bytes in both.
/// <para/>
/// What makes this container unlike the other two is that its packet boundaries are neither in a
/// table nor in the chunk headers. An AVI stores each packet as a chunk with its own length, so
/// walking the file is walking the packets; an ISO base media file stores no boundaries at all and
/// computes them from five tables. Matroska stores them in the clusters themselves — a cluster is a
/// timestamp and a run of blocks, a block may hold several frames at once, and there is no index of
/// any of it. So the clusters are walked in order and lazily, and opening a two-hour recording costs
/// its header and nothing per frame.
/// </remarks>
[FormatMimeType("video/x-matroska", "audio/x-matroska", "video/webm", "audio/webm")]
public sealed class MatroskaContainer : IVideoContainerReader<MatroskaContainer> {

  /// <summary>What the EBML header calls this document: <c>matroska</c> or <c>webm</c>.</summary>
  public required string? DocType { get; init; }

  /// <summary>The whole file, which every packet is a window onto.</summary>
  public required ReadOnlyMemory<byte> File { get; init; }

  /// <summary>Where the segment's children begin, counted from the start of the file.</summary>
  internal int SegmentStart { get; init; }

  /// <summary>Where the segment ends, counted from the start of the file.</summary>
  internal int SegmentEnd { get; init; }

  /// <summary>The nanoseconds one tick of every timestamp in this segment is worth.</summary>
  public required long TimestampScale { get; init; }

  /// <summary>Every track the document declares, in declaration order.</summary>
  /// <remarks>
  /// Internal because a track carries this reader's own timing bookkeeping beside what a caller
  /// wants of it, and what a caller wants of a track is its <see cref="MediaStreamInfo"/> —
  /// <see cref="Streams"/> is where those come out.
  /// </remarks>
  internal IReadOnlyList<MatroskaTrack> TrackEntries { get; init; } = [];

  /// <summary>What the document says about itself.</summary>
  public required VideoMetadata FileMetadata { get; init; }

  /// <summary>
  /// Which stream index a block's track number belongs to.
  /// </summary>
  /// <remarks>
  /// Not the same number. Track numbers start at one and a file may leave gaps in them — removing a
  /// track from a file leaves the others where they were — while a stream index is a position among
  /// the tracks that are there. Built once because a block carries the number and the walk needs the
  /// position for every frame of the film.
  /// </remarks>
  private Dictionary<ulong, int>? _byTrackNumber;

  private Dictionary<ulong, int> _TrackIndex {
    get {
      if (this._byTrackNumber != null)
        return this._byTrackNumber;

      var map = new Dictionary<ulong, int>(this.TrackEntries.Count);
      for (var i = 0; i < this.TrackEntries.Count; ++i)
        map.TryAdd(this.TrackEntries[i].Number, i);

      return this._byTrackNumber = map;
    }
  }

  // -------- Format identity --------

  public static string PrimaryExtension => ".mkv";

  /// <summary>
  /// Every name the one format goes under.
  /// </summary>
  /// <remarks>
  /// <c>.mka</c> and <c>.mks</c> are the same container carrying only sound or only subtitles, and
  /// <c>.mk3d</c> is one whose video track happens to be stereoscopic. None of them is a different
  /// format and a demuxer that refused them would refuse files it takes apart perfectly for the sake
  /// of a name. <c>.webm</c> is the same container again under a different <c>DocType</c>.
  /// </remarks>
  public static string[] FileExtensions => [".mkv", ".mka", ".mks", ".mk3d", ".webm"];

  /// <summary>
  /// A file that begins with the EBML magic.
  /// </summary>
  /// <remarks>
  /// Four bytes and no more. Every EBML document begins with them, and the <c>DocType</c> that says
  /// which document it is sits inside the header rather than at a fixed offset — so the signature
  /// claims EBML, and a document that turns out to be neither Matroska nor WebM is refused by name
  /// when it is opened. Nothing else registered here is EBML, so there is no contest to lose.
  /// </remarks>
  public static bool? MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 4 && header[0] == 0x1A && header[1] == 0x45 && header[2] == 0xDF && header[3] == 0xA3
      ? true
      : null;

  // -------- Demux --------

  public static MatroskaContainer FromSpan(ReadOnlySpan<byte> data) => MatroskaReader.FromSpan(data);

  /// <summary>Opens a document over the caller's array, keeping it rather than copying it.</summary>
  public static MatroskaContainer FromBytes(byte[] data) => MatroskaReader.FromBytes(data);

  public static MatroskaContainer FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Matroska file not found.", file.FullName);

    return MatroskaReader.FromBytes(System.IO.File.ReadAllBytes(file.FullName));
  }

  /// <summary>Every track the container declares — sound and subtitles as well as pictures.</summary>
  public static IReadOnlyList<MediaStreamInfo> Streams(MatroskaContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    var result = new MediaStreamInfo[container.TrackEntries.Count];
    for (var i = 0; i < result.Length; ++i)
      result[i] = container.TrackEntries[i].Info;

    return result;
  }

  /// <summary>What the container says about itself.</summary>
  public static VideoMetadata Metadata(MatroskaContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    return container.FileMetadata;
  }

  /// <summary>Walks every packet of every track, in the order the file stores them.</summary>
  /// <remarks>
  /// Storage order, which for a file with sound is the interleaving the writer chose rather than one
  /// whole track followed by another. Nothing has to be merged to recover it — unlike an ISO base
  /// media file, whose tracks are described separately and have to be put back together on their
  /// offsets, a Matroska cluster already holds the blocks of every track in the order they are due.
  /// <para/>
  /// Lazy and re-runnable: nothing of a cluster is touched until a packet is asked for, and each
  /// packet's data is a window onto the buffer the file was read into rather than a copy of it.
  /// </remarks>
  public static IEnumerable<CodedPacket> ReadPackets(MatroskaContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    return _Walk(container, null);
  }

  /// <summary>Walks the packets of one track, in storage order.</summary>
  /// <remarks>
  /// By filtering the walk rather than by seeking, because there is nothing to seek with. The
  /// <c>Cues</c> element indexes keyframes and the clusters they begin, not packets, so a walk of one
  /// track still has to open every cluster the other tracks share with it.
  /// </remarks>
  public static IEnumerable<CodedPacket> ReadPackets(MatroskaContainer container, int streamIndex) {
    ArgumentNullException.ThrowIfNull(container);

    if ((uint)streamIndex >= (uint)container.TrackEntries.Count)
      return [];

    return _Walk(container, streamIndex);
  }

  private static IEnumerable<CodedPacket> _Walk(MatroskaContainer container, int? onlyStream) {
    // Reused across the whole walk rather than allocated per block. A block of one frame is the
    // ordinary case and would otherwise cost a list a frame for the length of the film.
    var frames = new List<(int Offset, int Length)>();
    var packets = new List<CodedPacket>();

    foreach (var level1 in EbmlScanner.Walk(container.File, container.SegmentStart, container.SegmentEnd, MatroskaElementId.IsSegmentLevel)) {
      if (level1.Id != MatroskaElementId.CLUSTER)
        continue;

      // Every block's timestamp is an offset from this one, and the specification puts it first in
      // the cluster for exactly that reason. A cluster that stated it later would have its earlier
      // blocks timed from zero, which is the reading a single pass gives and the one no writer
      // produces.
      var clusterTimestamp = 0L;

      foreach (var child in EbmlScanner.Children(container.File, level1)) {
        switch (child.Id) {
          case MatroskaElementId.CLUSTER_TIMESTAMP:
            clusterTimestamp = (long)child.UnsignedValue();
            continue;

          case MatroskaElementId.SIMPLE_BLOCK:
            _ReadBlock(container, child, clusterTimestamp, null, null, frames, onlyStream, packets);
            break;

          case MatroskaElementId.BLOCK_GROUP: {
            EbmlElement? block = null;
            long? duration = null;
            var referenced = false;

            foreach (var member in EbmlScanner.Children(container.File, child))
              switch (member.Id) {
                case MatroskaElementId.BLOCK:
                  block ??= member;
                  break;
                case MatroskaElementId.BLOCK_DURATION:
                  duration ??= (long)member.UnsignedValue();
                  break;
                case MatroskaElementId.REFERENCE_BLOCK:
                  referenced = true;
                  break;
              }

            if (block == null)
              continue;

            // A Block carries no keyframe flag of its own — what makes it one is that its group names
            // no other block it depends on. ffprobe reports every Vorbis block of a file ffmpeg muxed
            // as a keyframe, and every one of them is a bare Block in a group with a BlockDuration and
            // no ReferenceBlock.
            _ReadBlock(container, block.Value, clusterTimestamp, duration, !referenced, frames, onlyStream, packets);
            break;
          }

          default:
            continue;
        }

        foreach (var packet in packets)
          yield return packet;
      }
    }
  }

  /// <summary>
  /// Turns one block into the packets it holds.
  /// </summary>
  /// <remarks>
  /// Not one packet. A laced block carries several frames behind one header, and reporting it as a
  /// single packet would hand a decoder the first frame with the rest stuck to the end of it.
  /// <para/>
  /// Behind a call rather than inside the walk because the block header and the lace tables are read
  /// out of spans, which an iterator cannot hold across a <c>yield</c>.
  /// </remarks>
  private static void _ReadBlock(
    MatroskaContainer container, EbmlElement element, long clusterTimestamp,
    long? statedDuration, bool? keyFrame,
    List<(int Offset, int Length)> frames, int? onlyStream, List<CodedPacket> packets) {
    packets.Clear();

    // A file that stops in the middle of a block holds part of a frame, and part of a frame is not a
    // frame. Handing it on with the bytes that happen to be there would present a truncated read as a
    // complete one, which is the failure a caller cannot see and cannot trace.
    if (element.IsTruncated)
      throw new InvalidDataException(
        $"The file ends inside the block at offset {element.Offset}, which states more bytes than are there — {element.Body.Length} of them were written.");

    var block = element.Body;
    if (!MatroskaBlock.TryReadHeader(block.Span, out var header))
      throw new InvalidDataException(
        $"A block of {block.Length} bytes at offset {element.Offset} is too short to state a track, a timestamp and its flags.");

    // A block naming a track the Tracks element never declared belongs to nothing. It is skipped
    // rather than refused, because that is what ffmpeg does with one and because the rest of the file
    // is perfectly readable without it.
    if (!container._TrackIndex.TryGetValue(header.TrackNumber, out var streamIndex))
      return;

    if (onlyStream != null && streamIndex != onlyStream)
      return;

    var track = container.TrackEntries[streamIndex];
    MatroskaBlock.ReadFrames(block.Span, header, frames);

    // The block's own moment: its cluster's timestamp plus its signed offset from it, less whatever
    // delay the codec builds in. All three are in the segment's ticks by now.
    var timestamp = clusterTimestamp + header.RelativeTimestamp - track.CodecDelayTicks;
    var laces = frames.Count;

    // How long the whole block occupies. Stated where the file states it, and otherwise as many frame
    // durations as the block holds frames — which is what ffprobe reports for a laced block of a
    // track whose DefaultDuration is 100 000 000 ns: four packets of 100 ticks each, not one.
    var duration = statedDuration
                   ?? (track.DefaultDurationNanoseconds > 0
                     ? track.DefaultDurationNanoseconds * laces / container.TimestampScale
                     : 0);

    for (var i = 0; i < laces; ++i) {
      var (offset, length) = frames[i];

      // Split by rounding the block's span at each boundary rather than by handing every frame the
      // same length. A duration that does not divide by the number of frames otherwise loses the
      // remainder, and the frames of a lace drift apart from the block after it.
      var start = duration * i / laces;
      var next = duration * (i + 1) / laces;

      packets.Add(new(
        streamIndex,
        block.Slice(offset, length),
        timestamp + start,
        // Matroska stores presentation timestamps and states nothing about decode order — a codec
        // that reorders frames keeps that in its own bitstream, and inventing a decode timestamp here
        // would be inventing an order the file does not describe. ffprobe reports the two equal for
        // every Matroska packet measured.
        timestamp + start,
        duration == 0 ? null : next - start,
        keyFrame ?? header.IsKeyFrame));
    }
  }
}
