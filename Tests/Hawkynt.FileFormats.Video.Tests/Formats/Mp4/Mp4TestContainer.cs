using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace FileFormat.Mp4.Tests;

/// <summary>One track to be written into a built container.</summary>
/// <remarks>
/// Every table form the reader has to take is reachable from here, because most of them are ones
/// ffmpeg will not produce for a file small enough to check by hand: <c>co64</c> needs a file past
/// four gigabytes, <c>stz2</c> is written by no muxer this was measured against, and a fixed sample
/// size needs samples that all happen to be the same length. The two that ffmpeg does write —
/// <c>stco</c> with <c>stsz</c> — are the defaults, so a track described with nothing but its samples
/// is the shape a real file has.
/// </remarks>
internal sealed class Mp4TestTrack {

  /// <summary>The <c>hdlr</c> handler type; anything but <c>vide</c> makes the track a non-video one.</summary>
  public string Handler { get; init; } = "vide";

  /// <summary>The four-character type of the <c>stsd</c> sample entry, which is the codec's code.</summary>
  public string Codec { get; init; } = "jpeg";

  public int Width { get; init; } = 8;
  public int Height { get; init; } = 4;

  /// <summary>The <c>biBitCount</c> equivalent — a visual sample entry's depth field.</summary>
  public int Depth { get; init; } = 24;

  /// <summary>The <c>mdhd</c> time scale: how many units of this track's clock make a second.</summary>
  public int Timescale { get; init; } = 10;

  /// <summary>The packed <c>mdhd</c> language field; 0 and 0x7FFF both mean "unstated".</summary>
  public int Language { get; init; }

  /// <summary>The <c>trak/udta/name</c> atom, or null for none.</summary>
  public string? Name { get; init; }

  /// <summary>One payload per sample.</summary>
  public IReadOnlyList<byte[]> Samples { get; init; } = [];

  /// <summary>How long each sample lasts, in the track's own time scale.</summary>
  public int SampleDuration { get; init; } = 1;

  /// <summary>How many samples go in each chunk; the last chunk takes what is left.</summary>
  public int SamplesPerChunk { get; init; } = 1;

  /// <summary>
  /// A <c>stsc</c> of several runs as (one-based first chunk, samples in each chunk from there on),
  /// or null to write the single run <see cref="SamplesPerChunk"/> describes.
  /// </summary>
  /// <remarks>
  /// The shape ffmpeg writes for a track whose chunks are not all the same: the AAC track of an MP4
  /// it muxed came out with five entries. A reader that took the first run for the whole table would
  /// read that file's every packet from the wrong offset.
  /// </remarks>
  public (int FirstChunk, int SamplesPerChunk)[]? ChunkRuns { get; init; }

  /// <summary>One-based sample numbers for <c>stss</c>, or null to write no <c>stss</c> at all.</summary>
  public int[]? SyncSamples { get; init; }

  /// <summary>One display offset per sample for <c>ctts</c>, or null to write no <c>ctts</c>.</summary>
  public int[]? CompositionOffsets { get; init; }

  /// <summary>Writes <c>co64</c> rather than <c>stco</c>.</summary>
  public bool ChunkOffsets64 { get; init; }

  /// <summary>Writes <c>stz2</c> with this field width rather than <c>stsz</c>; 0 writes <c>stsz</c>.</summary>
  public int CompactSizeBits { get; init; }

  /// <summary>Writes one size in the <c>stsz</c> header rather than a table; the samples must all be that long.</summary>
  public bool FixedSampleSize { get; init; }

  /// <summary>An <c>elst</c> entry as (segment duration in movie units, media time), or null for no <c>edts</c>.</summary>
  public (long Duration, long MediaTime)[]? Edits { get; init; }
}

/// <summary>
/// Builds ISO base media files byte by byte so the reader can be tested without a sample in the tree.
/// </summary>
/// <remarks>
/// The layout copied here is the one ffmpeg writes, read off a hexdump of its own output rather than
/// off the specification: <c>ftyp</c>, then <c>mdat</c>, then <c>moov</c> — which is the order a
/// writer producing a file in one pass ends up with, and the order both of the files this reader was
/// measured against are in. <see cref="Build"/> can put <c>moov</c> first instead, because a file
/// prepared for streaming has it there and the two have to read alike.
/// <para/>
/// Nothing here is a valid picture. The samples are whatever bytes a test hands over, which is all a
/// demuxer needs: the point of these files is where the packets are and what the tables say about
/// them, and the one test that needs real pictures builds real JPEGs to put in them.
/// </remarks>
internal static class Mp4TestContainer {

  public const int FRAME_WIDTH = 8;
  public const int FRAME_HEIGHT = 4;

  /// <summary>The movie time scale <see cref="Build"/> states, in units a second.</summary>
  public const int MOVIE_TIMESCALE = 1000;

  /// <summary>Seconds between 1904-01-01 UTC, which MP4 counts from, and the Unix epoch.</summary>
  public const long EPOCH_OFFSET = 2_082_844_800L;

  /// <summary>Assembles a file around the given tracks.</summary>
  /// <param name="tracks">One per <c>trak</c>, in the order they are to be declared.</param>
  /// <param name="movieDuration">The <c>mvhd</c> duration, in <see cref="MOVIE_TIMESCALE"/> units.</param>
  /// <param name="creationTime">The <c>mvhd</c> creation time, in seconds since 1904; 0 means unstated.</param>
  /// <param name="tags">iTunes-style <c>udta/meta/ilst</c> entries as (four-character name, text).</param>
  /// <param name="quickTimeTags">QuickTime-style <c>udta</c> text atoms as (four-character name, text).</param>
  /// <param name="cover">A <c>covr</c> payload written into the <c>ilst</c>, or null for none.</param>
  /// <param name="movieFirst">Writes <c>moov</c> before <c>mdat</c> rather than after it.</param>
  /// <param name="wideMovieBox">Gives <c>moov</c> a 64-bit size rather than a 32-bit one.</param>
  /// <param name="fragment">Appends an empty <c>moof</c>, which makes the file a fragmented one.</param>
  /// <param name="brand">The <c>ftyp</c> major brand.</param>
  /// <param name="compressMovieHeader">
  /// Writes the whole movie atom as a classic QuickTime <c>cmov</c> — a <c>moov</c> whose only child is
  /// <c>dcom</c> naming <c>zlib</c> and <c>cmvd</c> holding the real <c>moov</c>, inflated size first,
  /// deflated after — rather than the plain, uncompressed atom. Combining this with
  /// <paramref name="movieFirst"/> is not supported: deflating changes length with the bytes it is fed,
  /// so the two-pass trick <see cref="Build"/> uses to place <c>moov</c> before <c>mdat</c> cannot also
  /// predict a compressed length before the real chunk offsets are known.
  /// </param>
  public static byte[] Build(
    IReadOnlyList<Mp4TestTrack> tracks,
    long movieDuration = 500,
    long creationTime = 0,
    IReadOnlyList<(string Name, string Text)>? tags = null,
    IReadOnlyList<(string Name, string Text)>? quickTimeTags = null,
    byte[]? cover = null,
    bool movieFirst = false,
    bool wideMovieBox = false,
    bool fragment = false,
    string brand = "isom",
    bool compressMovieHeader = false) {
    ArgumentNullException.ThrowIfNull(tracks);
    if (compressMovieHeader && movieFirst)
      throw new ArgumentException("A compressed movie header cannot also be placed before mdat by this builder.", nameof(compressMovieHeader));

    var fileType = _Box("ftyp", _Ascii(brand), _UInt32(512), _Ascii(brand));

    // Chunks are laid out round robin across the tracks, which is the interleaving a writer produces
    // and the order the packets are due in. A file whose tracks were written one after the other
    // would never exercise the merge that puts them back in order.
    var chunks = _LayOutChunks(tracks, out var chunkPlan);

    // The chunk offsets are absolute, so where mdat lands has to be known before the tables can be
    // written — and when moov comes first, where mdat lands depends on how long moov is. The tables
    // are a fixed width whatever the offsets in them, so building moov once with zeroes gives its
    // length and building it again with the real offsets gives the same length.
    var mediaStart = movieFirst
      ? fileType.Length + _Movie(tracks, chunkPlan, 0, movieDuration, creationTime, tags, quickTimeTags, cover, wideMovieBox).Length + 8
      : fileType.Length + 8;

    var movie = _Movie(tracks, chunkPlan, mediaStart, movieDuration, creationTime, tags, quickTimeTags, cover, wideMovieBox);
    if (compressMovieHeader)
      movie = _CompressMovie(movie);

    var media = _Box("mdat", chunks);

    var file = new MemoryStream();
    file.Write(fileType);
    if (movieFirst) {
      file.Write(movie);
      file.Write(media);
    } else {
      file.Write(media);
      file.Write(movie);
    }

    if (fragment)
      file.Write(_Box("moof"));

    return file.ToArray();
  }

  /// <summary>Convenience for the ordinary case: one video track of the given samples.</summary>
  public static byte[] Build(string codec, int width, int height, IReadOnlyList<byte[]> samples)
    => Build([new Mp4TestTrack { Codec = codec, Width = width, Height = height, Samples = samples }]);

  // ------------------------------------------------------------------------------------------
  // Layout
  // ------------------------------------------------------------------------------------------

  /// <summary>Where each chunk of each track goes inside <c>mdat</c>, and the bytes that fill it.</summary>
  private static byte[] _LayOutChunks(IReadOnlyList<Mp4TestTrack> tracks, out long[][] chunkPlan) {
    var grouped = new List<byte[]>[tracks.Count];
    for (var t = 0; t < tracks.Count; ++t) {
      grouped[t] = [];
      var track = tracks[t];
      var taken = 0;
      var chunkNumber = 1;
      while (taken < track.Samples.Count) {
        var perChunk = Math.Max(1, _SamplesInChunk(track, chunkNumber));
        var chunk = new MemoryStream();
        for (var j = taken; j < Math.Min(taken + perChunk, track.Samples.Count); ++j)
          chunk.Write(track.Samples[j]);

        grouped[t].Add(chunk.ToArray());
        taken += perChunk;
        ++chunkNumber;
      }
    }

    var offsets = new List<long>[tracks.Count];
    for (var t = 0; t < tracks.Count; ++t)
      offsets[t] = [];

    var body = new MemoryStream();
    var most = 0;
    foreach (var track in grouped)
      most = Math.Max(most, track.Count);

    for (var chunk = 0; chunk < most; ++chunk)
      for (var t = 0; t < tracks.Count; ++t) {
        if (chunk >= grouped[t].Count)
          continue;

        offsets[t].Add(body.Length);
        body.Write(grouped[t][chunk]);
      }

    chunkPlan = new long[tracks.Count][];
    for (var t = 0; t < tracks.Count; ++t)
      chunkPlan[t] = offsets[t].ToArray();

    return body.ToArray();
  }

  /// <summary>How many samples the track's <c>stsc</c> puts in a given one-based chunk.</summary>
  private static int _SamplesInChunk(Mp4TestTrack track, int chunkNumber) {
    if (track.ChunkRuns is not { Length: > 0 } runs)
      return track.SamplesPerChunk;

    var result = runs[0].SamplesPerChunk;
    foreach (var (firstChunk, perChunk) in runs) {
      if (firstChunk > chunkNumber)
        break;

      result = perChunk;
    }

    return result;
  }

  // ------------------------------------------------------------------------------------------
  // moov
  // ------------------------------------------------------------------------------------------

  private static byte[] _Movie(
    IReadOnlyList<Mp4TestTrack> tracks, long[][] chunkPlan, long mediaStart,
    long duration, long creationTime,
    IReadOnlyList<(string Name, string Text)>? tags,
    IReadOnlyList<(string Name, string Text)>? quickTimeTags,
    byte[]? cover, bool wide) {
    var parts = new List<byte[]> { _MovieHeader(duration, creationTime) };
    for (var t = 0; t < tracks.Count; ++t)
      parts.Add(_Track(tracks[t], chunkPlan[t], mediaStart, t + 1, duration));

    var userData = _UserData(tags, quickTimeTags, cover);
    if (userData != null)
      parts.Add(userData);

    var movie = _Box("moov", parts.ToArray());
    if (!wide)
      return movie;

    // Size 1 means the real, 64-bit size follows the type. The box grows by the eight bytes that
    // number occupies.
    var widened = new MemoryStream();
    widened.Write(_UInt32(1));
    widened.Write(_Ascii("moov"));
    widened.Write(_UInt64((ulong)movie.Length + 8));
    widened.Write(movie.AsSpan(8));
    return widened.ToArray();
  }

  /// <summary>
  /// Wraps a whole <c>moov</c> atom, header included, into a classic QuickTime <c>cmov</c>: the atom
  /// itself becomes the payload of <c>cmvd</c>, four bytes of its own length in front and deflated
  /// after, next to a <c>dcom</c> naming <c>zlib</c> — the same shape a real fast-start QuickTime file
  /// carries when it was saved with its header compressed.
  /// </summary>
  private static byte[] _CompressMovie(byte[] plainMovie) {
    using var deflated = new MemoryStream();
    using (var zlib = new ZLibStream(deflated, CompressionLevel.Optimal, leaveOpen: true))
      zlib.Write(plainMovie);

    var cmvdBody = new byte[4 + (int)deflated.Length];
    BinaryPrimitives.WriteUInt32BigEndian(cmvdBody, (uint)plainMovie.Length);
    deflated.ToArray().CopyTo(cmvdBody.AsSpan(4));

    var cmov = _Box("cmov", _Box("dcom", _Ascii("zlib")), _Box("cmvd", cmvdBody));
    return _Box("moov", cmov);
  }

  private static byte[] _MovieHeader(long duration, long creationTime)
    => _FullBox("mvhd", 0, 0,
      _UInt32((uint)creationTime),
      _UInt32((uint)creationTime),
      _UInt32(MOVIE_TIMESCALE),
      _UInt32((uint)duration),
      _UInt32(0x00010000), // rate
      _UInt16(0x0100),     // volume
      new byte[10],
      _UnityMatrix(),
      new byte[24],        // pre_defined
      _UInt32(2));         // next track id

  private static byte[] _Track(Mp4TestTrack track, long[] chunkOffsets, long mediaStart, int trackId, long duration) {
    var parts = new List<byte[]> {
      _FullBox("tkhd", 0, 3,
        _UInt32(0), _UInt32(0), _UInt32((uint)trackId), _UInt32(0), _UInt32((uint)duration),
        new byte[8], _UInt16(0), _UInt16(0), _UInt16(0), _UInt16(0), _UnityMatrix(),
        _UInt32((uint)(track.Width << 16)), _UInt32((uint)(track.Height << 16))),
    };

    if (track.Edits is { Length: > 0 }) {
      var entries = new List<byte[]> { _UInt32((uint)track.Edits.Length) };
      foreach (var (segment, mediaTime) in track.Edits) {
        entries.Add(_UInt32((uint)segment));
        entries.Add(_Int32((int)mediaTime));
        entries.Add(_UInt32(0x00010000)); // media rate 1.0
      }

      parts.Add(_Box("edts", _FullBox("elst", 0, 0, entries.ToArray())));
    }

    parts.Add(_Media(track, chunkOffsets, mediaStart));

    if (track.Name != null)
      parts.Add(_Box("udta", _Box("name", Encoding.UTF8.GetBytes(track.Name))));

    return _Box("trak", parts.ToArray());
  }

  private static byte[] _Media(Mp4TestTrack track, long[] chunkOffsets, long mediaStart) {
    var mediaDuration = (long)track.Samples.Count * track.SampleDuration;

    return _Box("mdia",
      _FullBox("mdhd", 0, 0,
        _UInt32(0), _UInt32(0), _UInt32((uint)track.Timescale), _UInt32((uint)mediaDuration),
        _UInt16((ushort)track.Language), _UInt16(0)),
      _FullBox("hdlr", 0, 0, _UInt32(0), _Ascii(track.Handler), new byte[12], [0]),
      _Box("minf", _Box("stbl", _SampleTable(track, chunkOffsets, mediaStart))));
  }

  private static byte[][] _SampleTable(Mp4TestTrack track, long[] chunkOffsets, long mediaStart) {
    var parts = new List<byte[]> {
      _FullBox("stsd", 0, 0, _UInt32(1), _SampleEntry(track)),
      _FullBox("stts", 0, 0, _UInt32(1), _UInt32((uint)track.Samples.Count), _UInt32((uint)track.SampleDuration)),
    };

    if (track.CompositionOffsets != null) {
      var entries = new List<byte[]> { _UInt32((uint)track.CompositionOffsets.Length) };
      foreach (var offset in track.CompositionOffsets) {
        entries.Add(_UInt32(1));
        entries.Add(_Int32(offset));
      }

      parts.Add(_FullBox("ctts", 0, 0, entries.ToArray()));
    }

    if (track.SyncSamples != null) {
      var entries = new List<byte[]> { _UInt32((uint)track.SyncSamples.Length) };
      foreach (var sample in track.SyncSamples)
        entries.Add(_UInt32((uint)sample));

      parts.Add(_FullBox("stss", 0, 0, entries.ToArray()));
    }

    if (track.ChunkRuns is { Length: > 0 } runs) {
      var entries = new List<byte[]> { _UInt32((uint)runs.Length) };
      foreach (var (firstChunk, perChunk) in runs) {
        entries.Add(_UInt32((uint)firstChunk));
        entries.Add(_UInt32((uint)perChunk));
        entries.Add(_UInt32(1)); // sample description index
      }

      parts.Add(_FullBox("stsc", 0, 0, entries.ToArray()));
    } else
      // One run covering every chunk, which is what a writer produces when the chunks are all the
      // same size — and the last chunk holding fewer samples than the run states is the ordinary
      // case, since the sample count is what stops the walk rather than the chunk table.
      parts.Add(_FullBox("stsc", 0, 0,
        _UInt32(1), _UInt32(1), _UInt32((uint)Math.Max(1, track.SamplesPerChunk)), _UInt32(1)));

    parts.Add(_SampleSizes(track));

    if (track.ChunkOffsets64) {
      var entries = new List<byte[]> { _UInt32((uint)chunkOffsets.Length) };
      foreach (var offset in chunkOffsets)
        entries.Add(_UInt64((ulong)(offset + mediaStart)));

      parts.Add(_FullBox("co64", 0, 0, entries.ToArray()));
    } else {
      var entries = new List<byte[]> { _UInt32((uint)chunkOffsets.Length) };
      foreach (var offset in chunkOffsets)
        entries.Add(_UInt32((uint)(offset + mediaStart)));

      parts.Add(_FullBox("stco", 0, 0, entries.ToArray()));
    }

    return parts.ToArray();
  }

  private static byte[] _SampleSizes(Mp4TestTrack track) {
    if (track.FixedSampleSize) {
      var size = track.Samples.Count == 0 ? 0 : track.Samples[0].Length;
      return _FullBox("stsz", 0, 0, _UInt32((uint)size), _UInt32((uint)track.Samples.Count));
    }

    if (track.CompactSizeBits == 0) {
      var entries = new List<byte[]> { _UInt32(0), _UInt32((uint)track.Samples.Count) };
      foreach (var sample in track.Samples)
        entries.Add(_UInt32((uint)sample.Length));

      return _FullBox("stsz", 0, 0, entries.ToArray());
    }

    var packed = new MemoryStream();
    switch (track.CompactSizeBits) {
      case 16:
        foreach (var sample in track.Samples)
          packed.Write(_UInt16((ushort)sample.Length));
        break;
      case 8:
        foreach (var sample in track.Samples)
          packed.WriteByte((byte)sample.Length);
        break;
      case 4:
        // Two to a byte, the earlier sample in the high nibble, and an odd count padded out.
        for (var i = 0; i < track.Samples.Count; i += 2) {
          var high = track.Samples[i].Length & 0x0F;
          var low = i + 1 < track.Samples.Count ? track.Samples[i + 1].Length & 0x0F : 0;
          packed.WriteByte((byte)((high << 4) | low));
        }

        break;
      default:
        throw new ArgumentOutOfRangeException(nameof(track), track.CompactSizeBits, "A compact size table is 4, 8 or 16 bits wide.");
    }

    return _FullBox("stz2", 0, 0,
      [0, 0, 0], [(byte)track.CompactSizeBits], _UInt32((uint)track.Samples.Count), packed.ToArray());
  }

  /// <summary>A sample entry of whichever shape the track's handler calls for.</summary>
  private static byte[] _SampleEntry(Mp4TestTrack track) {
    if (track.Handler != "vide")
      return _Box(track.Codec,
        new byte[6], _UInt16(1),          // reserved, data reference index
        new byte[8],                      // version, revision, vendor
        _UInt16(2), _UInt16(16),          // channel count, sample size
        _UInt16(0), _UInt16(0),           // pre-defined, reserved
        _UInt32(44100u << 16));           // sample rate, 16.16

    return _Box(track.Codec,
      new byte[6], _UInt16(1),            // reserved, data reference index
      _UInt16(0), _UInt16(0), new byte[12], // pre-defined, reserved, pre-defined
      _UInt16((ushort)track.Width), _UInt16((ushort)track.Height),
      _UInt32(0x00480000), _UInt32(0x00480000), // 72 dpi each way
      _UInt32(0),                         // reserved
      _UInt16(1),                         // frame count
      new byte[32],                       // compressor name
      _UInt16((ushort)track.Depth),
      _UInt16(0xFFFF));                   // pre-defined
  }

  // ------------------------------------------------------------------------------------------
  // udta
  // ------------------------------------------------------------------------------------------

  private static byte[]? _UserData(
    IReadOnlyList<(string Name, string Text)>? tags,
    IReadOnlyList<(string Name, string Text)>? quickTimeTags,
    byte[]? cover) {
    var parts = new List<byte[]>();

    if (tags != null || cover != null) {
      var items = new List<byte[]>();
      foreach (var (name, text) in tags ?? [])
        items.Add(_Box(name, _FullBox("data", 0, 1, _UInt32(0), Encoding.UTF8.GetBytes(text))));

      // The data type of a cover is 13 for a JPEG and 14 for a PNG rather than 1 for text.
      if (cover != null)
        items.Add(_Box("covr", _FullBox("data", 0, 14, _UInt32(0), cover)));

      parts.Add(_Box("meta", _UInt32(0), _Box("hdlr", new byte[8], _Ascii("mdir"), _Ascii("appl"), new byte[9]), _Box("ilst", items.ToArray())));
    }

    // A QuickTime text atom carries its own length and language in front of the text, which an
    // iTunes-style one does not — the two shapes share a udta and have to be told apart by name.
    foreach (var (name, text) in quickTimeTags ?? []) {
      var bytes = Encoding.UTF8.GetBytes(text);
      parts.Add(_Box(name, _UInt16((ushort)bytes.Length), _UInt16(0x55C4), bytes));
    }

    return parts.Count == 0 ? null : _Box("udta", parts.ToArray());
  }

  // ------------------------------------------------------------------------------------------
  // Box plumbing
  // ------------------------------------------------------------------------------------------

  private static byte[] _Box(string type, params byte[][] parts) {
    var body = new MemoryStream();
    foreach (var part in parts)
      body.Write(part);

    var payload = body.ToArray();
    var box = new MemoryStream();
    box.Write(_UInt32((uint)(payload.Length + 8)));
    box.Write(_Ascii(type));
    box.Write(payload);
    return box.ToArray();
  }

  private static byte[] _FullBox(string type, byte version, uint flags, params byte[][] parts) {
    var prefixed = new List<byte[]> { new byte[] { version, (byte)(flags >> 16), (byte)(flags >> 8), (byte)flags } };
    prefixed.AddRange(parts);
    return _Box(type, prefixed.ToArray());
  }

  private static byte[] _UnityMatrix() {
    var matrix = new byte[36];
    BinaryPrimitives.WriteUInt32BigEndian(matrix.AsSpan(0), 0x00010000);
    BinaryPrimitives.WriteUInt32BigEndian(matrix.AsSpan(16), 0x00010000);
    BinaryPrimitives.WriteUInt32BigEndian(matrix.AsSpan(32), 0x40000000);
    return matrix;
  }

  /// <summary>A four-character code as its bytes, copyright sign and all.</summary>
  private static byte[] _Ascii(string value) {
    var result = new byte[value.Length];
    for (var i = 0; i < value.Length; ++i)
      result[i] = (byte)value[i];

    return result;
  }

  private static byte[] _UInt16(ushort value) {
    var result = new byte[2];
    BinaryPrimitives.WriteUInt16BigEndian(result, value);
    return result;
  }

  private static byte[] _UInt32(uint value) {
    var result = new byte[4];
    BinaryPrimitives.WriteUInt32BigEndian(result, value);
    return result;
  }

  private static byte[] _Int32(int value) {
    var result = new byte[4];
    BinaryPrimitives.WriteInt32BigEndian(result, value);
    return result;
  }

  private static byte[] _UInt64(ulong value) {
    var result = new byte[8];
    BinaryPrimitives.WriteUInt64BigEndian(result, value);
    return result;
  }
}
