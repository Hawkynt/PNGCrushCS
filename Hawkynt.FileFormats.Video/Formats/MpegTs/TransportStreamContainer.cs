using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.MpegTs;

/// <summary>
/// An MPEG-2 transport stream taken apart into the streams its tables declare and the coded units its
/// packets are cut into — and nothing else.
/// </summary>
/// <remarks>
/// This container knows where every packet is and not one thing about what is inside any of them. It
/// does not decode, it does not report pictures, and it does not refuse a file for naming a coding
/// nothing here reads: a transport stream full of H.265 is a perfectly good transport stream, and
/// copying its packets into another container needs no decoder at all. Refusal by code still happens,
/// at the moment a decoder is asked for.
/// <para/>
/// What makes this container different from the other four is that it was not designed for a file. An
/// AVI, an FLV and an ISO base media file are all written front to back by something that knows what
/// it is writing; a transport stream is a broadcast, and a receiver is expected to be switched on
/// half way through it and to find its way from what repeats. So there is no index, no directory and
/// no header — the streams are found by reading the tables the multiplex repeats, and a packet's
/// boundaries are found by reassembling it out of the 188-byte pieces it was cut into.
/// <para/>
/// It is the one container here whose packets are copies rather than windows onto the file. There is
/// no choice about it: a coded unit is spread across as many transport packets as it needs and a
/// four-byte header sits between every pair of them, so no run of bytes in the file is the unit and
/// nothing else.
/// </remarks>
[FormatMimeType("video/mp2t", "video/mpeg", "audio/mp2t")]
public sealed class TransportStreamContainer : IVideoContainerReader<TransportStreamContainer> {

  /// <summary>The whole file, which every packet is assembled out of.</summary>
  public required ReadOnlyMemory<byte> File { get; init; }

  /// <summary>
  /// The distance from one packet to the next: 188, or 192 for a file whose packets carry an arrival
  /// timecode in front of them.
  /// </summary>
  internal int PacketStride { get; init; } = TransportPacketScanner.PACKET_SIZE;

  /// <summary>Where the first packet begins — nought, or four in a file with arrival timecodes.</summary>
  internal int FirstPacketOffset { get; init; }

  /// <summary>Every stream the program maps declare, in the order they declare them.</summary>
  public required IReadOnlyList<MediaStreamInfo> StreamInfos { get; init; }

  /// <summary>Which stream each elementary PID is.</summary>
  /// <remarks>
  /// Internal because a PID is this format's own numbering and no caller has a use for it: what a
  /// caller wants is a stream index, which is what <see cref="MediaStreamInfo.Index"/> already is.
  /// </remarks>
  internal IReadOnlyDictionary<int, int> StreamByPid { get; init; } = new Dictionary<int, int>();

  /// <summary>What the multiplex says about itself.</summary>
  public required VideoMetadata FileMetadata { get; init; }

  // -------- Format identity --------

  public static string PrimaryExtension => ".ts";

  /// <summary>
  /// The names a transport stream is stored under.
  /// </summary>
  /// <remarks>
  /// <c>.m2ts</c> and <c>.mts</c> are the Blu-ray and AVCHD spellings, which differ from a plain
  /// <c>.ts</c> only in the four-byte arrival timecode in front of every packet — the same format
  /// with a wider stride, which is measured rather than taken from the name. <c>.m2t</c> and
  /// <c>.tsv</c> are the same thing again under names some recorders use.
  /// </remarks>
  public static string[] FileExtensions => [".ts", ".m2ts", ".mts", ".m2t", ".tsv"];

  /// <summary>
  /// A file whose packets begin with the sync byte at one of the two strides.
  /// </summary>
  /// <remarks>
  /// Four packets in a row rather than one, because one is not evidence: the sync byte is the letter
  /// <c>G</c>, and a GIF begins with it. Four puts a coincidence at one in sixteen million and is
  /// decided from the first 752 bytes.
  /// <para/>
  /// The stride is part of the signature rather than something read afterwards, because the two
  /// framings are not distinguishable at a single byte: a Blu-ray file's first sync byte is at offset
  /// four, behind the arrival timecode of its first packet, and there is nothing at offset zero at all.
  /// </remarks>
  public static bool? MatchesSignature(ReadOnlySpan<byte> header)
    => _Framed(header, 188, 0) || _Framed(header, 192, 4) ? true : null;

  private static bool _Framed(ReadOnlySpan<byte> header, int stride, int offset) {
    const int _WANTED = 4;

    if (header.Length < offset + (_WANTED - 1) * stride + 1)
      return false;

    for (var i = 0; i < _WANTED; ++i)
      if (header[offset + i * stride] != TransportPacketScanner.SYNC_BYTE)
        return false;

    return true;
  }

  // -------- Demux --------

  public static TransportStreamContainer FromSpan(ReadOnlySpan<byte> data) => TransportStreamReader.FromSpan(data);

  /// <summary>Opens a file over the caller's array, keeping it rather than copying it.</summary>
  public static TransportStreamContainer FromBytes(byte[] data) => TransportStreamReader.FromBytes(data);

  public static TransportStreamContainer FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Transport stream file not found.", file.FullName);

    return TransportStreamReader.FromBytes(System.IO.File.ReadAllBytes(file.FullName));
  }

  /// <summary>Every stream the program maps declare — the sound and the subtitles as well as the pictures.</summary>
  public static IReadOnlyList<MediaStreamInfo> Streams(TransportStreamContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    return container.StreamInfos;
  }

  /// <summary>What the multiplex says about itself.</summary>
  public static VideoMetadata Metadata(TransportStreamContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    return container.FileMetadata;
  }

  /// <summary>Walks every coded unit of the file, in the order they are finished.</summary>
  /// <remarks>
  /// Finished rather than begun, which for this format is not the same order. A unit whose length was
  /// never stated — which is every picture, because a coded picture has no length until it has been
  /// coded — ends where the next one on its PID begins, so it can only be handed over once that one
  /// has arrived. A file with sound therefore alternates in a way that looks odd and is right:
  /// ffprobe reports the same order for the same file.
  /// <para/>
  /// Lazy and re-runnable. Nothing is touched until a packet is asked for, and the assembly holds one
  /// unit per stream rather than a list of them — so a two-hour recording enumerated for its first
  /// frame costs one frame per stream.
  /// </remarks>
  public static IEnumerable<CodedPacket> ReadPackets(TransportStreamContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    return TransportStreamReader.Walk(container, null);
  }

  /// <summary>Walks the coded units of one stream, in the order they are finished.</summary>
  /// <remarks>
  /// By filtering the full walk rather than by seeking, because there is nothing to seek to: a
  /// transport stream has no index, one stream's packets are interleaved with every other's, and the
  /// continuity counters that say whether anything is missing have to be followed through the whole
  /// multiplex either way. Filtering also keeps the refusals the same in both walks, which matters —
  /// a file that refuses when read whole should not read cleanly when read a stream at a time.
  /// </remarks>
  public static IEnumerable<CodedPacket> ReadPackets(TransportStreamContainer container, int streamIndex) {
    ArgumentNullException.ThrowIfNull(container);

    return TransportStreamReader.Walk(container, streamIndex);
  }
}
