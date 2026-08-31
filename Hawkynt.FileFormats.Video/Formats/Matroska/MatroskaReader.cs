using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Matroska;

/// <summary>
/// Takes a Matroska document apart: which tracks it declares, what it says about itself, and where
/// the segment's clusters begin.
/// </summary>
/// <remarks>
/// One reader for Matroska and WebM because they are one format. WebM is a Matroska document whose
/// <c>DocType</c> says so and whose codecs are drawn from a shorter list, and the shorter list is the
/// business of whoever is asked for a decoder — the elements, the identifiers and the block layout
/// are the same bytes in both, so a second reader would be the same reader with a different name.
/// <para/>
/// The clusters are not read here. Where a track's frames are is not a table anywhere in this format;
/// it is the clusters themselves, in order, which is why <see cref="MatroskaContainer.ReadPackets"/>
/// walks them lazily and why opening a two-hour recording costs its header and nothing per frame. The
/// <c>Cues</c> element is an index, but only of keyframes and only of the clusters they start, so it
/// answers "where do I seek to" and not "where is packet <c>n</c>".
/// <para/>
/// What the document states about time is stated twice over and in two units, which is the trap this
/// format sets. <c>TimestampScale</c> gives the nanoseconds one tick is worth and every block's
/// timestamp is counted in those ticks; <c>DefaultDuration</c> and <c>CodecDelay</c> are stated in
/// nanoseconds regardless. Mixing the two gives timestamps that look entirely plausible and drift.
/// </remarks>
public static class MatroskaReader {

  /// <summary>Nanoseconds in a second, which is the unit Matroska states its real durations in.</summary>
  private const long _NANOSECONDS_PER_SECOND = 1_000_000_000L;

  /// <summary>What <c>TimestampScale</c> means when the file does not state it.</summary>
  /// <remarks>
  /// One millisecond per tick. Every file measured here writes it explicitly and every one of them
  /// writes this value; the specification names it as the default, so a file that omits it is stating
  /// this rather than stating nothing.
  /// </remarks>
  private const long _DEFAULT_TIMESTAMP_SCALE = 1_000_000L;

  /// <summary>
  /// The language a track without a <c>Language</c> element is in.
  /// </summary>
  /// <remarks>
  /// English, which is a peculiar default for a format used everywhere and is nonetheless what the
  /// specification says. Measured rather than taken on trust: a file built here with no
  /// <c>Language</c> element at all is reported by ffprobe as <c>language=eng</c>, so reporting
  /// nothing would disagree with every other tool about a file that says nothing.
  /// </remarks>
  private const string _DEFAULT_LANGUAGE = "eng";

  /// <summary>Seconds between the Matroska epoch and the Unix one; Matroska counts from 2001-01-01 UTC.</summary>
  /// <remarks>
  /// Thirty-one years, of which eight were leap years: 11323 days. Verified against ffprobe, which
  /// reports a <c>DateUTC</c> of 139 651 750 000 000 000 as 2005-06-05T08:09:10Z.
  /// </remarks>
  private const long _SECONDS_FROM_1970_TO_2001 = 978_307_200L;

  private const string _MATROSKA_DOC_TYPE = "matroska";
  private const string _WEBM_DOC_TYPE = "webm";

  /// <summary>The <c>CodecID</c> of a track that carries a Video for Windows description of itself.</summary>
  /// <remarks>
  /// The one place a Matroska track states a four-character code. Everything else in this format
  /// names its codec with a string, which is why <see cref="MediaStreamInfo.CodecId"/> exists.
  /// </remarks>
  private const string _VFW_CODEC_ID = "V_MS/VFW/FOURCC";

  /// <summary>Where <c>biCompression</c> sits in a <c>BITMAPINFOHEADER</c>.</summary>
  private const int _BITMAP_COMPRESSION_AT = 16;

  private const int _BITMAP_INFO_HEADER_SIZE = 40;

  /// <summary>Reads an instance from the specified file.</summary>
  public static MatroskaContainer FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Matroska file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  /// <summary>Reads an instance from the specified stream.</summary>
  public static MatroskaContainer FromStream(Stream stream) {
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

  /// <summary>Reads an instance from the specified byte array.</summary>
  public static MatroskaContainer FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);

    return _Parse(data);
  }

  /// <summary>Reads a Matroska document out of a span.</summary>
  /// <remarks>
  /// The bytes are copied once here, and only here. A container has to outlive the call that built
  /// it — its packets are windows onto the file and are walked long afterwards — and a span promises
  /// nothing about how long the memory behind it stays valid. Callers that already hold an array
  /// should use <see cref="FromBytes"/>, which keeps theirs.
  /// </remarks>
  public static MatroskaContainer FromSpan(ReadOnlySpan<byte> data) => _Parse(data.ToArray());

  private static MatroskaContainer _Parse(byte[] data) {
    if (data.Length < 4)
      throw new InvalidDataException("Data is too small to be a Matroska document.");

    var file = new ReadOnlyMemory<byte>(data);

    // The header and the segment are the only two elements at the document's own level, and an
    // unknown-size segment runs to the end of the file — a segment written to a pipe cannot know its
    // own length, and ffmpeg's live muxer writes exactly that.
    EbmlElement? header = null;
    EbmlElement? segment = null;
    foreach (var element in EbmlScanner.Walk(file, 0, data.Length)) {
      if (element.Id == MatroskaElementId.EBML_HEADER)
        header ??= element;
      else if (element.Id == MatroskaElementId.SEGMENT) {
        segment ??= element;
        break;
      }
    }

    if (header == null)
      throw new InvalidDataException("The data does not begin with an EBML header.");

    var docType = _DocTypeOf(file, header.Value);

    // Refused by name rather than read hopefully. EBML carries Matroska, WebM and a handful of things
    // that are not video at all, and the identifiers this reader looks for mean nothing in those —
    // it would find no tracks and report a container of no streams, which is indistinguishable from
    // a Matroska file that genuinely holds none.
    if (docType is not (_MATROSKA_DOC_TYPE or _WEBM_DOC_TYPE))
      throw new NotSupportedException(
        $"This EBML document states a DocType of '{docType ?? "(none)"}', where this reader takes '{_MATROSKA_DOC_TYPE}' and '{_WEBM_DOC_TYPE}'.");

    if (segment == null)
      throw new InvalidDataException("The document holds no Segment element.");

    // One walk of the segment's own level, not one per thing wanted out of it. The elements are
    // walked by their headers alone, but a cluster the writer stated no length for has to be measured
    // by reading its children's headers, and doing that three times over would make opening a live
    // recording cost three passes over every block in it.
    EbmlElement? info = null;
    EbmlElement? trackList = null;
    var tags = new List<EbmlElement>();
    var attachments = new List<EbmlElement>();
    foreach (var level1 in EbmlScanner.Walk(file, segment.Value.BodyOffset, segment.Value.BodyOffset + segment.Value.Body.Length, MatroskaElementId.IsSegmentLevel))
      switch (level1.Id) {
        // The first of a repeated element and no other, the way the RIFF and ISO readers treat a
        // repeated header: a second one is a malformed file, and taking the later would silently
        // prefer whatever was appended.
        case MatroskaElementId.INFO:
          info ??= level1;
          break;
        case MatroskaElementId.TRACKS:
          trackList ??= level1;
          break;
        case MatroskaElementId.TAGS:
          tags.Add(level1);
          break;
        case MatroskaElementId.ATTACHMENTS:
          attachments.Add(level1);
          break;
      }

    var (timestampScale, duration, title, muxingApp, writingApp, dateUtc) = _ReadInfo(file, info);

    if (trackList == null)
      throw new InvalidDataException("The Segment declares no Tracks element, so nothing says which track a block belongs to.");

    var tracks = _ReadTracks(file, trackList.Value, timestampScale);
    if (tracks.Count == 0)
      throw new InvalidDataException("The Segment's Tracks element declares no TrackEntry, so the file holds no stream to walk.");

    var metadata = _ReadMetadata(file, tags, attachments, tracks, timestampScale, duration, title, muxingApp, writingApp, dateUtc);

    return new() {
      DocType = docType,
      File = file,
      SegmentStart = segment.Value.BodyOffset,
      SegmentEnd = segment.Value.BodyOffset + segment.Value.Body.Length,
      TrackEntries = tracks,
      TimestampScale = timestampScale,
      FileMetadata = metadata,
    };
  }

  private static string? _DocTypeOf(ReadOnlyMemory<byte> file, EbmlElement header) {
    foreach (var element in EbmlScanner.Children(file, header))
      if (element.Id == MatroskaElementId.DOC_TYPE)
        return element.TextValue();

    return null;
  }

  // ------------------------------------------------------------------------------------------
  // Info
  // ------------------------------------------------------------------------------------------

  /// <summary>Reads <c>Info</c>: the clock the whole segment is timed by, and what it says about itself.</summary>
  /// <remarks>
  /// A segment with no <c>Info</c> at all is still readable. Every field of it has a default the
  /// specification states, and the one that matters — <c>TimestampScale</c> — defaults to the
  /// millisecond every writer measured here states explicitly, so a file that omits the element times
  /// exactly as one that writes it.
  /// </remarks>
  private static (long TimestampScale, double? Duration, string? Title, string? MuxingApp, string? WritingApp, long? DateUtc)
    _ReadInfo(ReadOnlyMemory<byte> file, EbmlElement? info) {
    var timestampScale = _DEFAULT_TIMESTAMP_SCALE;
    double? duration = null;
    string? title = null, muxingApp = null, writingApp = null;
    long? dateUtc = null;

    if (info == null)
      return (timestampScale, duration, title, muxingApp, writingApp, dateUtc);

    foreach (var element in EbmlScanner.Children(file, info.Value))
      switch (element.Id) {
        case MatroskaElementId.TIMESTAMP_SCALE:
          var stated = (long)element.UnsignedValue();
          if (stated > 0)
            timestampScale = stated;

          break;
        case MatroskaElementId.DURATION:
          duration ??= element.FloatValue();
          break;
        case MatroskaElementId.TITLE:
          title ??= element.TextValue();
          break;
        case MatroskaElementId.MUXING_APP:
          muxingApp ??= element.TextValue();
          break;
        case MatroskaElementId.WRITING_APP:
          writingApp ??= element.TextValue();
          break;
        case MatroskaElementId.DATE_UTC:
          dateUtc ??= element.SignedValue();
          break;
      }

    return (timestampScale, duration, title, muxingApp, writingApp, dateUtc);
  }

  // ------------------------------------------------------------------------------------------
  // Tracks
  // ------------------------------------------------------------------------------------------

  private static IReadOnlyList<MatroskaTrack> _ReadTracks(ReadOnlyMemory<byte> file, EbmlElement trackList, long timestampScale) {
    var tracks = new List<MatroskaTrack>();
    foreach (var entry in EbmlScanner.Children(file, trackList))
      if (entry.Id == MatroskaElementId.TRACK_ENTRY)
        tracks.Add(_ReadTrackEntry(file, entry, tracks.Count, timestampScale));

    return tracks;
  }

  private static MatroskaTrack _ReadTrackEntry(ReadOnlyMemory<byte> file, EbmlElement entry, int index, long timestampScale) {
    ulong number = 0;
    ulong type = 0;
    string? codecId = null, language = null, languageBcp47 = null, name = null;
    var codecPrivate = ReadOnlyMemory<byte>.Empty;
    long defaultDuration = 0, codecDelay = 0;
    var width = 0;
    var height = 0;

    foreach (var element in EbmlScanner.Children(file, entry))
      switch (element.Id) {
        case MatroskaElementId.TRACK_NUMBER:
          number = element.UnsignedValue();
          break;
        case MatroskaElementId.TRACK_TYPE:
          type = element.UnsignedValue();
          break;
        case MatroskaElementId.CODEC_ID:
          codecId ??= element.TextValue();
          break;
        case MatroskaElementId.CODEC_PRIVATE:
          codecPrivate = element.Body;
          break;
        case MatroskaElementId.CODEC_DELAY:
          codecDelay = (long)element.UnsignedValue();
          break;
        case MatroskaElementId.LANGUAGE:
          language ??= element.TextValue();
          break;
        case MatroskaElementId.LANGUAGE_BCP47:
          languageBcp47 ??= element.TextValue();
          break;
        case MatroskaElementId.TRACK_NAME:
          name ??= element.TextValue();
          break;
        case MatroskaElementId.DEFAULT_DURATION:
          defaultDuration = (long)element.UnsignedValue();
          break;
        case MatroskaElementId.VIDEO:
          (width, height) = _ReadVideo(file, element);
          break;
        case MatroskaElementId.CONTENT_ENCODINGS:
          _RefuseContentEncodings(file, element, number);
          break;
      }

    if (number == 0)
      throw new InvalidDataException($"Track {index} states no TrackNumber, so no block can be attributed to it.");

    if (codecId == null)
      throw new InvalidDataException($"Track {index} states no CodecID, so nothing says what its packets are coded with.");

    var kind = type switch {
      1 => MediaStreamKind.Video,
      2 => MediaStreamKind.Audio,
      0x11 => MediaStreamKind.Subtitle,
      3 or 0x10 or 0x12 or 0x20 or 0x21 => MediaStreamKind.Data,
      _ => MediaStreamKind.Unknown,
    };

    return new() {
      Info = new() {
        Index = index,
        Kind = kind,
        // Only where the file actually holds a four-character code. Matroska names its codecs with
        // strings, and a tag invented from one would be a code no file contains — which a decoder
        // matching on tags would then have to be taught to recognise, and a second container writing
        // the stream out would have to be taught to undo.
        Codec = _CodecTagOf(codecId, codecPrivate),
        CodecId = codecId,
        // One tick of the segment's own clock, which is what every timestamp this reader hands out is
        // counted in. Reduced so that the ordinary millisecond scale reads as 1/1000, which is what
        // ffprobe reports for the same file.
        TimeBase = _Reduce(timestampScale, _NANOSECONDS_PER_SECOND),
        // DefaultDuration is nanoseconds a frame, so the rate is its reciprocal — 100 000 000 ns is
        // the 10/1 ffprobe reports for a ten-frame-a-second file.
        FrameRate = defaultDuration > 0 ? _Reduce(_NANOSECONDS_PER_SECOND, defaultDuration) : Rational.Unknown,
        Width = width,
        Height = height,
        // Verbatim, whatever it is: a BITMAPINFOHEADER for a Video for Windows track, a Vorbis
        // identification header for A_VORBIS, an AVC decoder configuration record for H.264. What is
        // in it is defined by the codec and not by this container.
        CodecPrivateData = codecPrivate,
        Language = languageBcp47 ?? language ?? _DEFAULT_LANGUAGE,
        Name = name,
      },
      Number = number,
      DefaultDurationNanoseconds = defaultDuration,
      // Rounded rather than truncated, because that is what ffprobe reports: a CodecDelay of
      // 2 902 494 ns against a millisecond tick moves the track's first packet to -3 and not to -2.
      CodecDelayTicks = codecDelay == 0 ? 0 : (codecDelay + (timestampScale / 2)) / timestampScale,
    };
  }

  private static (int Width, int Height) _ReadVideo(ReadOnlyMemory<byte> file, EbmlElement video) {
    var width = 0;
    var height = 0;

    foreach (var element in EbmlScanner.Children(file, video))
      switch (element.Id) {
        case MatroskaElementId.PIXEL_WIDTH:
          width = (int)Math.Min(element.UnsignedValue(), int.MaxValue);
          break;
        case MatroskaElementId.PIXEL_HEIGHT:
          height = (int)Math.Min(element.UnsignedValue(), int.MaxValue);
          break;
      }

    return (width, height);
  }

  /// <summary>
  /// Refuses a track whose frames are not in the file as the codec produced them.
  /// </summary>
  /// <remarks>
  /// A <c>ContentEncoding</c> says a block's bytes were compressed, had a common header stripped off
  /// them, or were encrypted before being written. Reading such a block and handing it on is handing
  /// on something that is not a frame: header stripping in particular removes bytes the decoder needs
  /// and leaves a payload that still looks entirely plausible, which would come back as a picture
  /// full of noise with nothing in the file to point at. Refusing by name is the only honest answer a
  /// demuxer that does not undo the encoding can give.
  /// </remarks>
  private static void _RefuseContentEncodings(ReadOnlyMemory<byte> file, EbmlElement encodings, ulong track) {
    foreach (var encoding in EbmlScanner.Children(file, encodings)) {
      if (encoding.Id != MatroskaElementId.CONTENT_ENCODING)
        continue;

      foreach (var element in EbmlScanner.Children(file, encoding))
        switch (element.Id) {
          case MatroskaElementId.CONTENT_COMPRESSION: {
            var algorithm = 0UL;
            foreach (var child in EbmlScanner.Children(file, element))
              if (child.Id == MatroskaElementId.CONTENT_COMP_ALGO)
                algorithm = child.UnsignedValue();

            throw new NotSupportedException(
              $"Track {track} states a ContentCompression of algorithm {algorithm}, so its blocks do not hold the frames the codec produced. This reader does not undo it.");
          }

          case MatroskaElementId.CONTENT_ENCRYPTION:
            throw new NotSupportedException(
              $"Track {track} states a ContentEncryption, so its blocks hold ciphertext rather than frames.");

          case MatroskaElementId.CONTENT_ENCODING_TYPE when element.UnsignedValue() == 1:
            throw new NotSupportedException(
              $"Track {track} states a ContentEncoding of type 1, which is encryption, so its blocks hold ciphertext rather than frames.");
        }
    }
  }

  /// <summary>
  /// The four-character code the track states, where it states one at all.
  /// </summary>
  /// <remarks>
  /// One <c>CodecID</c> carries a code and the rest do not. <c>V_MS/VFW/FOURCC</c> means the track's
  /// private data is a <c>BITMAPINFOHEADER</c> — the same structure an AVI's <c>strf</c> is — and its
  /// <c>biCompression</c> field is a real four-character code sitting in the file. ffprobe reads such
  /// a track's tag as <c>MJPG</c> where the same picture written as <c>V_MJPEG</c> gets no tag at
  /// all, which is exactly the distinction kept here.
  /// <para/>
  /// Everything else gets <see cref="CodecTag.None"/> and states its name through
  /// <see cref="MediaStreamInfo.CodecId"/> instead. Inventing a code for <c>V_VP9</c> would put a
  /// number in the file's description that is in no file.
  /// </remarks>
  private static CodecTag _CodecTagOf(string codecId, ReadOnlyMemory<byte> codecPrivate) {
    if (codecId != _VFW_CODEC_ID || codecPrivate.Length < _BITMAP_INFO_HEADER_SIZE)
      return CodecTag.None;

    return new(BinaryPrimitives.ReadUInt32LittleEndian(codecPrivate.Span.Slice(_BITMAP_COMPRESSION_AT, 4)));
  }

  // ------------------------------------------------------------------------------------------
  // Metadata
  // ------------------------------------------------------------------------------------------

  private static VideoMetadata _ReadMetadata(
    ReadOnlyMemory<byte> file, IReadOnlyList<EbmlElement> tags, IReadOnlyList<EbmlElement> attachments,
    IReadOnlyList<MatroskaTrack> tracks,
    long timestampScale, double? duration, string? title, string? muxingApp, string? writingApp, long? dateUtc) {
    var texts = new List<TextMetadataEntry>();
    var covers = new List<CoverArt>();
    string? tagTitle = null, artist = null, album = null;

    foreach (var attachment in attachments)
      _ReadAttachments(file, attachment, covers);

    foreach (var tag in tags)
      _ReadTags(file, tag, texts, ref tagTitle, ref artist, ref album);

    // The writing application is kept beside the muxing one rather than instead of it. They are two
    // different tools — the muxer that assembled the file and the program a person was using — and
    // ffprobe reports the muxer as the file's encoder, which is what EncodedBy means here.
    if (writingApp != null)
      texts.Add(new("Writing Application", writingApp));

    var streams = new MediaStreamMetadata[tracks.Count];
    for (var i = 0; i < tracks.Count; ++i) {
      var info = tracks[i].Info;
      streams[i] = new(info.Index, info.Kind, info.Codec, info.Language, info.Name);
    }

    return new() {
      // Info's own Title first. A global TITLE tag says the same thing in the other of the two places
      // this format keeps metadata, and is taken only when the element itself is absent.
      Title = title ?? tagTitle,
      Artist = artist,
      Album = album,
      EncodedBy = muxingApp ?? writingApp,
      CreationTime = dateUtc is { } stamp ? _TimeOf(stamp) : null,
      // Duration is a float counted in the segment's own ticks, not in seconds — 500.0 against a
      // millisecond tick is half a second, which is what ffprobe reports for the same file.
      Duration = duration is > 0 ? TimeSpan.FromTicks((long)(duration.Value * timestampScale / _NANOSECONDS_PER_SECOND * TimeSpan.TicksPerSecond)) : null,
      Streams = streams,
      CoverArt = covers,
      TextEntries = texts,
    };
  }

  /// <summary>
  /// Reads the attached files that are pictures as cover art.
  /// </summary>
  /// <remarks>
  /// An attachment is where a Matroska file keeps a cover, and the picture crosses in the format it
  /// was embedded as rather than decoded — the same rule the ISO reader's <c>covr</c> follows, and for
  /// the same reason: decoding it could only lose the original a muxer would have to hand on.
  /// <para/>
  /// Deliberately not a stream. ffmpeg reports an attachment as an extra stream carrying one packet,
  /// which is a convenience of its model rather than something the file says: a Matroska attachment
  /// has no track number, appears in no cluster, and has no timestamp. Counting it as a stream would
  /// renumber the real ones against what the file declares.
  /// </remarks>
  private static void _ReadAttachments(ReadOnlyMemory<byte> file, EbmlElement attachments, List<CoverArt> covers) {
    foreach (var attached in EbmlScanner.Children(file, attachments)) {
      if (attached.Id != MatroskaElementId.ATTACHED_FILE)
        continue;

      string? description = null, name = null, mime = null;
      var data = ReadOnlyMemory<byte>.Empty;

      foreach (var element in EbmlScanner.Children(file, attached))
        switch (element.Id) {
          case MatroskaElementId.FILE_DESCRIPTION:
            description ??= element.TextValue();
            break;
          case MatroskaElementId.FILE_NAME:
            name ??= element.TextValue();
            break;
          case MatroskaElementId.FILE_MIME_TYPE:
            mime ??= element.TextValue();
            break;
          case MatroskaElementId.FILE_DATA:
            data = element.Body;
            break;
        }

      if (data.IsEmpty || mime == null || !mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        continue;

      covers.Add(new(data.ToArray(), mime, description, name));
    }
  }

  /// <summary>
  /// Reads the tags that describe the whole file.
  /// </summary>
  /// <remarks>
  /// The ones that describe the whole file and no others. A <c>Tag</c> whose <c>Targets</c> name a
  /// track describes that track, and ffprobe reports the two separately — a per-track <c>ENCODER</c>
  /// appears against the stream and a global <c>TITLE</c> against the format. Folding the per-track
  /// ones in here would attribute a track's encoder to the film.
  /// </remarks>
  private static void _ReadTags(
    ReadOnlyMemory<byte> file, EbmlElement tags, List<TextMetadataEntry> texts,
    ref string? title, ref string? artist, ref string? album) {
    foreach (var tag in EbmlScanner.Children(file, tags)) {
      if (tag.Id != MatroskaElementId.TAG)
        continue;

      var targeted = false;
      foreach (var element in EbmlScanner.Children(file, tag)) {
        if (element.Id != MatroskaElementId.TAG_TARGETS)
          continue;

        foreach (var target in EbmlScanner.Children(file, element))
          if (target.Id == MatroskaElementId.TAG_TRACK_UID && target.UnsignedValue() != 0)
            targeted = true;
      }

      if (targeted)
        continue;

      foreach (var element in EbmlScanner.Children(file, tag)) {
        if (element.Id != MatroskaElementId.SIMPLE_TAG)
          continue;

        string? name = null, value = null;
        foreach (var child in EbmlScanner.Children(file, element))
          switch (child.Id) {
            case MatroskaElementId.TAG_NAME:
              name ??= child.TextValue();
              break;
            case MatroskaElementId.TAG_STRING:
              value ??= child.TextValue();
              break;
          }

        if (name == null || value == null)
          continue;

        switch (name.ToUpperInvariant()) {
          case "TITLE":
            title ??= value;
            break;
          case "ARTIST":
            artist ??= value;
            break;
          case "ALBUM":
            album ??= value;
            break;
        }

        texts.Add(new(name, value));
      }
    }
  }

  // ------------------------------------------------------------------------------------------
  // Small conversions
  // ------------------------------------------------------------------------------------------

  /// <summary>Turns a count of nanoseconds since 2001 into an instant.</summary>
  private static DateTimeOffset _TimeOf(long nanosecondsSince2001)
    => DateTimeOffset.FromUnixTimeSeconds(_SECONDS_FROM_1970_TO_2001)
      + TimeSpan.FromTicks(nanosecondsSince2001 / (_NANOSECONDS_PER_SECOND / TimeSpan.TicksPerSecond));

  /// <summary>Puts a ratio in lowest terms so it reads the way the file meant it.</summary>
  /// <remarks>
  /// A time base of 1 000 000 over 1 000 000 000 is the same ratio as 1 over 1000 and reads as
  /// nothing at all; ffprobe reports the reduced form and so does this.
  /// </remarks>
  private static Rational _Reduce(long numerator, long denominator) {
    if (numerator == 0 || denominator == 0)
      return Rational.Unknown;

    var a = Math.Abs(numerator);
    var b = Math.Abs(denominator);
    while (b != 0)
      (a, b) = (b, a % b);

    return new(numerator / a, denominator / a);
  }
}
