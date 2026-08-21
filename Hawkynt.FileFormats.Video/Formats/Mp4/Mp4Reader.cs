using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using FileFormat.Core;
using FileFormat.Riff;

namespace FileFormat.Mp4;

/// <summary>
/// Takes an ISO base media file apart: which tracks it declares, what it says about itself, and where
/// the samples of each track are.
/// </summary>
/// <remarks>
/// One format under four names. MP4, QuickTime MOV, M4V and 3GP are the same box structure with
/// different brands in <c>ftyp</c> and different codecs inside, which is why one reader takes all of
/// them and why the differences that do exist are handled where they occur rather than by branching
/// on the brand — QuickTime's <c>meta</c> lacking the version word, its <c>udta</c> text atoms
/// carrying their own length, and its <c>0x7FFF</c> for an unstated language are the three that
/// actually matter here.
/// <para/>
/// Nothing of <c>mdat</c> is read. It has no structure to read: an ISO base media file's packet
/// boundaries are not in the data at all, they are in the sample tables of <c>moov</c>, which is a
/// few hundred bytes per track however long the film. That is also why a file whose <c>moov</c>
/// follows its <c>mdat</c> — what a writer produces in one pass, and what both of the reference
/// samples this was measured against are — needs no special handling: the top-level boxes are walked
/// in whatever order they were written and <c>moov</c> is taken wherever it turns up.
/// </remarks>
public static class Mp4Reader {

  private static readonly FourCC _FILE_TYPE = new("ftyp");
  private static readonly FourCC _MOVIE = new("moov");
  private static readonly FourCC _MOVIE_FRAGMENT = new("moof");
  private static readonly FourCC _MOVIE_HEADER = new("mvhd");
  private static readonly FourCC _COMPRESSED_MOVIE = new("cmov");
  private static readonly FourCC _DECOMPRESSOR = new("dcom");
  private static readonly FourCC _COMPRESSED_MOVIE_DATA = new("cmvd");
  private static readonly FourCC _TRACK = new("trak");
  private static readonly FourCC _EDIT_LIST_CONTAINER = new("edts");
  private static readonly FourCC _EDIT_LIST = new("elst");
  private static readonly FourCC _MEDIA = new("mdia");
  private static readonly FourCC _MEDIA_HEADER = new("mdhd");
  private static readonly FourCC _HANDLER = new("hdlr");
  private static readonly FourCC _MEDIA_INFORMATION = new("minf");
  private static readonly FourCC _SAMPLE_TABLE = new("stbl");
  private static readonly FourCC _SAMPLE_DESCRIPTION = new("stsd");
  private static readonly FourCC _TIME_TO_SAMPLE = new("stts");
  private static readonly FourCC _COMPOSITION_OFFSET = new("ctts");
  private static readonly FourCC _SAMPLE_TO_CHUNK = new("stsc");
  private static readonly FourCC _SAMPLE_SIZE = new("stsz");
  private static readonly FourCC _COMPACT_SAMPLE_SIZE = new("stz2");
  private static readonly FourCC _CHUNK_OFFSET = new("stco");
  private static readonly FourCC _CHUNK_OFFSET_64 = new("co64");
  private static readonly FourCC _SYNC_SAMPLE = new("stss");
  private static readonly FourCC _USER_DATA = new("udta");
  private static readonly FourCC _METADATA = new("meta");
  private static readonly FourCC _ITEM_LIST = new("ilst");
  private static readonly FourCC _DATA = new("data");
  private static readonly FourCC _TRACK_NAME = new("name");
  private static readonly FourCC _COVER_ART = new("covr");

  private const string _VIDEO_HANDLER = "vide";
  private const string _AUDIO_HANDLER = "soun";

  /// <summary>The byte half the tag atoms of an ISO base media file are named with.</summary>
  private const byte _COPYRIGHT_SIGN = 0xA9;

  /// <summary>Seconds between the MP4 epoch and the Unix one; MP4 counts from 1904-01-01 UTC.</summary>
  /// <remarks>
  /// Sixty-six years, of which sixteen were leap years plus 1904 itself: 24107 days. Written as the
  /// number rather than as arithmetic because it is a constant of the format and not of the calendar.
  /// </remarks>
  private const long _SECONDS_FROM_1904_TO_1970 = 2_082_844_800L;

  private const int _FULL_BOX_PREFIX = 4;

  public static Mp4Container FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("MP4 file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static Mp4Container FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }

    using var buffer = new MemoryStream();
    stream.CopyTo(buffer);
    return FromBytes(buffer.ToArray());
  }

  public static Mp4Container FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);

    return _Parse(data);
  }

  /// <summary>
  /// Reads an ISO base media file out of a span.
  /// </summary>
  /// <remarks>
  /// The bytes are copied once here, and only here. A container has to outlive the call that built
  /// it — its packets are windows onto the file and are walked long afterwards — and a span promises
  /// nothing about how long the memory behind it stays valid. Callers that already hold an array
  /// should use <see cref="FromBytes"/>, which keeps theirs.
  /// </remarks>
  public static Mp4Container FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < BoxScanner.HEADER_SIZE)
      throw new InvalidDataException("Data is too small to be a valid ISO base media file.");

    return _Parse(data.ToArray());
  }

  private static Mp4Container _Parse(byte[] data) {
    if (data.Length < BoxScanner.HEADER_SIZE)
      throw new InvalidDataException("Data is too small to be a valid ISO base media file.");

    var file = new ReadOnlyMemory<byte>(data);

    Mp4Box? movie = null;
    string? brand = null;
    var fragmented = false;
    foreach (var box in BoxScanner.Walk(file, 0, data.Length)) {
      if (box.Type == _MOVIE)
        movie ??= box;
      else if (box.Type == _MOVIE_FRAGMENT)
        fragmented = true;
      else if (box.Type == _FILE_TYPE && box.Body.Length >= 4)
        brand ??= _Ascii(box.Body.Span[..4]);
    }

    if (movie == null)
      throw new InvalidDataException($"Missing '{_MOVIE}' box.");

    // A fragmented file keeps its sample tables in the fragments rather than in moov, whose own
    // tables are then empty. Walking them would report a container of no packets at all, which is
    // indistinguishable from a film that really holds none — so it is refused by name instead.
    if (fragmented)
      throw new NotSupportedException(
        $"This file is fragmented: its samples are described by '{_MOVIE_FRAGMENT}' boxes rather than by the sample tables of '{_MOVIE}', which this reader does not walk.");

    // Classic QuickTime allows the whole movie atom to be written zlib-compressed inside a single
    // 'cmov', which is what a moov with no 'mvhd' among its direct children usually means. The tree
    // that comes back describes the same film; only where its bytes live changes, so everything below
    // this point is walked against whichever buffer actually holds it while sample data is still read
    // from the file as delivered — a chunk offset in a decompressed moov still counts from the start
    // of the original file, not from the start of the buffer the header was unpacked into.
    var (structure, structureMovie) = _ResolveMovie(file, movie.Value);

    var (movieTimescale, duration, created) = _ReadMovieHeader(structure, structureMovie);

    var tracks = new List<Mp4Track>();
    foreach (var box in BoxScanner.Children(structure, structureMovie))
      if (box.Type == _TRACK)
        tracks.Add(_ReadTrack(structure, file, box, tracks.Count, movieTimescale));

    var metadata = _ReadMetadata(structure, structureMovie, tracks, movieTimescale, duration, created);

    return new() {
      MajorBrand = brand,
      File = file,
      Tracks = tracks.ToArray(),
      FileMetadata = metadata,
    };
  }

  /// <summary>
  /// Unpacks a compressed movie atom, if that is what this one is.
  /// </summary>
  /// <remarks>
  /// A moov with no <c>mvhd</c> among its direct children is not necessarily broken — QuickTime's own
  /// "Save As" has always been able to write one whose atom tree is deflated into a single <c>cmov</c>,
  /// which is what "fast start, compressed header" means. <c>cmov</c> holds a <c>dcom</c> naming the
  /// method, always <c>zlib</c> in every file this was measured against, and a <c>cmvd</c> whose first
  /// four bytes are the inflated size and the rest a zlib stream; what comes out the far side is a
  /// complete replacement <c>moov</c> atom, header and all, standing in for the one that was compressed
  /// away.
  /// <para/>
  /// A moov that already has an <c>mvhd</c> is handed back untouched, and one with neither an
  /// <c>mvhd</c> nor a <c>cmov</c> is handed back untouched too — there is nothing to unpack, and
  /// <see cref="_ReadMovieHeader"/> is what reports that honestly.
  /// </remarks>
  private static (ReadOnlyMemory<byte> File, Mp4Box Movie) _ResolveMovie(ReadOnlyMemory<byte> file, Mp4Box movie) {
    Mp4Box? compressed = null;
    foreach (var box in BoxScanner.Children(file, movie)) {
      if (box.Type == _MOVIE_HEADER)
        return (file, movie);

      if (box.Type == _COMPRESSED_MOVIE)
        compressed ??= box;
    }

    if (compressed == null)
      return (file, movie);

    string? algorithm = null;
    Mp4Box? compressedData = null;
    foreach (var box in BoxScanner.Children(file, compressed.Value)) {
      if (box.Type == _DECOMPRESSOR && box.Body.Length >= 4)
        algorithm ??= _Ascii(box.Body.Span[..4]);
      else if (box.Type == _COMPRESSED_MOVIE_DATA)
        compressedData ??= box;
    }

    if (compressedData == null)
      throw new InvalidDataException($"'{_COMPRESSED_MOVIE}' box has no '{_COMPRESSED_MOVIE_DATA}'.");

    if (algorithm != "zlib")
      throw new NotSupportedException(
        $"This file's movie header is compressed with '{algorithm ?? "an unnamed method"}' rather than 'zlib', which this reader does not decompress.");

    var body = compressedData.Value.Body;
    if (body.Length < 4)
      throw new InvalidDataException($"'{_COMPRESSED_MOVIE_DATA}' is {body.Length} bytes, too short to hold its own inflated size.");

    var inflatedSize = BinaryPrimitives.ReadUInt32BigEndian(body.Span[..4]);
    if (inflatedSize > int.MaxValue)
      throw new InvalidDataException($"'{_COMPRESSED_MOVIE_DATA}' states an inflated size of {inflatedSize} bytes, too large to hold in memory.");

    var inflated = _Inflate(body[4..], inflatedSize, _COMPRESSED_MOVIE_DATA);

    var decompressed = new ReadOnlyMemory<byte>(inflated);
    foreach (var box in BoxScanner.Walk(decompressed, 0, decompressed.Length))
      if (box.Type == _MOVIE)
        return (decompressed, box);

    throw new InvalidDataException(
      $"'{_COMPRESSED_MOVIE}' inflated to {inflated.Length} bytes with no '{_MOVIE}' box inside.");
  }

  /// <summary>Inflates a <c>cmvd</c>'s zlib payload to the size its own header claims.</summary>
  private static byte[] _Inflate(ReadOnlyMemory<byte> compressed, uint inflatedSize, FourCC box) {
    using var source = new MemoryStream(compressed.ToArray(), writable: false);
    using var zlib = new ZLibStream(source, CompressionMode.Decompress);
    using var destination = new MemoryStream(checked((int)inflatedSize));
    try {
      zlib.CopyTo(destination);
    } catch (InvalidDataException e) {
      throw new InvalidDataException($"'{box}' does not hold a valid zlib stream.", e);
    }

    return destination.ToArray();
  }

  // ------------------------------------------------------------------------------------------
  // Movie and track headers
  // ------------------------------------------------------------------------------------------

  /// <summary>Reads <c>mvhd</c>: the clock the whole file's durations are counted in, and when it was made.</summary>
  private static (long Timescale, long Duration, DateTimeOffset? Created) _ReadMovieHeader(ReadOnlyMemory<byte> file, Mp4Box movie) {
    foreach (var box in BoxScanner.Children(file, movie)) {
      if (box.Type != _MOVIE_HEADER)
        continue;

      var span = box.Body.Span;
      if (span.Length < _FULL_BOX_PREFIX)
        break;

      // Version 1 widens the two times and the duration to sixty-four bits and leaves the time scale
      // where it was. Nothing else about the box moves.
      var version = span[0];
      var wide = version == 1;
      var needed = wide ? _FULL_BOX_PREFIX + 28 : _FULL_BOX_PREFIX + 16;
      if (span.Length < needed)
        break;

      var at = _FULL_BOX_PREFIX;
      var created = wide ? (long)BinaryPrimitives.ReadUInt64BigEndian(span.Slice(at, 8)) : BinaryPrimitives.ReadUInt32BigEndian(span.Slice(at, 4));
      at += wide ? 16 : 8; // past creation and modification time
      var timescale = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(at, 4));
      at += 4;
      var duration = wide ? (long)BinaryPrimitives.ReadUInt64BigEndian(span.Slice(at, 8)) : BinaryPrimitives.ReadUInt32BigEndian(span.Slice(at, 4));

      return (timescale, duration, _TimeOf(created));
    }

    throw new InvalidDataException($"Missing '{_MOVIE_HEADER}' box.");
  }

  /// <summary>Describes one <c>trak</c> without deciding anything about its codec.</summary>
  /// <param name="file">
  /// Where the track's boxes live — the file itself, or a <c>cmov</c>'s inflated buffer when the movie
  /// header was compressed.
  /// </param>
  /// <param name="dataFile">
  /// The file as delivered, whatever <paramref name="file"/> is. A sample table's chunk offsets are
  /// always counted from here: they name a place in <c>mdat</c>, which a compressed movie header never
  /// touches, so they stay correct however the atom tree above them was stored.
  /// </param>
  private static Mp4Track _ReadTrack(ReadOnlyMemory<byte> file, ReadOnlyMemory<byte> dataFile, Mp4Box track, int index, long movieTimescale) {
    Mp4Box? media = null;
    Mp4Box? editList = null;
    string? name = null;

    foreach (var box in BoxScanner.Children(file, track)) {
      if (box.Type == _MEDIA)
        media ??= box;
      else if (box.Type == _USER_DATA)
        name ??= _ReadTrackName(file, box);
      else if (box.Type == _EDIT_LIST_CONTAINER) {
        foreach (var edit in BoxScanner.Children(file, box))
          if (edit.Type == _EDIT_LIST)
            editList ??= edit;
      }
    }

    if (media == null)
      throw new InvalidDataException($"Track {index} has no '{_MEDIA}' box.");

    var (timescale, language) = _ReadMediaHeader(file, media.Value, index);
    var handler = _ReadHandler(file, media.Value);
    var kind = handler switch {
      _VIDEO_HANDLER => MediaStreamKind.Video,
      _AUDIO_HANDLER => MediaStreamKind.Audio,
      "sbtl" or "subt" or "text" or "clcp" => MediaStreamKind.Subtitle,
      null => MediaStreamKind.Unknown,
      _ => MediaStreamKind.Data,
    };

    var sampleTable = _FindSampleTable(file, media.Value, index);
    var (description, timeToSample, compositionOffsets, sampleToChunk, sampleSizes, compactSizes, chunkOffsets, chunkOffsets64, syncSamples)
      = _ReadSampleTableBoxes(file, sampleTable);

    var table = new Mp4SampleTable(
      dataFile, timeToSample, compositionOffsets, sampleToChunk, sampleSizes, compactSizes,
      chunkOffsets, chunkOffsets64, syncSamples,
      _EditShift(editList, movieTimescale, timescale));

    var entry = _FirstSampleEntry(file, description);

    // A track's time base is one unit of its own media time scale, which is the clock every timestamp
    // this reader hands out is counted in. It is not the movie's — those are two different clocks and
    // mixing them puts every packet of the track at the wrong moment.
    var timeBase = timescale == 0 ? Rational.Unknown : new Rational(1, timescale);

    if (kind != MediaStreamKind.Video || entry == null)
      return new() {
        Info = new() {
          Index = index,
          Kind = kind,
          Codec = entry == null ? CodecTag.None : _CodecOf(entry.Value.Type),
          TimeBase = timeBase,
          DeclaredFrameCount = table.SampleCount,
          CodecPrivateData = entry?.Whole(file) ?? ReadOnlyMemory<byte>.Empty,
          Language = language,
          Name = name,
        },
        Table = table,
      };

    var (width, height, depth) = _ReadVisualSampleEntry(entry.Value, index);

    // A single-entry stts is the writer stating one duration for every sample, which for pictures is
    // the frame rate the other way up. More than one entry means the samples differ and there is no
    // one rate to report; a reader that averaged them would state a rate the file never claimed.
    var sampleDuration = table.ConstantSampleDuration;
    var frameRate = sampleDuration > 0 && timescale > 0 ? new Rational(timescale, sampleDuration) : Rational.Unknown;

    return new() {
      Info = new() {
        Index = index,
        Kind = kind,
        Codec = _CodecOf(entry.Value.Type),
        TimeBase = timeBase,
        FrameRate = frameRate,
        DeclaredFrameCount = table.SampleCount,
        Width = width,
        Height = height,
        BitsPerPixel = depth,
        // The whole sample entry, verbatim. What describes the codec is inside it as boxes of its
        // own — 'esds' for MPEG-4, 'avcC' for H.264, nothing at all for Motion JPEG — and picking the
        // right one out would mean this container knowing the codecs, which is the one thing the
        // demux/decode split exists to prevent.
        CodecPrivateData = entry.Value.Whole(file),
        Language = language,
        Name = name,
      },
      Table = table,
    };
  }

  /// <summary>Reads <c>mdhd</c>: the clock this track's timestamps are counted in, and its language.</summary>
  private static (long Timescale, string? Language) _ReadMediaHeader(ReadOnlyMemory<byte> file, Mp4Box media, int index) {
    foreach (var box in BoxScanner.Children(file, media)) {
      if (box.Type != _MEDIA_HEADER)
        continue;

      var span = box.Body.Span;
      if (span.Length < _FULL_BOX_PREFIX)
        break;

      var wide = span[0] == 1;
      var needed = wide ? _FULL_BOX_PREFIX + 30 : _FULL_BOX_PREFIX + 18;
      if (span.Length < needed)
        break;

      var at = _FULL_BOX_PREFIX + (wide ? 16 : 8); // past creation and modification time
      var timescale = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(at, 4));
      at += 4 + (wide ? 8 : 4); // past the duration
      var language = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(at, 2));

      return (timescale, _LanguageOf(language));
    }

    throw new InvalidDataException($"Track {index} has no '{_MEDIA_HEADER}' box.");
  }

  /// <summary>Reads the handler type out of <c>hdlr</c>, which is what says whether a track is pictures.</summary>
  private static string? _ReadHandler(ReadOnlyMemory<byte> file, Mp4Box media) {
    foreach (var box in BoxScanner.Children(file, media)) {
      if (box.Type != _HANDLER)
        continue;

      // Four bytes of version and flags, then a pre-defined word that QuickTime uses for a component
      // type and ISO base media leaves at zero, then the handler type itself.
      var span = box.Body.Span;
      if (span.Length < _FULL_BOX_PREFIX + 8)
        break;

      return _Ascii(span.Slice(_FULL_BOX_PREFIX + 4, 4));
    }

    return null;
  }

  private static Mp4Box _FindSampleTable(ReadOnlyMemory<byte> file, Mp4Box media, int index) {
    foreach (var information in BoxScanner.Children(file, media)) {
      if (information.Type != _MEDIA_INFORMATION)
        continue;

      foreach (var box in BoxScanner.Children(file, information))
        if (box.Type == _SAMPLE_TABLE)
          return box;
    }

    throw new InvalidDataException($"Track {index} has no '{_SAMPLE_TABLE}' box.");
  }

  private static (Mp4Box? Description, ReadOnlyMemory<byte> TimeToSample, ReadOnlyMemory<byte> CompositionOffsets,
    ReadOnlyMemory<byte> SampleToChunk, ReadOnlyMemory<byte> SampleSizes, ReadOnlyMemory<byte> CompactSizes,
    ReadOnlyMemory<byte> ChunkOffsets, ReadOnlyMemory<byte> ChunkOffsets64, ReadOnlyMemory<byte> SyncSamples)
    _ReadSampleTableBoxes(ReadOnlyMemory<byte> file, Mp4Box sampleTable) {
    Mp4Box? description = null;
    ReadOnlyMemory<byte> timeToSample = default, compositionOffsets = default;
    ReadOnlyMemory<byte> sampleToChunk = default, sampleSizes = default, compactSizes = default;
    ReadOnlyMemory<byte> chunkOffsets = default, chunkOffsets64 = default, syncSamples = default;

    foreach (var box in BoxScanner.Children(file, sampleTable)) {
      if (box.Type == _SAMPLE_DESCRIPTION)
        description ??= box;
      else if (box.Type == _TIME_TO_SAMPLE)
        timeToSample = _Keep(timeToSample, box);
      else if (box.Type == _COMPOSITION_OFFSET)
        compositionOffsets = _Keep(compositionOffsets, box);
      else if (box.Type == _SAMPLE_TO_CHUNK)
        sampleToChunk = _Keep(sampleToChunk, box);
      else if (box.Type == _SAMPLE_SIZE)
        sampleSizes = _Keep(sampleSizes, box);
      else if (box.Type == _COMPACT_SAMPLE_SIZE)
        compactSizes = _Keep(compactSizes, box);
      else if (box.Type == _CHUNK_OFFSET)
        chunkOffsets = _Keep(chunkOffsets, box);
      else if (box.Type == _CHUNK_OFFSET_64)
        chunkOffsets64 = _Keep(chunkOffsets64, box);
      else if (box.Type == _SYNC_SAMPLE)
        syncSamples = _Keep(syncSamples, box);
    }

    return (description, timeToSample, compositionOffsets, sampleToChunk, sampleSizes, compactSizes, chunkOffsets, chunkOffsets64, syncSamples);

    // Only the first of a repeated box counts, the way the first hdrl of an AVI does. A second one is
    // a malformed file, and taking the later one would silently prefer whatever was appended.
    static ReadOnlyMemory<byte> _Keep(ReadOnlyMemory<byte> existing, Mp4Box box) => existing.IsEmpty ? box.Body : existing;
  }

  /// <summary>The first entry of <c>stsd</c>, which is what says how the track's samples are coded.</summary>
  /// <remarks>
  /// The first and not all of them. A <c>stsd</c> may hold several, one per way the track's samples
  /// are coded, but a track that changes codec part way through would need more than one
  /// <see cref="MediaStreamInfo"/> to be described honestly — and no writer produces one, so the
  /// alternative to taking the first is describing a track nothing has ever written.
  /// </remarks>
  private static Mp4Box? _FirstSampleEntry(ReadOnlyMemory<byte> file, Mp4Box? description) {
    if (description is not { } box || box.Body.Length < _FULL_BOX_PREFIX + 4)
      return null;

    // The entries begin after the version-and-flags word and the entry count. They are ordinary
    // boxes whose type is the codec's four-character code.
    foreach (var entry in BoxScanner.Walk(file, box.BodyOffset + _FULL_BOX_PREFIX + 4, box.BodyOffset + box.Body.Length))
      return entry;

    return null;
  }

  /// <summary>Reads the picture size and depth out of a visual sample entry.</summary>
  /// <remarks>
  /// The fields are at fixed places in every visual sample entry whatever the codec, which is what
  /// makes reading them the container's business and not a codec's: six reserved bytes and a data
  /// reference index, sixteen bytes QuickTime uses for a vendor and two quality values and ISO base
  /// media leaves at zero, then the width and the height. The depth is thirty-two bytes of compressor
  /// name further on.
  /// </remarks>
  private static (int Width, int Height, int Depth) _ReadVisualSampleEntry(Mp4Box entry, int index) {
    const int _WIDTH_AT = 24;
    const int _DEPTH_AT = 74;

    var span = entry.Body.Span;
    if (span.Length < _WIDTH_AT + 4)
      throw new InvalidDataException(
        $"Track {index} states a '{entry.Type}' sample entry of {span.Length} bytes, which is too short to hold a picture size.");

    var width = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(_WIDTH_AT, 2));
    var height = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(_WIDTH_AT + 2, 2));
    if (width == 0 || height == 0)
      throw new InvalidDataException($"Track {index} states an impossible picture size of {width}x{height}.");

    var depth = span.Length >= _DEPTH_AT + 2 ? BinaryPrimitives.ReadUInt16BigEndian(span.Slice(_DEPTH_AT, 2)) : 0;
    return (width, height, depth);
  }

  /// <summary>
  /// What the edit list shifts this track's timestamps by, in the track's own time scale.
  /// </summary>
  /// <remarks>
  /// Measured rather than assumed. ffmpeg writes an <c>elst</c> of one entry for both tracks of an
  /// MP4 it muxes; the video track's states a media time of zero and shifts nothing, while an AAC
  /// track's states 1024 — the encoder's priming samples — and ffprobe accordingly reports that
  /// track's first packet at <c>-1024</c> rather than at zero. A reader that ignored the box would
  /// disagree with every other tool about where a file with sound starts.
  /// <para/>
  /// Two forms are applied and no more. A leading entry whose media time is <c>-1</c> is an empty
  /// edit — a gap before the media begins — and delays everything after it by its own duration, which
  /// is stated in the movie's time scale and so has to be converted into the track's. The single real
  /// entry after it, or on its own, moves the timestamps back by its media time. A list of several
  /// real entries is a genuine edit of the timeline rather than an offset, and this reports the
  /// track's own timestamps unshifted rather than inventing an interpretation of it.
  /// </remarks>
  private static long _EditShift(Mp4Box? editList, long movieTimescale, long mediaTimescale) {
    if (editList == null)
      return 0;

    var body = editList.Value.Body;
    if (body.Length < _FULL_BOX_PREFIX + 4)
      return 0;

    var span = body.Span;
    var wide = span[0] == 1;
    var entrySize = wide ? 20 : 12;
    var declared = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(_FULL_BOX_PREFIX, 4));
    var entries = span[(_FULL_BOX_PREFIX + 4)..];
    var count = (int)Math.Min(declared, (uint)(entries.Length / entrySize));
    if (count == 0)
      return 0;

    var shift = 0L;
    var first = 0;

    var (firstDuration, firstMediaTime) = _Entry(entries, 0);
    if (firstMediaTime == -1) {
      if (movieTimescale > 0)
        shift += firstDuration * mediaTimescale / movieTimescale;

      first = 1;
    }

    if (count - first == 1) {
      var (_, mediaTime) = _Entry(entries, first);
      if (mediaTime > 0)
        shift -= mediaTime;
    }

    return shift;

    (long Duration, long MediaTime) _Entry(ReadOnlySpan<byte> data, int at) {
      var offset = at * entrySize;
      return wide
        ? ((long)BinaryPrimitives.ReadUInt64BigEndian(data.Slice(offset, 8)), BinaryPrimitives.ReadInt64BigEndian(data.Slice(offset + 8, 8)))
        : (BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, 4)), BinaryPrimitives.ReadInt32BigEndian(data.Slice(offset + 4, 4)));
    }
  }

  // ------------------------------------------------------------------------------------------
  // Metadata
  // ------------------------------------------------------------------------------------------

  /// <summary>Gathers what the file says about itself from <c>mvhd</c> and whichever tag list it carries.</summary>
  private static VideoMetadata _ReadMetadata(
    ReadOnlyMemory<byte> file, Mp4Box movie, IReadOnlyList<Mp4Track> tracks,
    long movieTimescale, long duration, DateTimeOffset? created) {
    string? title = null, artist = null, album = null, encodedBy = null;
    var texts = new List<TextMetadataEntry>();
    var covers = new List<CoverArt>();

    foreach (var box in BoxScanner.Children(file, movie)) {
      if (box.Type != _USER_DATA)
        continue;

      foreach (var item in _UserDataItems(file, box)) {
        if (item.Type == _COVER_ART) {
          var cover = _ReadCover(item.Value);
          if (cover != null)
            covers.Add(cover);

          continue;
        }

        var value = item.Text;
        if (value == null)
          continue;

        switch (_TagName(item.Type)) {
          case "nam": title ??= value; break;
          case "ART": artist ??= value; break;
          case "alb": album ??= value; break;
          case "too": encodedBy ??= value; break;
          case "swr": encodedBy ??= value; break;
          case "cmt": texts.Add(new("Comment", value)); break;
          case "cpy": texts.Add(new("Copyright", value)); break;
          case "day": texts.Add(new("Creation Time", value)); break;
          default: texts.Add(new(_TagName(item.Type), value)); break;
        }
      }
    }

    var streams = new MediaStreamMetadata[tracks.Count];
    for (var i = 0; i < tracks.Count; ++i) {
      var info = tracks[i].Info;
      streams[i] = new(info.Index, info.Kind, info.Codec, info.Language, info.Name);
    }

    return new() {
      Title = title,
      Artist = artist,
      Album = album,
      EncodedBy = encodedBy,
      CreationTime = created,
      // The movie header's own claim, in the clock it states beside it. A file left unfinished keeps
      // whatever duration was written when it stopped, which is why this is the header's claim rather
      // than a total of the samples — counting those means walking every track's tables.
      Duration = movieTimescale > 0 && duration > 0
        ? TimeSpan.FromTicks(duration * TimeSpan.TicksPerSecond / movieTimescale)
        : null,
      Streams = streams,
      CoverArt = covers,
      TextEntries = texts,
    };
  }

  /// <summary>One entry of a tag list, however the file happens to spell tag lists.</summary>
  /// <param name="Type">The atom's four-character type, copyright sign and all.</param>
  /// <param name="Text">Its value read as text, or <c>null</c> when it is not text.</param>
  /// <param name="Value">Its value as bytes.</param>
  private readonly record struct Mp4Tag(FourCC Type, string? Text, ReadOnlyMemory<byte> Value);

  /// <summary>
  /// Walks a <c>udta</c>, taking both of the shapes a tag can be written in.
  /// </summary>
  /// <remarks>
  /// The two are not variants of one design, they are two designs that ended up in the same box.
  /// QuickTime puts its atoms straight into <c>udta</c> and gives each a payload of a sixteen-bit
  /// length, a sixteen-bit language and then the text — which is what ffmpeg's MOV muxer writes for
  /// <c>©swr</c>. iTunes-style MP4 wraps them in <c>meta</c> then <c>ilst</c>, and gives each atom a
  /// <c>data</c> box whose first word says what kind of value it holds. Both were measured coming out
  /// of the same version of ffmpeg, one per container, so a reader that took only one of them would
  /// read the tags of only one of the two files.
  /// <para/>
  /// <c>meta</c> is where the two disagree most sharply: ISO base media declares it a full box, so its
  /// children start four bytes in, and QuickTime declares it a plain one, so they start immediately.
  /// Which it is has to be decided from the bytes, because both spellings occur in files that are
  /// otherwise the same format.
  /// </remarks>
  private static IEnumerable<Mp4Tag> _UserDataItems(ReadOnlyMemory<byte> file, Mp4Box userData) {
    foreach (var box in BoxScanner.Children(file, userData)) {
      if (box.Type == _METADATA) {
        foreach (var child in BoxScanner.Children(file, box, _MetaPrefix(box))) {
          if (child.Type != _ITEM_LIST)
            continue;

          foreach (var atom in BoxScanner.Children(file, child))
            yield return _ReadItemListAtom(file, atom);
        }

        continue;
      }

      // A QuickTime text atom: two bytes of length, two of language, then the text. Only the atoms
      // whose name starts with the copyright sign are that shape; 'name', 'hdlr' and the rest of what
      // shares a udta are boxes of their own and reading them this way would report their first two
      // bytes as a length.
      if (box.Type.A != _COPYRIGHT_SIGN || box.Body.Length < 4)
        continue;

      var declared = BinaryPrimitives.ReadUInt16BigEndian(box.Body.Span[..2]);
      var available = box.Body.Length - 4;
      var text = box.Body[4..(4 + Math.Min(declared, available))];
      yield return new(box.Type, _Utf8(text.Span), text);
    }
  }

  /// <summary>
  /// How many bytes of a <c>meta</c> box come before its children.
  /// </summary>
  /// <remarks>
  /// Four in ISO base media, where it is a full box, and none in QuickTime, where it is not. Decided
  /// by looking, and by looking at both halves of a box header rather than one: a plausible length
  /// followed by four printable letters is a box, and the version-and-flags word is followed by a
  /// length instead — four bytes that are almost never all printable. Testing the length alone would
  /// misread a <c>meta</c> whose flags happened to be a small number as the QuickTime spelling.
  /// </remarks>
  private static int _MetaPrefix(Mp4Box meta) {
    var span = meta.Body.Span;
    if (span.Length < BoxScanner.HEADER_SIZE)
      return 0;

    var size = BinaryPrimitives.ReadUInt32BigEndian(span[..4]);
    if (size < BoxScanner.HEADER_SIZE || size > (uint)span.Length)
      return _FULL_BOX_PREFIX;

    for (var i = 4; i < 8; ++i)
      if (span[i] is < 0x20 or > 0x7E)
        return _FULL_BOX_PREFIX;

    return 0;
  }

  /// <summary>Reads one <c>ilst</c> atom, whose value is inside a <c>data</c> box of its own.</summary>
  private static Mp4Tag _ReadItemListAtom(ReadOnlyMemory<byte> file, Mp4Box atom) {
    foreach (var box in BoxScanner.Children(file, atom)) {
      if (box.Type != _DATA || box.Body.Length < 8)
        continue;

      // Four bytes of type — 1 for UTF-8 text, 13 for a JPEG, 14 for a PNG — then four of locale,
      // then the value. Anything but text is handed on as bytes rather than guessed at.
      var kind = BinaryPrimitives.ReadUInt32BigEndian(box.Body.Span[..4]) & 0x00FFFFFF;
      var value = box.Body[8..];
      return new(atom.Type, kind == 1 ? _Utf8(value.Span) : null, value);
    }

    return new(atom.Type, null, ReadOnlyMemory<byte>.Empty);
  }

  /// <summary>Turns a <c>covr</c> atom into cover art, keeping the picture in the format it was embedded as.</summary>
  private static CoverArt? _ReadCover(ReadOnlyMemory<byte> value) {
    if (value.IsEmpty)
      return null;

    var span = value.Span;
    var mime = span.Length >= 8 && span[0] == 0x89 && span[1] == (byte)'P' && span[2] == (byte)'N' && span[3] == (byte)'G'
      ? "image/png"
      : span.Length >= 3 && span[0] == 0xFF && span[1] == 0xD8 && span[2] == 0xFF
        ? "image/jpeg"
        : null;

    // The picture goes across in the format it was embedded in and is not decoded. That is what a
    // muxer writing another container has to hand over, and decoding it first could only lose the
    // original — the same reason ImageMetadata's own embedded pictures are kept as bytes.
    return new(value.ToArray(), mime, Kind: "cover");
  }

  // ------------------------------------------------------------------------------------------
  // Small conversions
  // ------------------------------------------------------------------------------------------

  /// <summary>
  /// Turns a count of seconds since 1904 into an instant.
  /// </summary>
  /// <remarks>
  /// MP4 counts from 1904-01-01 UTC, which is neither the Unix epoch nor anything the framework
  /// knows. Zero means the writer stated nothing rather than that the file was made in 1904: ffmpeg
  /// writes zero unless a creation time was given, and ffprobe reports no creation time for such a
  /// file at all.
  /// </remarks>
  private static DateTimeOffset? _TimeOf(long secondsSince1904)
    => secondsSince1904 == 0 ? null : DateTimeOffset.FromUnixTimeSeconds(secondsSince1904 - _SECONDS_FROM_1904_TO_1970);

  /// <summary>
  /// Turns an <c>mdhd</c> language field into a language tag.
  /// </summary>
  /// <remarks>
  /// The field is three ISO 639-2 letters packed five bits each with the top bit clear, each letter
  /// stored as its distance from <c>0x60</c>. Two values are not that at all and both occur in files
  /// this was measured against: zero, and QuickTime's <c>0x7FFF</c> for "unspecified" — which ffmpeg
  /// writes into every MOV it muxes and which unpacked would give three characters of nonsense.
  /// Values below <c>0x400</c> are Macintosh language codes from a table of their own rather than
  /// packed letters, and are left unstated rather than misread as letters.
  /// </remarks>
  private static string? _LanguageOf(int packed) {
    if (packed is 0 or 0x7FFF or < 0x400)
      return null;

    Span<char> letters = stackalloc char[3];
    for (var i = 0; i < 3; ++i) {
      var letter = (char)(((packed >> ((2 - i) * 5)) & 0x1F) + 0x60);
      if (letter is < 'a' or > 'z')
        return null;

      letters[i] = letter;
    }

    return new(letters);
  }

  /// <summary>The codec code of a sample entry, as the bytes sit in the file.</summary>
  /// <remarks>
  /// The same convention <see cref="CodecTag"/> uses everywhere: the first character is the low byte,
  /// so a MOV's <c>jpeg</c> and an AVI's <c>MJPG</c> are comparable without either container knowing
  /// what the other calls things.
  /// </remarks>
  private static CodecTag _CodecOf(FourCC type) => new(type.A | ((uint)type.B << 8) | ((uint)type.C << 16) | ((uint)type.D << 24));

  /// <summary>An <c>ilst</c> atom's name without the copyright sign that half of them start with.</summary>
  private static string _TagName(FourCC type) {
    var name = new string([(char)type.A, (char)type.B, (char)type.C, (char)type.D]);
    return type.A == 0xA9 ? name[1..] : name;
  }

  /// <summary>
  /// The name the writer gave a track, from whichever of the two places it put one.
  /// </summary>
  /// <remarks>
  /// ffmpeg writes a track title into <c>trak/udta/name</c>, which is a bare UTF-8 string with no
  /// length and no language in front of it — measured off a file muxed with
  /// <c>-metadata:s:v:0 title=</c>, which ffprobe then reports back as <c>TAG:name</c>. A track may
  /// also carry a tag list of its own with a <c>©nam</c> in it, so both are looked at and the bare
  /// atom wins for being the one that is actually written.
  /// </remarks>
  private static string? _ReadTrackName(ReadOnlyMemory<byte> file, Mp4Box userData) {
    foreach (var box in BoxScanner.Children(file, userData))
      if (box.Type == _TRACK_NAME)
        return _Utf8(box.Body.Span);

    foreach (var item in _UserDataItems(file, userData))
      if (_TagName(item.Type) == "nam")
        return item.Text;

    return null;
  }

  private static string _Ascii(ReadOnlySpan<byte> data) => Encoding.ASCII.GetString(data);

  /// <summary>Reads a tag's text, which is UTF-8 and may or may not carry a terminator.</summary>
  private static string? _Utf8(ReadOnlySpan<byte> data) {
    var end = data.IndexOf((byte)0);
    if (end >= 0)
      data = data[..end];

    if (data.IsEmpty)
      return null;

    return Encoding.UTF8.GetString(data).Trim();
  }
}
