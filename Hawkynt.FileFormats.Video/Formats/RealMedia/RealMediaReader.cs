using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using FileFormat.Core;

namespace FileFormat.RealMedia;

/// <summary>
/// Takes a RealMedia file's structure apart: which streams it declares, what it says about itself,
/// and where its packets are.
/// </summary>
/// <remarks>
/// The file is a flat run of chunks. <c>PROP</c> states the rates and where the packets begin,
/// <c>MDPR</c> describes one stream and carries that stream's codec-specific bytes verbatim,
/// <c>CONT</c> holds the title, author, copyright and comment, <c>DATA</c> holds the packets and
/// <c>INDX</c> indexes them for seeking. Nothing of <c>DATA</c> is read here — only the headers, which
/// are a few hundred bytes whatever the length of the recording — and the packets are walked on
/// demand by <see cref="RealMediaContainer.ReadPackets(RealMediaContainer)"/>.
/// <para/>
/// Because every chunk states its own length, a chunk this reader has never heard of costs nothing.
/// That is what makes this reader complete for the format while decoding no codec at all: a
/// <c>.rm</c> full of RealVideo 4 and Cook is a perfectly good <c>.rm</c>, and which codecs are inside
/// is somebody else's question.
/// </remarks>
public static class RealMediaReader {

  /// <summary>The file header's body: a version and the number of chunks that follow it.</summary>
  private const int _FILE_HEADER_BODY = 8;

  /// <summary>The fields of a media properties chunk before its stream name.</summary>
  private const int _MEDIA_PROPERTIES_FIXED = 2 + (4 * 7);

  /// <summary>The fields of a video stream's description before its codec-specific remainder.</summary>
  /// <remarks>
  /// A length, the four characters <c>VIDO</c>, the codec's four-character code, the picture size, the
  /// depth, two padding figures and the frame rate. Everything past them is the codec's own and is
  /// handed across untouched.
  /// </remarks>
  private const int _VIDEO_DESCRIPTION = 26;

  /// <summary>The four characters a video stream's description opens its second field with.</summary>
  private const uint _VIDEO_MARKER = 0x5649444F; // "VIDO"

  /// <summary>Where a version 4 sound description states the code naming its codec.</summary>
  /// <remarks>
  /// Both codes in a version 4 description are introduced by a length byte, which is always four;
  /// the first names the interleaver and the second the codec. A version 5 description drops the
  /// length bytes and puts the two codes at fixed places instead.
  /// </remarks>
  private const int _AUDIO_V4_CODEC = 0x3E;

  /// <summary>Where a version 5 sound description states the code naming its codec.</summary>
  private const int _AUDIO_V5_CODEC = 0x42;

  /// <summary>The seconds one RealMedia timestamp stands for, which the format fixes at a millisecond.</summary>
  private static readonly Rational _TIME_BASE = new(1, 1000);

  /// <summary>The mime type an ordinary file gives the chunk that describes the file rather than a stream.</summary>
  private const string _FILE_INFO = "logical-fileinfo";

  public static RealMediaContainer FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("RealMedia file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static RealMediaContainer FromStream(Stream stream) {
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

  public static RealMediaContainer FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);

    return _Parse(data);
  }

  /// <summary>
  /// Reads a RealMedia file out of a span.
  /// </summary>
  /// <remarks>
  /// The bytes are copied once here, and only here. A container has to outlive the call that built
  /// it — its packets are windows onto the file and are walked long afterwards — and a span promises
  /// nothing about how long the memory behind it stays valid. Callers that already hold an array
  /// should use <see cref="FromBytes"/>, which keeps theirs.
  /// </remarks>
  public static RealMediaContainer FromSpan(ReadOnlySpan<byte> data) => _Parse(data.ToArray());

  private static RealMediaContainer _Parse(byte[] data) {
    if (data.Length < RealMediaChunkScanner.PREFIX + _FILE_HEADER_BODY)
      throw new InvalidDataException("Data is too small to be a valid RealMedia file.");

    if (RealMediaChunkScanner.NameAt(data, 0) != RealMediaChunkScanner.FILE_HEADER)
      throw new InvalidDataException(
        "This file does not begin with the four characters '.RMF', which every RealMedia file opens with.");

    var file = new ReadOnlyMemory<byte>(data);
    var span = file.Span;

    var drafts = new List<_Draft>();
    var texts = new List<TextMetadataEntry>();
    string? title = null, author = null, copyright = null, comment = null, generatedBy = null;
    DateTimeOffset? created = null;
    long durationMilliseconds = 0;
    var dataStart = -1;
    var dataEnd = -1;

    foreach (var chunk in RealMediaChunkScanner.Walk(file)) {
      var available = Math.Min(chunk.Offset + chunk.Length, data.Length) - chunk.BodyOffset;
      if (available <= 0)
        continue;

      switch (chunk.Name) {
        case RealMediaChunkScanner.PROPERTIES:
          if (chunk.Version == 0 && available >= 40)
            durationMilliseconds = BinaryPrimitives.ReadUInt32BigEndian(span[(chunk.BodyOffset + 20)..]);

          break;

        case RealMediaChunkScanner.MEDIA_PROPERTIES:
          if (chunk.Version == 0)
            _ReadMediaProperties(file, chunk.BodyOffset, chunk.BodyOffset + available, drafts, texts, ref created, ref generatedBy);

          break;

        case RealMediaChunkScanner.CONTENT:
          if (chunk.Version == 0)
            _ReadContent(span, chunk.BodyOffset, chunk.BodyOffset + available, ref title, ref author, ref copyright, ref comment);

          break;

        case RealMediaChunkScanner.DATA:
          // The first data chunk only. A file may chain a second one, and its offset is stated in the
          // first; every file measured here states zero, and a reader that followed the chain without
          // one to follow would be reading a field nobody wrote.
          if (chunk.Version == 0 && dataStart < 0 && available >= RealMediaPacketReader.DATA_PREFIX) {
            dataStart = chunk.BodyOffset + RealMediaPacketReader.DATA_PREFIX;
            dataEnd = chunk.Offset + chunk.Length;
          }

          break;

        // INDX and anything else a writer added: stepped over. An index is a faster way to reach a
        // packet this reader already reaches by walking, and nothing else here needs it.
        default:
          break;
      }
    }

    if (dataStart < 0)
      throw new InvalidDataException(
        "This RealMedia file holds no 'DATA' chunk, so it declares no packets at all.");

    var streams = new MediaStreamInfo[drafts.Count];
    var highestNumber = -1;
    for (var i = 0; i < drafts.Count; ++i) {
      streams[i] = drafts[i].ToStreamInfo(i);
      if (drafts[i].Number > highestNumber)
        highestNumber = drafts[i].Number;
    }

    // RealMedia numbers its streams itself and a file may leave gaps or start above zero, where a
    // stream's index is its position among the declarations. The two are different numbers and a
    // packet states the first, so demuxing needs the translation between them.
    var indexByNumber = new int[highestNumber + 1];
    var videoByNumber = new bool[highestNumber + 1];
    Array.Fill(indexByNumber, -1);
    for (var i = 0; i < drafts.Count; ++i) {
      indexByNumber[drafts[i].Number] = i;
      videoByNumber[drafts[i].Number] = drafts[i].Kind == MediaStreamKind.Video;
    }

    var streamMetadata = new MediaStreamMetadata[streams.Length];
    for (var i = 0; i < streams.Length; ++i)
      streamMetadata[i] = new(streams[i].Index, streams[i].Kind, streams[i].Codec, streams[i].Language, streams[i].Name);

    _Text(texts, "Comment", comment);
    _Text(texts, "Copyright", copyright);

    return new() {
      File = file,
      StreamInfos = streams,
      DataStart = dataStart,
      DataEnd = Math.Min(dataEnd, data.Length),
      StreamIndexByNumber = indexByNumber,
      IsVideoByNumber = videoByNumber,
      FileMetadata = new() {
        Title = _OrNull(title),
        Artist = _OrNull(author),
        EncodedBy = _OrNull(generatedBy),
        CreationTime = created,
        Duration = durationMilliseconds > 0 ? TimeSpan.FromMilliseconds(durationMilliseconds) : null,
        Streams = streamMetadata,
        TextEntries = texts,
      },
    };
  }

  // ------------------------------------------------------------------------------------------
  // Streams
  // ------------------------------------------------------------------------------------------

  /// <summary>What one media properties chunk turned out to describe.</summary>
  private sealed class _Draft {

    public int Number;
    public MediaStreamKind Kind;
    public CodecTag Codec;
    public string? Name;
    public int Width;
    public int Height;
    public int BitsPerPixel;
    public Rational FrameRate = Rational.Unknown;
    public ReadOnlyMemory<byte> Private;

    public MediaStreamInfo ToStreamInfo(int index) => new() {
      Index = index,
      Kind = this.Kind,
      Codec = this.Codec,
      TimeBase = _TIME_BASE,
      FrameRate = this.FrameRate,
      Width = this.Width,
      Height = this.Height,
      BitsPerPixel = this.BitsPerPixel,
      CodecPrivateData = this.Private,
      Name = this.Name,
    };
  }

  /// <summary>
  /// Reads one media properties chunk, which describes one stream.
  /// </summary>
  /// <remarks>
  /// A chunk whose mime type is <c>logical-fileinfo</c> is not a stream and is not counted as one. It
  /// describes the file — the target audience the recording was made for, when it was made and what
  /// made it — and carries no packets at all; every file here that has one declares it as stream two
  /// and never sends a packet for it. Reporting it as a stream would put an entry in the list that no
  /// packet ever belongs to and would number the real streams differently from every other tool.
  /// </remarks>
  private static void _ReadMediaProperties(
    ReadOnlyMemory<byte> file, int at, int end, List<_Draft> drafts, List<TextMetadataEntry> texts,
    ref DateTimeOffset? created, ref string? generatedBy) {
    var span = file.Span;
    if (at + _MEDIA_PROPERTIES_FIXED + 2 > end)
      return;

    var number = BinaryPrimitives.ReadUInt16BigEndian(span[at..]);
    var cursor = at + _MEDIA_PROPERTIES_FIXED;

    if (!_TryReadByteString(span, ref cursor, end, out var name))
      return;

    if (!_TryReadByteString(span, ref cursor, end, out var mimeType))
      return;

    if (cursor + 4 > end)
      return;

    var descriptionLength = (int)BinaryPrimitives.ReadUInt32BigEndian(span[cursor..]);
    cursor += 4;
    if (descriptionLength < 0 || cursor + descriptionLength > end)
      descriptionLength = end - cursor;

    var description = file.Slice(cursor, descriptionLength);

    if (mimeType == _FILE_INFO) {
      _ReadFileInfo(description.Span, texts, ref created, ref generatedBy);
      return;
    }

    var draft = new _Draft {
      Number = number,
      Name = _OrNull(name),
      Kind = mimeType.Contains("realvideo", StringComparison.Ordinal) ? MediaStreamKind.Video
        : mimeType.Contains("realaudio", StringComparison.Ordinal) ? MediaStreamKind.Audio
        : mimeType.Contains("realtext", StringComparison.Ordinal) ? MediaStreamKind.Subtitle
        : MediaStreamKind.Data,
    };

    switch (draft.Kind) {
      case MediaStreamKind.Video:
        _DescribeVideo(description, draft);
        break;

      case MediaStreamKind.Audio:
        _DescribeAudio(description, draft);
        break;

      default:
        draft.Private = description;
        break;
    }

    drafts.Add(draft);
  }

  /// <summary>
  /// Reads a video stream's description.
  /// </summary>
  /// <remarks>
  /// The bytes past the fixed fields are the codec's own and are handed across verbatim as
  /// <see cref="MediaStreamInfo.CodecPrivateData"/>. For RealVideo they carry the bitstream version
  /// the pictures are coded to, which no field of the container states and which a decoder cannot do
  /// without — and which is exactly the sort of thing a container has no business interpreting.
  /// </remarks>
  private static void _DescribeVideo(ReadOnlyMemory<byte> description, _Draft draft) {
    var span = description.Span;
    if (span.Length < _VIDEO_DESCRIPTION || BinaryPrimitives.ReadUInt32BigEndian(span[4..]) != _VIDEO_MARKER)
      return;

    draft.Codec = _Code(span[8..]);
    draft.Width = BinaryPrimitives.ReadUInt16BigEndian(span[12..]);
    draft.Height = BinaryPrimitives.ReadUInt16BigEndian(span[14..]);
    draft.BitsPerPixel = BinaryPrimitives.ReadUInt16BigEndian(span[16..]);
    draft.FrameRate = _FixedPointRate(BinaryPrimitives.ReadUInt32BigEndian(span[22..]));
    draft.Private = description[_VIDEO_DESCRIPTION..];
  }

  /// <summary>
  /// Reads a sound stream's description, which is a RealAudio header.
  /// </summary>
  /// <remarks>
  /// Only the code naming the codec is taken out of it. The rest — the sample rate, the channel count,
  /// the interleaving the sound was written with — has nowhere to go in
  /// <see cref="MediaStreamInfo"/>, which describes pictures, and would be a description this library
  /// has no model for. The whole header is handed across as the stream's private data, so nothing is
  /// lost and whoever decodes the sound reads it there.
  /// </remarks>
  private static void _DescribeAudio(ReadOnlyMemory<byte> description, _Draft draft) {
    draft.Private = description;

    var span = description.Span;
    if (span.Length < 6)
      return;

    var version = BinaryPrimitives.ReadUInt16BigEndian(span[4..]);
    var at = version switch {
      4 => _AUDIO_V4_CODEC,
      5 => _AUDIO_V5_CODEC,
      _ => -1,
    };

    if (at >= 0 && at + 4 <= span.Length)
      draft.Codec = _Code(span[at..]);
  }

  /// <summary>
  /// Turns the four characters of a code into the tag the rest of this library names codecs with.
  /// </summary>
  /// <remarks>
  /// RealMedia writes its codes the way they read; a <see cref="CodecTag"/> holds the four bytes as
  /// one little-endian number, which is the order an AVI's would sit in. So the bytes are packed
  /// rather than read as a number, and <c>RV20</c> here is the same tag <c>RV20</c> would be anywhere.
  /// </remarks>
  private static CodecTag _Code(ReadOnlySpan<byte> at)
    => new(at[0] | ((uint)at[1] << 8) | ((uint)at[2] << 16) | ((uint)at[3] << 24));

  /// <summary>
  /// Turns a rate written as a sixteen-bit whole part and a sixteen-bit fraction into an exact ratio.
  /// </summary>
  /// <remarks>
  /// Reduced but not rounded to anything nicer. A file stating 0x001DF852 is stating 981801/32768,
  /// which is 29.96999 and not 30000/1001 — turning the one into the other would report a rate the
  /// file never claimed, however much it looks like the rate that was meant.
  /// </remarks>
  private static Rational _FixedPointRate(uint fixedPoint) {
    if (fixedPoint == 0)
      return Rational.Unknown;

    long numerator = fixedPoint;
    long denominator = 1 << 16;
    var divisor = _GreatestCommonDivisor(numerator, denominator);
    return new(numerator / divisor, denominator / divisor);
  }

  private static long _GreatestCommonDivisor(long a, long b) {
    while (b != 0)
      (a, b) = (b, a % b);

    return a == 0 ? 1 : a;
  }

  // ------------------------------------------------------------------------------------------
  // Metadata
  // ------------------------------------------------------------------------------------------

  /// <summary>Reads the content description: title, author, copyright and comment, each length-prefixed.</summary>
  private static void _ReadContent(
    ReadOnlySpan<byte> span, int at, int end,
    ref string? title, ref string? author, ref string? copyright, ref string? comment) {
    var cursor = at;
    if (!_TryReadWideString(span, ref cursor, end, out var readTitle)
        || !_TryReadWideString(span, ref cursor, end, out var readAuthor)
        || !_TryReadWideString(span, ref cursor, end, out var readCopyright)
        || !_TryReadWideString(span, ref cursor, end, out var readComment))
      return;

    title ??= readTitle;
    author ??= readAuthor;
    copyright ??= readCopyright;
    comment ??= readComment;
  }

  /// <summary>
  /// Reads the name-and-value pairs of the chunk that describes the file rather than a stream.
  /// </summary>
  /// <remarks>
  /// Each pair states its own whole length, so one whose type this does not read is stepped over
  /// rather than guessed at. Only the text ones are kept: the numeric ones are a player's settings —
  /// whether the file may be seeked in, which bandwidth alternative to prefer — and are statements
  /// about how to play the file rather than about the work.
  /// </remarks>
  private static void _ReadFileInfo(
    ReadOnlySpan<byte> span, List<TextMetadataEntry> texts, ref DateTimeOffset? created, ref string? generatedBy) {
    // A total length, a reserved word and the number of pairs that follow.
    if (span.Length < 12)
      return;

    var count = (int)BinaryPrimitives.ReadUInt32BigEndian(span[8..]);
    var cursor = 12;

    for (var i = 0; i < count && cursor + 4 <= span.Length; ++i) {
      var size = (int)BinaryPrimitives.ReadUInt32BigEndian(span[cursor..]);
      if (size < 4 || cursor + size > span.Length)
        return;

      var next = cursor + size;
      var at = cursor + 4;

      // One byte this reader has found no meaning for, then the name's length.
      if (at + 3 > next)
        return;

      var nameLength = BinaryPrimitives.ReadUInt16BigEndian(span[(at + 1)..]);
      at += 3;
      if (at + nameLength + 6 > next)
        return;

      var name = _Text(span.Slice(at, nameLength));
      at += nameLength;

      var kind = BinaryPrimitives.ReadUInt32BigEndian(span[at..]);
      var valueLength = BinaryPrimitives.ReadUInt16BigEndian(span[(at + 4)..]);
      at += 6;
      if (at + valueLength > next)
        return;

      // Type two is text; the others are numbers, and a number has no place in a text entry.
      if (kind == 2 && name.Length > 0) {
        var value = _Text(span.Slice(at, valueLength));
        switch (name) {
          case "Generated By":
            generatedBy ??= _OrNull(value);
            break;

          case "Creation Date":
            created ??= _Instant(value);
            if (created == null)
              _Text(texts, name, value);

            break;

          default:
            _Text(texts, name, value);
            break;
        }
      }

      cursor = next;
    }
  }

  /// <summary>
  /// Reads a creation date, which these files write as a local date and time with no zone at all.
  /// </summary>
  /// <remarks>
  /// Read as universal because there is nothing else to read it as. The writer's zone is not in the
  /// file, so any other choice would be this reader's guess about where the recording was made rather
  /// than anything the file states.
  /// </remarks>
  private static DateTimeOffset? _Instant(string value)
    => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
      ? parsed
      : null;

  private static void _Text(List<TextMetadataEntry> texts, string keyword, string? value) {
    if (!string.IsNullOrWhiteSpace(value))
      texts.Add(new(keyword, value!.Trim()));
  }

  // ------------------------------------------------------------------------------------------
  // Strings
  // ------------------------------------------------------------------------------------------

  /// <summary>Reads a string introduced by one byte of length.</summary>
  private static bool _TryReadByteString(ReadOnlySpan<byte> span, ref int cursor, int end, out string value) {
    value = string.Empty;
    if (cursor + 1 > end)
      return false;

    var length = span[cursor];
    ++cursor;
    if (cursor + length > end)
      return false;

    value = _Text(span.Slice(cursor, length));
    cursor += length;
    return true;
  }

  /// <summary>Reads a string introduced by two bytes of length.</summary>
  private static bool _TryReadWideString(ReadOnlySpan<byte> span, ref int cursor, int end, out string value) {
    value = string.Empty;
    if (cursor + 2 > end)
      return false;

    var length = BinaryPrimitives.ReadUInt16BigEndian(span[cursor..]);
    cursor += 2;
    if (cursor + length > end)
      return false;

    value = _Text(span.Slice(cursor, length));
    cursor += length;
    return true;
  }

  /// <summary>
  /// Turns a run of bytes into text.
  /// </summary>
  /// <remarks>
  /// Latin-1, because the format predates any statement about encoding and its writers wrote whatever
  /// their machine used: the files here carry titles in Windows-1252 and in Chinese code pages alike,
  /// with nothing anywhere saying which. Latin-1 is the one reading that never fails and never throws
  /// bytes away, so text that was written in another code page comes back as recoverable mojibake
  /// rather than as replacement characters. A trailing terminator is dropped where a writer left one.
  /// </remarks>
  private static string _Text(ReadOnlySpan<byte> bytes) {
    while (bytes.Length > 0 && bytes[^1] == 0)
      bytes = bytes[..^1];

    return bytes.IsEmpty ? string.Empty : Encoding.Latin1.GetString(bytes);
  }

  private static string? _OrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
