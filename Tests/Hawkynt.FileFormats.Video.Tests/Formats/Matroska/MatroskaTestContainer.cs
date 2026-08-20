using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FileFormat.Matroska.Tests;

/// <summary>How a built block packs its frames.</summary>
/// <remarks>Public because a test case runs over it, and NUnit reaches those from outside.</remarks>
public enum TestLacing {
  None = 0,
  Xiph = 1,
  Fixed = 2,
  Ebml = 3,
}

/// <summary>One block to be written into a built cluster.</summary>
/// <remarks>
/// A block rather than a frame, because a block holding several frames is the case that separates a
/// reader that understands lacing from one that hands the first frame back with the rest stuck to
/// the end of it. ffmpeg's Matroska muxer writes no laced block at all, so every one of these was
/// assembled by hand and put past ffprobe before being written here.
/// </remarks>
internal sealed class MatroskaTestBlock {

  /// <summary>The number of the track this belongs to, which is not its stream index.</summary>
  public ulong Track { get; init; } = 1;

  /// <summary>How far this sits from its cluster's timestamp, in the segment's ticks; may be negative.</summary>
  public short Relative { get; init; }

  /// <summary>The frames the block holds; more than one needs a <see cref="Lacing"/>.</summary>
  public IReadOnlyList<byte[]> Frames { get; init; } = [];

  public TestLacing Lacing { get; init; } = TestLacing.None;

  /// <summary>Whether the <c>SimpleBlock</c> flag byte says decoding may begin here.</summary>
  /// <remarks>
  /// True by default because that is what ffmpeg writes for every block of an all-intra codec. It
  /// has no effect on a block written inside a group, which has no such flag.
  /// </remarks>
  public bool KeyFrame { get; init; } = true;

  /// <summary>Writes the block inside a <c>BlockGroup</c> rather than as a <c>SimpleBlock</c>.</summary>
  public bool InGroup { get; init; }

  /// <summary>A <c>BlockDuration</c> for the group, in the segment's ticks, or null for none.</summary>
  public long? BlockDuration { get; init; }

  /// <summary>Gives the group a <c>ReferenceBlock</c>, which is what makes a block not a keyframe.</summary>
  public bool Referenced { get; init; }

  /// <summary>Writes the block's own flag byte verbatim, whatever the other fields say.</summary>
  public byte? RawFlags { get; init; }

  /// <summary>Writes a lace size table that does not describe the frames, to be refused.</summary>
  public int[]? BrokenLaceSizes { get; init; }
}

/// <summary>One cluster of a built segment.</summary>
internal sealed class MatroskaTestCluster {

  public long Timestamp { get; init; }

  public IReadOnlyList<MatroskaTestBlock> Blocks { get; init; } = [];

  /// <summary>Writes the cluster with no length at all, the way a live muxer does.</summary>
  public bool UnknownSize { get; init; }

  /// <summary>Leaves the <c>Timestamp</c> element out entirely.</summary>
  public bool WithoutTimestamp { get; init; }
}

/// <summary>One <c>TrackEntry</c> of a built segment.</summary>
internal sealed class MatroskaTestTrack {

  public ulong Number { get; init; } = 1;

  /// <summary>1 is video, 2 is audio, 0x11 is subtitles.</summary>
  public int Type { get; init; } = 1;

  public string? CodecId { get; init; } = "V_MJPEG";

  public byte[]? CodecPrivate { get; init; }

  /// <summary>Nanoseconds one frame lasts, or zero to write no <c>DefaultDuration</c>.</summary>
  public long DefaultDuration { get; init; } = 100_000_000;

  /// <summary>Nanoseconds of <c>CodecDelay</c>, or zero to write none.</summary>
  public long CodecDelay { get; init; }

  /// <summary>The ISO 639-2 <c>Language</c>, or null to write none at all.</summary>
  public string? Language { get; init; } = "und";

  /// <summary>The RFC 5646 <c>LanguageBCP47</c>, which outranks the other where both are written.</summary>
  public string? LanguageBcp47 { get; init; }

  public string? Name { get; init; }

  public int Width { get; init; } = 8;

  public int Height { get; init; } = 4;

  /// <summary>Writes no <c>Video</c> element at all, as a sound or subtitle track has none.</summary>
  public bool WithoutVideo { get; init; }

  /// <summary>Writes a <c>ContentEncodings</c> declaring compression of this algorithm.</summary>
  public int? CompressionAlgorithm { get; init; }

  /// <summary>Writes a <c>ContentEncodings</c> declaring the blocks encrypted.</summary>
  public bool Encrypted { get; init; }

  /// <summary>Leaves <c>TrackNumber</c> out, which makes the entry unattributable.</summary>
  public bool WithoutNumber { get; init; }
}

/// <summary>
/// Builds Matroska documents byte by byte so the reader can be tested without a sample in the tree.
/// </summary>
/// <remarks>
/// The layout copied here is the one ffmpeg writes, read off a dump of its own output rather than off
/// the specification: an EBML header stating a <c>DocType</c>, then a segment holding <c>Info</c>,
/// <c>Tracks</c>, whatever tags there are, and the clusters. What ffmpeg will not produce is reachable
/// through the options above — a laced block, an element the writer stated no length for, a
/// four-byte-wide length where one byte would do — and each of those forms was assembled the same way
/// here, put past ffprobe, and only written as a test once ffprobe read the same packets out of it.
/// <para/>
/// Nothing here is a valid picture unless a test makes it one. The frames are whatever bytes a test
/// hands over, which is all a demuxer needs: what is being tested is where the packets are and when
/// they are due.
/// </remarks>
internal static class MatroskaTestContainer {

  /// <summary>The <c>TimestampScale</c> <see cref="Build"/> states: one millisecond a tick.</summary>
  public const long TIMESTAMP_SCALE = 1_000_000L;

  /// <summary>Nanoseconds from 2001-01-01 UTC, which is what <c>DateUTC</c> counts.</summary>
  public const long EPOCH_OFFSET_SECONDS = 978_307_200L;

  /// <summary>Assembles a document around the given tracks and clusters.</summary>
  /// <param name="tracks">One per <c>TrackEntry</c>, in the order they are to be declared.</param>
  /// <param name="clusters">The clusters, in the order they are to be stored.</param>
  /// <param name="docType">The EBML header's <c>DocType</c>.</param>
  /// <param name="timestampScale">Nanoseconds a tick, or zero to write no <c>TimestampScale</c>.</param>
  /// <param name="duration">The <c>Duration</c>, counted in ticks, or null for none.</param>
  /// <param name="title">The <c>Title</c> element of <c>Info</c>.</param>
  /// <param name="muxingApp">The <c>MuxingApp</c> element.</param>
  /// <param name="writingApp">The <c>WritingApp</c> element.</param>
  /// <param name="dateUtc">The <c>DateUTC</c>, in nanoseconds from 2001-01-01 UTC.</param>
  /// <param name="tags">Global tags as (name, value); they describe the whole file.</param>
  /// <param name="trackTags">Tags aimed at a track UID, which describe that track and not the file.</param>
  /// <param name="attachments">Attached files as (name, media type, description, bytes).</param>
  /// <param name="unknownSizeSegment">Writes the segment with no length, as a live muxer does.</param>
  /// <param name="withoutInfo">Leaves the whole <c>Info</c> element out.</param>
  /// <param name="withoutTracks">Leaves the whole <c>Tracks</c> element out.</param>
  /// <param name="sizeWidth">Writes every length this many bytes wide, to exercise the encoding.</param>
  /// <param name="padding">Writes a <c>Void</c> of this many bytes among the segment's children.</param>
  public static byte[] Build(
    IReadOnlyList<MatroskaTestTrack> tracks,
    IReadOnlyList<MatroskaTestCluster>? clusters = null,
    string docType = "matroska",
    long timestampScale = TIMESTAMP_SCALE,
    double? duration = null,
    string? title = null,
    string? muxingApp = null,
    string? writingApp = null,
    long? dateUtc = null,
    IReadOnlyList<(string Name, string Value)>? tags = null,
    IReadOnlyList<(ulong Track, string Name, string Value)>? trackTags = null,
    IReadOnlyList<(string Name, string Mime, string? Description, byte[] Data)>? attachments = null,
    bool unknownSizeSegment = false,
    bool withoutInfo = false,
    bool withoutTracks = false,
    int sizeWidth = 0,
    int padding = 0) {
    ArgumentNullException.ThrowIfNull(tracks);

    var body = new MemoryStream();

    if (!withoutInfo)
      body.Write(_Info(timestampScale, duration, title, muxingApp, writingApp, dateUtc, sizeWidth));

    if (padding > 0)
      body.Write(_Element(0xEC, new byte[padding], sizeWidth));

    if (!withoutTracks) {
      var entries = new MemoryStream();
      foreach (var track in tracks)
        entries.Write(_Track(track, sizeWidth));

      body.Write(_Element(0x1654AE6B, entries.ToArray(), sizeWidth));
    }

    if (attachments is { Count: > 0 }) {
      var files = new MemoryStream();
      foreach (var (name, mime, description, data) in attachments) {
        var file = new MemoryStream();
        if (description != null)
          file.Write(_Element(0x467E, _Text(description), sizeWidth));

        file.Write(_Element(0x466E, _Text(name), sizeWidth));
        file.Write(_Element(0x4660, _Text(mime), sizeWidth));
        file.Write(_Element(0x465C, data, sizeWidth));
        files.Write(_Element(0x61A7, file.ToArray(), sizeWidth));
      }

      body.Write(_Element(0x1941A469, files.ToArray(), sizeWidth));
    }

    if (tags != null || trackTags != null)
      body.Write(_Tags(tags, trackTags, sizeWidth));

    foreach (var cluster in clusters ?? [])
      body.Write(_Cluster(cluster, sizeWidth));

    var document = new MemoryStream();
    document.Write(_Header(docType, sizeWidth));
    document.Write(unknownSizeSegment
      ? _UnknownSizeElement(0x18538067, body.ToArray())
      : _Element(0x18538067, body.ToArray(), sizeWidth));

    return document.ToArray();
  }

  /// <summary>Convenience for the ordinary case: one video track and one cluster of single frames.</summary>
  public static byte[] Build(string codecId, IReadOnlyList<byte[]> frames, long firstTimestamp = 0) {
    var blocks = new MatroskaTestBlock[frames.Count];
    for (var i = 0; i < frames.Count; ++i)
      blocks[i] = new() { Relative = (short)(firstTimestamp + (i * 100)), Frames = [frames[i]] };

    return Build(
      [new MatroskaTestTrack { CodecId = codecId }],
      [new MatroskaTestCluster { Blocks = blocks }]);
  }

  // ------------------------------------------------------------------------------------------
  // Document
  // ------------------------------------------------------------------------------------------

  private static byte[] _Header(string docType, int sizeWidth) {
    var body = new MemoryStream();
    body.Write(_Element(0x4286, _Unsigned(1), sizeWidth));
    body.Write(_Element(0x42F7, _Unsigned(1), sizeWidth));
    body.Write(_Element(0x42F2, _Unsigned(4), sizeWidth));
    body.Write(_Element(0x42F3, _Unsigned(8), sizeWidth));
    body.Write(_Element(0x4282, _Text(docType), sizeWidth));
    body.Write(_Element(0x4287, _Unsigned(4), sizeWidth));
    body.Write(_Element(0x4285, _Unsigned(2), sizeWidth));
    return _Element(0x1A45DFA3, body.ToArray(), sizeWidth);
  }

  private static byte[] _Info(
    long timestampScale, double? duration, string? title, string? muxingApp, string? writingApp, long? dateUtc, int sizeWidth) {
    var body = new MemoryStream();
    if (timestampScale > 0)
      body.Write(_Element(0x2AD7B1, _Unsigned((ulong)timestampScale), sizeWidth));

    if (muxingApp != null)
      body.Write(_Element(0x4D80, _Text(muxingApp), sizeWidth));

    if (writingApp != null)
      body.Write(_Element(0x5741, _Text(writingApp), sizeWidth));

    if (title != null)
      body.Write(_Element(0x7BA9, _Text(title), sizeWidth));

    if (dateUtc != null)
      body.Write(_Element(0x4461, _Signed(dateUtc.Value), sizeWidth));

    if (duration != null) {
      var bytes = new byte[8];
      BinaryPrimitives.WriteInt64BigEndian(bytes, BitConverter.DoubleToInt64Bits(duration.Value));
      body.Write(_Element(0x4489, bytes, sizeWidth));
    }

    return _Element(0x1549A966, body.ToArray(), sizeWidth);
  }

  private static byte[] _Track(MatroskaTestTrack track, int sizeWidth) {
    var body = new MemoryStream();
    if (!track.WithoutNumber)
      body.Write(_Element(0xD7, _Unsigned(track.Number), sizeWidth));

    body.Write(_Element(0x73C5, _Unsigned(track.Number * 7919), sizeWidth));
    body.Write(_Element(0x83, _Unsigned((ulong)track.Type), sizeWidth));

    if (track.CodecId != null)
      body.Write(_Element(0x86, _Text(track.CodecId), sizeWidth));

    if (track.Language != null)
      body.Write(_Element(0x22B59C, _Text(track.Language), sizeWidth));

    if (track.LanguageBcp47 != null)
      body.Write(_Element(0x22B59D, _Text(track.LanguageBcp47), sizeWidth));

    if (track.Name != null)
      body.Write(_Element(0x536E, _Text(track.Name), sizeWidth));

    if (track.DefaultDuration > 0)
      body.Write(_Element(0x23E383, _Unsigned((ulong)track.DefaultDuration), sizeWidth));

    if (track.CodecDelay > 0)
      body.Write(_Element(0x56AA, _Unsigned((ulong)track.CodecDelay), sizeWidth));

    if (!track.WithoutVideo) {
      var video = new MemoryStream();
      video.Write(_Element(0xB0, _Unsigned((ulong)track.Width), sizeWidth));
      video.Write(_Element(0xBA, _Unsigned((ulong)track.Height), sizeWidth));
      body.Write(_Element(0xE0, video.ToArray(), sizeWidth));
    }

    if (track.CodecPrivate != null)
      body.Write(_Element(0x63A2, track.CodecPrivate, sizeWidth));

    if (track.CompressionAlgorithm != null) {
      var compression = _Element(0x4254, _Unsigned((ulong)track.CompressionAlgorithm.Value), sizeWidth);
      body.Write(_Element(0x6D80,
        _Element(0x6240, _Element(0x5034, compression, sizeWidth), sizeWidth), sizeWidth));
    }

    if (track.Encrypted)
      body.Write(_Element(0x6D80,
        _Element(0x6240, _Element(0x5035, [], sizeWidth), sizeWidth), sizeWidth));

    return _Element(0xAE, body.ToArray(), sizeWidth);
  }

  private static byte[] _Tags(
    IReadOnlyList<(string Name, string Value)>? tags,
    IReadOnlyList<(ulong Track, string Name, string Value)>? trackTags,
    int sizeWidth) {
    var body = new MemoryStream();

    if (tags is { Count: > 0 }) {
      var simple = new MemoryStream();
      simple.Write(_Element(0x63C0, [], sizeWidth));
      foreach (var (name, value) in tags) {
        var entry = new MemoryStream();
        entry.Write(_Element(0x45A3, _Text(name), sizeWidth));
        entry.Write(_Element(0x4487, _Text(value), sizeWidth));
        simple.Write(_Element(0x67C8, entry.ToArray(), sizeWidth));
      }

      body.Write(_Element(0x7373, simple.ToArray(), sizeWidth));
    }

    foreach (var (track, name, value) in trackTags ?? []) {
      var entry = new MemoryStream();
      entry.Write(_Element(0x45A3, _Text(name), sizeWidth));
      entry.Write(_Element(0x4487, _Text(value), sizeWidth));

      var tag = new MemoryStream();
      tag.Write(_Element(0x63C0, _Element(0x63C5, _Unsigned(track * 7919), sizeWidth), sizeWidth));
      tag.Write(_Element(0x67C8, entry.ToArray(), sizeWidth));
      body.Write(_Element(0x7373, tag.ToArray(), sizeWidth));
    }

    return _Element(0x1254C367, body.ToArray(), sizeWidth);
  }

  private static byte[] _Cluster(MatroskaTestCluster cluster, int sizeWidth) {
    var body = new MemoryStream();
    if (!cluster.WithoutTimestamp)
      body.Write(_Element(0xE7, _Unsigned((ulong)cluster.Timestamp), sizeWidth));

    foreach (var block in cluster.Blocks)
      body.Write(_Block(block, sizeWidth));

    return cluster.UnknownSize
      ? _UnknownSizeElement(0x1F43B675, body.ToArray())
      : _Element(0x1F43B675, body.ToArray(), sizeWidth);
  }

  private static byte[] _Block(MatroskaTestBlock block, int sizeWidth) {
    var payload = new MemoryStream();
    payload.Write(_Vint(block.Track, 0));
    payload.WriteByte((byte)(block.Relative >> 8));
    payload.WriteByte((byte)block.Relative);
    payload.WriteByte(block.RawFlags
                      ?? (byte)(((int)block.Lacing << 1) | (!block.InGroup && block.KeyFrame ? 0x80 : 0)));

    if (block.Lacing == TestLacing.None)
      payload.Write(block.Frames[0]);
    else {
      payload.WriteByte((byte)(block.Frames.Count - 1));

      var sizes = block.BrokenLaceSizes;
      if (sizes == null) {
        sizes = new int[block.Frames.Count - 1];
        for (var i = 0; i < sizes.Length; ++i)
          sizes[i] = block.Frames[i].Length;
      }

      switch (block.Lacing) {
        case TestLacing.Xiph:
          foreach (var size in sizes) {
            var left = size;
            while (left >= 0xFF) {
              payload.WriteByte(0xFF);
              left -= 0xFF;
            }

            payload.WriteByte((byte)left);
          }

          break;
        case TestLacing.Ebml:
          if (sizes.Length > 0) {
            payload.Write(_Vint((ulong)sizes[0], 0));
            for (var i = 1; i < sizes.Length; ++i)
              payload.Write(_SignedVint(sizes[i] - sizes[i - 1]));
          }

          break;
        // Fixed lacing stores no sizes at all.
      }

      foreach (var frame in block.Frames)
        payload.Write(frame);
    }

    if (!block.InGroup)
      return _Element(0xA3, payload.ToArray(), sizeWidth);

    var group = new MemoryStream();
    group.Write(_Element(0xA1, payload.ToArray(), sizeWidth));
    if (block.BlockDuration != null)
      group.Write(_Element(0x9B, _Unsigned((ulong)block.BlockDuration.Value), sizeWidth));

    if (block.Referenced)
      group.Write(_Element(0xFB, _Signed(-100), sizeWidth));

    return _Element(0xA0, group.ToArray(), sizeWidth);
  }

  // ------------------------------------------------------------------------------------------
  // EBML plumbing
  // ------------------------------------------------------------------------------------------

  /// <summary>An element: its identifier as stored, its length, and its payload.</summary>
  /// <param name="sizeWidth">How many bytes the length is to occupy, or 0 for the shortest that fits.</param>
  private static byte[] _Element(uint id, byte[] payload, int sizeWidth) {
    var element = new MemoryStream();
    element.Write(_Id(id));
    element.Write(_Vint((ulong)payload.Length, sizeWidth));
    element.Write(payload);
    return element.ToArray();
  }

  /// <summary>An element whose length is every value bit set, which means the writer did not know it.</summary>
  private static byte[] _UnknownSizeElement(uint id, byte[] payload) {
    var element = new MemoryStream();
    element.Write(_Id(id));
    element.WriteByte(0xFF);
    element.Write(payload);
    return element.ToArray();
  }

  /// <summary>An identifier, which is written exactly as its bytes stand — marker bit included.</summary>
  private static byte[] _Id(uint id) {
    if (id > 0x00FFFFFF)
      return [(byte)(id >> 24), (byte)(id >> 16), (byte)(id >> 8), (byte)id];
    if (id > 0x0000FFFF)
      return [(byte)(id >> 16), (byte)(id >> 8), (byte)id];
    if (id > 0x000000FF)
      return [(byte)(id >> 8), (byte)id];

    return [(byte)id];
  }

  /// <summary>A length, whose marker bit says how many bytes it occupies and is not part of the value.</summary>
  private static byte[] _Vint(ulong value, int width) {
    var length = Math.Max(1, width);
    if (width == 0)
      while (length < 8 && value >= (1UL << (7 * length)) - 1)
        ++length;

    var bytes = new byte[length];
    for (var i = length - 1; i >= 0; --i) {
      bytes[i] = (byte)value;
      value >>= 8;
    }

    bytes[0] |= (byte)(0x100 >> length);
    return bytes;
  }

  /// <summary>A length biased into a signed number, which is how EBML lacing stores its differences.</summary>
  private static byte[] _SignedVint(long value) {
    var length = 1;
    while (length < 8 && (value < -((1L << ((7 * length) - 1)) - 1) || value > (1L << ((7 * length) - 1)) - 1))
      ++length;

    return _Vint((ulong)(value + ((1L << ((7 * length) - 1)) - 1)), length);
  }

  /// <summary>An unsigned integer, in as few bytes as carry it — which is how EBML stores one.</summary>
  private static byte[] _Unsigned(ulong value) {
    if (value == 0)
      return [0];

    var length = 0;
    for (var probe = value; probe != 0; probe >>= 8)
      ++length;

    var bytes = new byte[length];
    for (var i = length - 1; i >= 0; --i) {
      bytes[i] = (byte)value;
      value >>= 8;
    }

    return bytes;
  }

  private static byte[] _Signed(long value) {
    // Eight bytes hold every long there is, and the test at that width would compare against a shift
    // that has already run off the top of the type — which is a loop that never ends rather than a
    // wrong answer.
    var length = 1;
    while (length < 8 && (value < -(1L << ((8 * length) - 1)) || value >= 1L << ((8 * length) - 1)))
      ++length;

    var bytes = new byte[length];
    for (var i = length - 1; i >= 0; --i) {
      bytes[i] = (byte)value;
      value >>= 8;
    }

    return bytes;
  }

  private static byte[] _Text(string value) => Encoding.UTF8.GetBytes(value);

  /// <summary>A <c>BITMAPINFOHEADER</c>, which is what a <c>V_MS/VFW/FOURCC</c> track carries.</summary>
  public static byte[] BitmapInfoHeader(string fourCharacterCode, int width, int height, int bitsPerPixel) {
    ArgumentNullException.ThrowIfNull(fourCharacterCode);

    var header = new byte[40];
    BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(0), 40);
    BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), width);
    BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8), height);
    BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(12), 1);
    BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(14), (short)bitsPerPixel);
    for (var i = 0; i < 4; ++i)
      header[16 + i] = (byte)(i < fourCharacterCode.Length ? fourCharacterCode[i] : 0);

    BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(20), ((((width * bitsPerPixel) + 7) / 8) + 3) / 4 * 4 * Math.Abs(height));
    return header;
  }
}
