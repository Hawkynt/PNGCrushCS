using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FileFormat.Bmp;
using FileFormat.Core;

namespace FileFormat.Asf;

/// <summary>
/// Takes an ASF file's structure apart: which streams it declares, what it says about itself, and
/// where its packets are.
/// </summary>
/// <remarks>
/// The format is a tree of objects, each named by a sixteen-byte identifier and stating its own
/// length (clause 3). The Header Object holds the descriptions — File Properties, one Stream
/// Properties per stream, a Header Extension holding the objects added after the format was first
/// published, and whatever text the writer attached — and the Data Object holds the packets. Nothing
/// of the Data Object is read here; only the header, which is a few hundred bytes whatever the length
/// of the recording, and the packets are walked on demand by
/// <see cref="AsfContainer.ReadPackets(AsfContainer)"/>.
/// <para/>
/// Because every object states its own length, an unknown one costs nothing: a file carrying digital
/// rights management, a mutual exclusion object, a bandwidth sharing object or an index is walked
/// past exactly as fast as one carrying none of them. That is why this reader is complete for ASF
/// while decoding no codec at all — <c>.wmv</c>, <c>.wma</c> and <c>.asf</c> are one format, and which
/// codec is inside is somebody else's question.
/// </remarks>
public static class AsfReader {

  /// <summary>Fixed fields of the Header Object before its children: a count and two reserved bytes.</summary>
  private const int _HEADER_PREFIX = 6;

  /// <summary>Fixed fields of the Header Extension Object before its children (clause 3.4).</summary>
  private const int _HEADER_EXTENSION_PREFIX = AsfGuid.SIZE + 2 + 4;

  /// <summary>Fixed fields of the Data Object before its packets: a file identifier, a count, two reserved bytes.</summary>
  private const int _DATA_PREFIX = AsfGuid.SIZE + 8 + 2;

  /// <summary>Fixed fields of a Stream Properties Object before its type-specific data (clause 3.3).</summary>
  private const int _STREAM_PROPERTIES_PREFIX = AsfGuid.SIZE + AsfGuid.SIZE + 8 + 4 + 4 + 2 + 4;

  /// <summary>Fixed fields of an Extended Stream Properties Object before its stream names (clause 4.1).</summary>
  private const int _EXTENDED_STREAM_PROPERTIES_PREFIX = 64;

  /// <summary>Fixed fields of a video stream's type-specific data before its format data (clause 11.2).</summary>
  private const int _VIDEO_TYPE_PREFIX = 4 + 4 + 1 + 2;

  /// <summary>100-nanosecond units in a second, which is what the format counts durations in.</summary>
  private const long _UNITS_PER_SECOND = 10_000_000L;

  /// <summary>
  /// ASF states every timestamp in milliseconds, so one tick of a stream's clock is a thousandth of a
  /// second — for every stream in every file, since the format has no per-stream time base at all.
  /// </summary>
  private static readonly Rational _TIME_BASE = new(1, 1000);

  /// <summary>Reads an instance from the specified file.</summary>
  public static AsfContainer FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("ASF file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  /// <summary>Reads an instance from the specified stream.</summary>
  public static AsfContainer FromStream(Stream stream) {
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
  public static AsfContainer FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);

    return _Parse(data);
  }

  /// <summary>
  /// Reads an ASF file out of a span.
  /// </summary>
  /// <remarks>
  /// The bytes are copied once here, and only here. A container has to outlive the call that built it —
  /// its packets are windows onto the file and are walked long afterwards — and a span promises nothing
  /// about how long the memory behind it stays valid. Callers that already hold an array should use
  /// <see cref="FromBytes"/>, which keeps theirs.
  /// </remarks>
  public static AsfContainer FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < AsfObjectScanner.HEADER_SIZE)
      throw new InvalidDataException("Data is too small to be a valid ASF file.");

    return _Parse(data.ToArray());
  }

  private static AsfContainer _Parse(byte[] data) {
    if (data.Length < AsfObjectScanner.HEADER_SIZE + _HEADER_PREFIX)
      throw new InvalidDataException("Data is too small to be a valid ASF file.");

    var memory = new ReadOnlyMemory<byte>(data);
    if (!AsfGuid.Equals(memory.Span, AsfGuid.Header))
      throw new InvalidDataException($"Invalid ASF signature: expected a Header Object, got {AsfGuid.ToText(memory.Span)}.");

    var headerSize = BinaryPrimitives.ReadUInt64LittleEndian(memory.Span.Slice(AsfGuid.SIZE, 8));
    if (headerSize < AsfObjectScanner.HEADER_SIZE + _HEADER_PREFIX)
      throw new InvalidDataException($"The Header Object states an impossible size of {headerSize} bytes.");

    // The declared size is the writer's claim; the array is the fact. A file cut short keeps whatever
    // was written, which is what every other tool reads out of it too.
    var headerEnd = headerSize > (ulong)data.Length ? data.Length : (int)headerSize;

    var header = _ReadHeader(memory, headerEnd);
    var (dataStart, dataEnd) = _FindDataObject(memory, headerEnd);

    var streams = _BuildStreams(header);
    var numbers = new int[128];
    Array.Fill(numbers, -1);
    for (var i = 0; i < header.Streams.Count; ++i) {
      var number = header.Streams[i].Number;
      if ((uint)number < (uint)numbers.Length)
        numbers[number] = i;
    }

    var properties = header.FileProperties;

    return new() {
      StreamInfos = streams,
      FileMetadata = _BuildMetadata(header, streams),
      File = memory,
      DataStart = dataStart,
      DataEnd = dataEnd,
      // A broadcast was written without knowing how much there would be, so its count is not a count.
      // Walking to the end of the object is the only thing that reads such a file at all.
      PacketCount = properties is { IsBroadcast: false } ? (long)properties.DataPacketCount : 0L,
      PacketSize = (int)properties.MaximumPacketSize,
      Preroll = (long)properties.Preroll,
      StreamIndexByNumber = numbers,
    };
  }

  /// <summary>Finds the Data Object among the file's top-level objects.</summary>
  /// <remarks>
  /// By walking rather than by assuming it follows the header. The specification allows the top level
  /// to hold objects in any order, and a file that has been indexed carries a Simple Index Object as
  /// well — which sits after the data in every file anyone writes, but the format does not say it must.
  /// </remarks>
  private static (int Start, int End) _FindDataObject(ReadOnlyMemory<byte> file, int headerEnd) {
    foreach (var element in AsfObjectScanner.Walk(file, headerEnd, file.Length)) {
      if (!element.Is(AsfGuid.Data))
        continue;

      var start = element.Offset + AsfObjectScanner.HEADER_SIZE + _DATA_PREFIX;
      var end = element.Offset + AsfObjectScanner.HEADER_SIZE + element.Body.Length;
      return start > end ? (end, end) : (start, end);
    }

    throw new InvalidDataException("Missing the Data Object; the file declares streams but holds no packets.");
  }

  // ------------------------------------------------------------------------------------------
  // The header
  // ------------------------------------------------------------------------------------------

  /// <summary>Everything the Header Object had to say, collected before any of it is interpreted.</summary>
  /// <remarks>
  /// Two passes rather than one because the objects do not arrive in the order they depend on each
  /// other. A stream's language is a number that indexes the Language List Object, its frame rate is
  /// stated in an Extended Stream Properties Object, and either may sit before or after the Stream
  /// Properties Object it describes — a reader that applied each object as it met it would resolve
  /// half of them against a table it had not read yet.
  /// </remarks>
  private sealed class AsfHeaderContents {
    public AsfFileProperties FileProperties { get; set; }
    public bool HasFileProperties { get; set; }
    public List<AsfStreamDeclaration> Streams { get; } = [];
    public Dictionary<int, AsfExtendedStream> ExtendedStreams { get; } = [];
    public List<string> Languages { get; } = [];
    public string? Title { get; set; }
    public string? Author { get; set; }
    public string? Copyright { get; set; }
    public string? Description { get; set; }
    public string? Rating { get; set; }
    public string? Album { get; set; }
    public string? EncodedBy { get; set; }
    public List<TextMetadataEntry> Texts { get; } = [];
    public List<CoverArt> Covers { get; } = [];
  }

  /// <summary>One Stream Properties Object, read but not yet turned into a stream description.</summary>
  private readonly record struct AsfStreamDeclaration(
    int Number,
    MediaStreamKind Kind,
    CodecTag Codec,
    int Width,
    int Height,
    int BitsPerPixel,
    ReadOnlyMemory<byte> FormatData);

  /// <summary>What an Extended Stream Properties Object adds to a stream (clause 4.1).</summary>
  private readonly record struct AsfExtendedStream(long AverageTimePerFrame, int LanguageIndex, string? Name);

  private static AsfHeaderContents _ReadHeader(ReadOnlyMemory<byte> file, int headerEnd) {
    var contents = new AsfHeaderContents();
    var seenNumbers = new HashSet<int>();

    foreach (var element in AsfObjectScanner.Walk(file, AsfObjectScanner.HEADER_SIZE + _HEADER_PREFIX, headerEnd)) {
      if (element.Is(AsfGuid.FileProperties)) {
        if (!contents.HasFileProperties && element.Body.Length >= AsfFileProperties.STRUCT_SIZE) {
          contents.FileProperties = AsfFileProperties.ReadFrom(element.Body);
          contents.HasFileProperties = true;
        }

        continue;
      }

      if (element.Is(AsfGuid.StreamProperties)) {
        if (_TryReadStreamProperties(element.Body, out var declaration) && seenNumbers.Add(declaration.Number))
          contents.Streams.Add(declaration);

        continue;
      }

      if (element.Is(AsfGuid.ContentDescription)) {
        _ReadContentDescription(element.Body, contents);
        continue;
      }

      if (element.Is(AsfGuid.ExtendedContentDescription)) {
        _ReadExtendedContentDescription(element.Body, contents);
        continue;
      }

      if (element.Is(AsfGuid.CodecList)) {
        _ReadCodecList(element.Body, contents);
        continue;
      }

      if (element.Is(AsfGuid.HeaderExtension))
        _ReadHeaderExtension(file, element, contents, seenNumbers);

      // Anything else — a Codec List, a Stream Bitrate Properties, a rights management object, a
      // padding object — states its own length and is stepped over by the walk. An unrecognised object
      // is not a broken file, it is a file written by something that knew one more object than this.
    }

    if (!contents.HasFileProperties)
      throw new InvalidDataException("Missing the File Properties Object; the file states no packet size to read its packets by.");

    if (contents.FileProperties.MaximumPacketSize == 0)
      throw new InvalidDataException("The File Properties Object states a maximum packet size of zero.");

    return contents;
  }

  /// <summary>Walks the objects that were added to the format after it was first published (clause 3.4).</summary>
  private static void _ReadHeaderExtension(
    ReadOnlyMemory<byte> file, AsfObject extension, AsfHeaderContents contents, HashSet<int> seenNumbers) {
    foreach (var element in AsfObjectScanner.Children(file, extension, _HEADER_EXTENSION_PREFIX)) {
      if (element.Is(AsfGuid.LanguageList)) {
        _ReadLanguageList(element.Body, contents);
        continue;
      }

      if (!element.Is(AsfGuid.ExtendedStreamProperties))
        continue;

      _ReadExtendedStreamProperties(file, element, contents, seenNumbers);
    }
  }

  /// <summary>Reads one Stream Properties Object (clause 3.3).</summary>
  private static bool _TryReadStreamProperties(ReadOnlyMemory<byte> body, out AsfStreamDeclaration declaration) {
    declaration = default;
    if (body.Length < _STREAM_PROPERTIES_PREFIX)
      return false;

    var span = body.Span;
    var typeSpecificLength = BinaryPrimitives.ReadUInt32LittleEndian(span[(AsfGuid.SIZE + AsfGuid.SIZE + 8)..]);
    var flags = BinaryPrimitives.ReadUInt16LittleEndian(span[(AsfGuid.SIZE + AsfGuid.SIZE + 8 + 4 + 4)..]);

    // The low seven bits and no more. Bit 15 says the content is encrypted and the rest are reserved, so
    // a reader that took the field whole would find no stream of that number in any protected file.
    var number = flags & 0x7F;

    // Stream number zero is reserved and names no stream (clause 3.3); a payload can never refer to it,
    // so a declaration carrying it describes nothing that will ever be demultiplexed.
    if (number == 0)
      return false;

    // Compared unsigned. The stated length is a 32-bit number and a file may state one with its top bit
    // set; narrowed to a signed integer first, that is a negative length, and the comparison that was
    // meant to clamp it to what is there passes it straight through.
    var available = body.Length - _STREAM_PROPERTIES_PREFIX;
    var typeSpecific = body.Slice(
      _STREAM_PROPERTIES_PREFIX, typeSpecificLength > (uint)available ? available : (int)typeSpecificLength);

    if (AsfGuid.Equals(span, AsfGuid.VideoMedia)) {
      declaration = _ReadVideoStream(number, typeSpecific);
      return true;
    }

    if (AsfGuid.Equals(span, AsfGuid.AudioMedia)) {
      // A sound stream's type-specific data is a WAVEFORMATEX whose first field is the format tag.
      // Reading that one number is not decoding sound; it is what lets a muxer copy the stream across
      // intact, and it is the same thing the video branch does with biCompression.
      var tag = typeSpecific.Length >= 2 ? new CodecTag(BinaryPrimitives.ReadUInt16LittleEndian(typeSpecific.Span)) : CodecTag.None;
      declaration = new(number, MediaStreamKind.Audio, tag, 0, 0, 0, typeSpecific);
      return true;
    }

    var kind = AsfGuid.Equals(span, AsfGuid.CommandMedia) || AsfGuid.Equals(span, AsfGuid.BinaryMedia)
      ? MediaStreamKind.Data
      : MediaStreamKind.Unknown;

    declaration = new(number, kind, CodecTag.None, 0, 0, 0, typeSpecific);
    return true;
  }

  /// <summary>Reads a video stream's type-specific data, which is a header ASF borrows from Windows.</summary>
  /// <remarks>
  /// The format data is a <c>BITMAPINFOHEADER</c>, which looks like knowledge of a codec and is not:
  /// the specification says that is what a video stream's format data is (clause 11.2), whatever the
  /// frames themselves turn out to be. What that header describes beyond width, height and depth is
  /// the codec's business, so it is also carried across whole as
  /// <see cref="MediaStreamInfo.CodecPrivateData"/> — bytes past the fortieth included, which is where
  /// a Windows Media Video 9 stream keeps its sequence header.
  /// <para/>
  /// The encoded width and height that precede it are read in preference to the ones inside it only
  /// when the inner ones say nothing. They are the same number in every file anyone writes, and where
  /// they disagree it is the format data that a decoder is handed.
  /// </remarks>
  private static AsfStreamDeclaration _ReadVideoStream(int number, ReadOnlyMemory<byte> typeSpecific) {
    if (typeSpecific.Length < _VIDEO_TYPE_PREFIX + BitmapInfoHeader.StructSize)
      throw new InvalidDataException(
        $"Video stream {number} states {typeSpecific.Length} bytes of type-specific data, too few for a bitmap header.");

    var span = typeSpecific.Span;
    var encodedWidth = (int)BinaryPrimitives.ReadUInt32LittleEndian(span);
    var encodedHeight = (int)BinaryPrimitives.ReadUInt32LittleEndian(span[4..]);
    var formatDataSize = BinaryPrimitives.ReadUInt16LittleEndian(span[9..]);

    var available = typeSpecific.Length - _VIDEO_TYPE_PREFIX;
    var formatData = typeSpecific.Slice(_VIDEO_TYPE_PREFIX, Math.Min((int)formatDataSize, available));

    // The stated size is its own field and may claim less than a bitmap header takes, whatever room the
    // type-specific data actually leaves. Reading one out of fewer bytes than it occupies would read
    // past the end of the object rather than refuse the stream.
    if (formatData.Length < BitmapInfoHeader.StructSize)
      throw new InvalidDataException(
        $"Video stream {number} states {formatData.Length} bytes of format data, too few for a bitmap header.");

    var info = BitmapInfoHeader.ReadFrom(formatData.Span);
    var width = info.Width != 0 ? info.Width : encodedWidth;
    var height = info.Height != 0 ? info.Height : encodedHeight;

    if (width <= 0 || height == 0)
      throw new InvalidDataException($"Video stream {number} states an impossible size of {width}x{height}.");

    return new(
      number,
      MediaStreamKind.Video,
      new CodecTag((uint)info.Compression),
      width,
      // Always positive. The sign of biHeight says which way the rows run, which is a property of the
      // pixels and therefore the codec's to read out of the header it was handed.
      Math.Abs(height),
      info.BitsPerPixel,
      formatData);
  }

  /// <summary>Reads an Extended Stream Properties Object, and any stream declared only inside one (clause 4.1).</summary>
  /// <remarks>
  /// The optional Stream Properties Object at the tail is the reason this walks rather than just reads
  /// fixed fields. A file may declare a stream nowhere else, and one skipped here is a stream whose
  /// packets would be handed out under a number nothing had described.
  /// </remarks>
  private static void _ReadExtendedStreamProperties(
    ReadOnlyMemory<byte> file, AsfObject element, AsfHeaderContents contents, HashSet<int> seenNumbers) {
    var body = element.Body;
    if (body.Length < _EXTENDED_STREAM_PROPERTIES_PREFIX)
      return;

    var span = body.Span;
    var number = BinaryPrimitives.ReadUInt16LittleEndian(span[48..]) & 0x7F;
    var languageIndex = BinaryPrimitives.ReadUInt16LittleEndian(span[50..]);
    var averageTimePerFrame = (long)BinaryPrimitives.ReadUInt64LittleEndian(span[52..]);
    var nameCount = BinaryPrimitives.ReadUInt16LittleEndian(span[60..]);
    var extensionCount = BinaryPrimitives.ReadUInt16LittleEndian(span[62..]);

    var at = _EXTENDED_STREAM_PROPERTIES_PREFIX;
    string? name = null;

    for (var i = 0; i < nameCount && at + 4 <= body.Length; ++i) {
      var length = BinaryPrimitives.ReadUInt16LittleEndian(span[(at + 2)..]);
      at += 4;
      if (at + length > body.Length)
        return;

      name ??= _Utf16(span.Slice(at, length));
      at += length;
    }

    // The payload extension systems are stepped over rather than read: what they describe is extra data
    // attached to each payload, which this reader does not hand out, and their combined length is the
    // only way to reach the Stream Properties Object that may follow them.
    for (var i = 0; i < extensionCount && at + 22 <= body.Length; ++i) {
      var infoLength = BinaryPrimitives.ReadUInt32LittleEndian(span[(at + 18)..]);
      at += 22;
      if (infoLength > (ulong)(body.Length - at))
        return;

      at += (int)infoLength;
    }

    if (number != 0)
      contents.ExtendedStreams[number] = new(averageTimePerFrame, languageIndex, name);

    var tailStart = element.Offset + AsfObjectScanner.HEADER_SIZE + at;
    var tailEnd = element.Offset + AsfObjectScanner.HEADER_SIZE + body.Length;
    foreach (var embedded in AsfObjectScanner.Walk(file, tailStart, tailEnd)) {
      if (!embedded.Is(AsfGuid.StreamProperties))
        continue;

      if (_TryReadStreamProperties(embedded.Body, out var declaration) && seenNumbers.Add(declaration.Number))
        contents.Streams.Add(declaration);
    }
  }

  /// <summary>Reads the Language List Object, whose entries the streams refer to by position (clause 4.6).</summary>
  private static void _ReadLanguageList(ReadOnlyMemory<byte> body, AsfHeaderContents contents) {
    if (body.Length < 2 || contents.Languages.Count > 0)
      return;

    var span = body.Span;
    var count = BinaryPrimitives.ReadUInt16LittleEndian(span);
    var at = 2;

    for (var i = 0; i < count && at < body.Length; ++i) {
      var length = span[at++];
      if (at + length > body.Length)
        return;

      contents.Languages.Add(_Utf16(span.Slice(at, length)) ?? string.Empty);
      at += length;
    }
  }

  /// <summary>
  /// Reads the Codec List Object: what the writer called each codec it used (clause 3.5).
  /// </summary>
  /// <remarks>
  /// Names for people rather than anything a decoder acts on — "Windows Media Video 9" beside the
  /// four-character code <c>WMV3</c> that actually selects the decoder. They are kept as annotations
  /// because they are the only place a file says in words what is inside it, which is worth having when
  /// the code names a codec nothing here reads.
  /// <para/>
  /// The two string lengths in an entry count characters where every other length in the format counts
  /// bytes, and the third counts bytes again. Reading all three the same way walks off the end of the
  /// entry and misreads every one after it.
  /// </remarks>
  private static void _ReadCodecList(ReadOnlyMemory<byte> body, AsfHeaderContents contents) {
    if (body.Length < AsfGuid.SIZE + 4)
      return;

    var span = body.Span;
    var count = BinaryPrimitives.ReadUInt32LittleEndian(span[AsfGuid.SIZE..]);
    var at = AsfGuid.SIZE + 4;

    for (var i = 0; i < count; ++i) {
      if (at + 4 > body.Length)
        return;

      var kind = BinaryPrimitives.ReadUInt16LittleEndian(span[at..]);
      var nameCharacters = BinaryPrimitives.ReadUInt16LittleEndian(span[(at + 2)..]);
      at += 4;
      if (at + (nameCharacters * 2) > body.Length)
        return;

      var name = _Utf16(span.Slice(at, nameCharacters * 2));
      at += nameCharacters * 2;

      if (at + 2 > body.Length)
        return;

      var descriptionCharacters = BinaryPrimitives.ReadUInt16LittleEndian(span[at..]);
      at += 2;
      if (at + (descriptionCharacters * 2) > body.Length)
        return;

      var description = _Utf16(span.Slice(at, descriptionCharacters * 2));
      at += descriptionCharacters * 2;

      if (at + 2 > body.Length)
        return;

      var informationLength = BinaryPrimitives.ReadUInt16LittleEndian(span[at..]);
      at += 2 + informationLength;

      if (name == null)
        continue;

      var keyword = kind switch {
        1 => "Video Codec",
        2 => "Audio Codec",
        _ => "Codec",
      };

      contents.Texts.Add(new(keyword, description == null ? name : $"{name} ({description})"));
    }
  }

  /// <summary>Reads the Content Description Object: five strings, each stated by length (clause 3.10).</summary>
  private static void _ReadContentDescription(ReadOnlyMemory<byte> body, AsfHeaderContents contents) {
    if (body.Length < 10)
      return;

    var span = body.Span;
    Span<int> lengths = stackalloc int[5];
    for (var i = 0; i < 5; ++i)
      lengths[i] = BinaryPrimitives.ReadUInt16LittleEndian(span[(i * 2)..]);

    var at = 10;
    var values = new string?[5];
    for (var i = 0; i < 5; ++i) {
      if (at + lengths[i] > body.Length)
        break;

      values[i] = _Utf16(span.Slice(at, lengths[i]));
      at += lengths[i];
    }

    contents.Title ??= values[0];
    contents.Author ??= values[1];
    contents.Copyright ??= values[2];
    contents.Description ??= values[3];
    contents.Rating ??= values[4];
  }

  /// <summary>
  /// Reads the Extended Content Description Object: any number of named values (clause 3.11).
  /// </summary>
  /// <remarks>
  /// This is where everything the five fixed fields of the Content Description Object have no room for
  /// ends up — the album, the encoder, the track number, and the cover picture. The names are
  /// Microsoft's own, <c>WM/AlbumTitle</c> and the like, and the ones that have a home in
  /// <see cref="VideoMetadata"/> are put there; the rest are kept as annotations under the name the
  /// file gave them rather than dropped, because a reader that dropped what it had no field for would
  /// be indistinguishable from a file that never carried it.
  /// </remarks>
  private static void _ReadExtendedContentDescription(ReadOnlyMemory<byte> body, AsfHeaderContents contents) {
    if (body.Length < 2)
      return;

    var span = body.Span;
    var count = BinaryPrimitives.ReadUInt16LittleEndian(span);
    var at = 2;

    for (var i = 0; i < count; ++i) {
      if (at + 2 > body.Length)
        return;

      var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(span[at..]);
      at += 2;
      if (at + nameLength + 4 > body.Length)
        return;

      var name = _Utf16(span.Slice(at, nameLength));
      at += nameLength;

      var dataType = BinaryPrimitives.ReadUInt16LittleEndian(span[at..]);
      var valueLength = BinaryPrimitives.ReadUInt16LittleEndian(span[(at + 2)..]);
      at += 4;
      if (at + valueLength > body.Length)
        return;

      var value = body.Slice(at, valueLength);
      at += valueLength;

      if (name == null)
        continue;

      // A picture is a byte array under a name that says so. Everything else that is not text is a
      // number the format spells four different ways, and a number rendered as text is what an
      // annotation is for.
      if (dataType == 1) {
        if (name == "WM/Picture") {
          var cover = _ReadPicture(value);
          if (cover != null)
            contents.Covers.Add(cover);
        }

        continue;
      }

      var text = dataType switch {
        0 => _Utf16(value.Span),
        2 => value.Length >= 4 ? (BinaryPrimitives.ReadUInt32LittleEndian(value.Span) != 0).ToString() : null,
        3 => value.Length >= 4 ? BinaryPrimitives.ReadUInt32LittleEndian(value.Span).ToString() : null,
        4 => value.Length >= 8 ? BinaryPrimitives.ReadUInt64LittleEndian(value.Span).ToString() : null,
        5 => value.Length >= 2 ? BinaryPrimitives.ReadUInt16LittleEndian(value.Span).ToString() : null,
        _ => null,
      };

      if (text == null)
        continue;

      // Compared without regard to case because the writers disagree about it: Microsoft's own tools
      // write "Title" and "WM/AlbumTitle" where ffmpeg writes "title" and "album" for the same field.
      // Matching only one spelling would file the other as an annotation beside the value it duplicates,
      // and a file would appear to carry its title twice.
      switch (name.ToUpperInvariant()) {
        case "WM/ALBUMTITLE" or "ALBUM":
          contents.Album ??= text;
          break;
        case "WM/ENCODINGSETTINGS" or "WM/TOOLNAME" or "ENCODER":
          contents.EncodedBy ??= text;
          break;
        case "TITLE":
          contents.Title ??= text;
          break;
        case "AUTHOR" or "ARTIST" or "WM/AUTHOR":
          contents.Author ??= text;
          break;
        case "COPYRIGHT" or "WM/COPYRIGHT":
          contents.Copyright ??= text;
          break;
        case "DESCRIPTION" or "COMMENT" or "WM/DESCRIPTION":
          contents.Description ??= text;
          break;
        default:
          contents.Texts.Add(new(name, text));
          break;
      }
    }
  }

  /// <summary>
  /// Turns a <c>WM/Picture</c> value into cover art, keeping the picture in the format it was embedded as.
  /// </summary>
  /// <remarks>
  /// The layout is the one the format borrows from ID3: a byte saying what the picture is for, a length,
  /// then the media type and the caption as null-terminated wide strings, then the picture. The stated
  /// length is the picture's and not the record's, so it says where the picture starts only once the
  /// two strings have been walked past.
  /// </remarks>
  private static CoverArt? _ReadPicture(ReadOnlyMemory<byte> value) {
    if (value.Length < 5)
      return null;

    var span = value.Span;
    var kind = span[0];
    var length = BinaryPrimitives.ReadUInt32LittleEndian(span[1..]);
    var at = 5;

    var mime = _ReadTerminated(span, ref at);
    var description = _ReadTerminated(span, ref at);
    if (at > value.Length)
      return null;

    var available = value.Length - at;
    var picture = value.Slice(at, length > (ulong)available ? available : (int)length);
    if (picture.IsEmpty)
      return null;

    // The picture goes across in the format it was embedded in and is not decoded. That is what a muxer
    // writing another container has to hand over, and decoding it first could only lose the original.
    return new(picture.ToArray(), mime, description, _PictureKind(kind));
  }

  /// <summary>What the picture is for, as the ID3 table the format borrows names it.</summary>
  private static string _PictureKind(byte kind) => kind switch {
    0x03 => "cover",
    0x04 => "back cover",
    0x05 => "leaflet",
    0x06 => "media",
    _ => "other",
  };

  /// <summary>Reads one null-terminated wide string and leaves the cursor past its terminator.</summary>
  private static string? _ReadTerminated(ReadOnlySpan<byte> span, ref int at) {
    var start = at;
    while (at + 1 < span.Length && !(span[at] == 0 && span[at + 1] == 0))
      at += 2;

    var text = at > start ? Encoding.Unicode.GetString(span[start..at]) : null;
    at += 2;
    return text;
  }

  // ------------------------------------------------------------------------------------------
  // Assembling what was read
  // ------------------------------------------------------------------------------------------

  private static MediaStreamInfo[] _BuildStreams(AsfHeaderContents contents) {
    var streams = new MediaStreamInfo[contents.Streams.Count];

    for (var i = 0; i < streams.Length; ++i) {
      var declaration = contents.Streams[i];
      contents.ExtendedStreams.TryGetValue(declaration.Number, out var extended);

      var language = extended.LanguageIndex < contents.Languages.Count
        ? contents.Languages[extended.LanguageIndex]
        : null;

      // A frame rate is stated only where an Extended Stream Properties Object states one, and is left
      // unknown otherwise. The obvious substitute — dividing the duration by the number of packets — is
      // exactly the interpolation this library refuses to do: a container that states no rate has not
      // stated one, and a number invented here would be indistinguishable from a number in the file.
      var frameRate = declaration.Kind == MediaStreamKind.Video && extended.AverageTimePerFrame > 0
        ? new Rational(_UNITS_PER_SECOND, extended.AverageTimePerFrame)
        : Rational.Unknown;

      streams[i] = new() {
        Index = i,
        Kind = declaration.Kind,
        Codec = declaration.Codec,
        TimeBase = _TIME_BASE,
        FrameRate = frameRate,
        Width = declaration.Width,
        Height = declaration.Height,
        BitsPerPixel = declaration.BitsPerPixel,
        CodecPrivateData = declaration.FormatData,
        Language = string.IsNullOrEmpty(language) ? null : language,
        Name = extended.Name,
      };
    }

    return streams;
  }

  private static VideoMetadata _BuildMetadata(AsfHeaderContents contents, MediaStreamInfo[] streams) {
    var texts = new List<TextMetadataEntry>();
    if (contents.Copyright != null)
      texts.Add(new("Copyright", contents.Copyright));
    if (contents.Description != null)
      texts.Add(new("Description", contents.Description));
    if (contents.Rating != null)
      texts.Add(new("Rating", contents.Rating));

    texts.AddRange(contents.Texts);

    var streamMetadata = new MediaStreamMetadata[streams.Length];
    for (var i = 0; i < streams.Length; ++i)
      streamMetadata[i] = new(streams[i].Index, streams[i].Kind, streams[i].Codec, streams[i].Language, streams[i].Name);

    var properties = contents.FileProperties;

    return new() {
      Title = contents.Title,
      Artist = contents.Author,
      Album = contents.Album,
      EncodedBy = contents.EncodedBy,
      CreationTime = properties.CreationDate == 0 ? null : _TimeOf(properties.CreationDate),
      Duration = _DeclaredDuration(properties),
      Streams = streamMetadata,
      CoverArt = contents.Covers,
      TextEntries = texts,
    };
  }

  /// <summary>
  /// How long the file's own header claims it runs.
  /// </summary>
  /// <remarks>
  /// The play duration counts the preroll as well as the film, because it is how long the clock runs
  /// and the clock starts early — so the preroll comes off here for the same reason it comes off every
  /// timestamp. A broadcast states neither meaningfully, having been written before either was known.
  /// </remarks>
  private static TimeSpan? _DeclaredDuration(AsfFileProperties properties) {
    if (properties.IsBroadcast || properties.PlayDuration == 0)
      return null;

    var ticks = (long)properties.PlayDuration - ((long)properties.Preroll * (_UNITS_PER_SECOND / 1000));
    return ticks <= 0 ? TimeSpan.Zero : TimeSpan.FromTicks(ticks);
  }

  /// <summary>Turns a Windows file time into an instant.</summary>
  /// <remarks>
  /// The field counts 100-nanosecond units from 1601-01-01 UTC, which is neither the Unix epoch nor
  /// what any other container here counts from. A value outside what that clock can express is a
  /// writer that put something else in the field, and is reported as no creation time rather than as a
  /// date in the sixteenth century.
  /// </remarks>
  private static DateTimeOffset? _TimeOf(ulong fileTime) {
    try {
      return DateTimeOffset.FromFileTime((long)fileTime);
    } catch (ArgumentOutOfRangeException) {
      return null;
    }
  }

  /// <summary>Reads one of the format's strings, which are UTF-16 and may carry their terminator.</summary>
  private static string? _Utf16(ReadOnlySpan<byte> data) {
    // An odd length cannot be whole characters. The last byte is dropped rather than the string being
    // refused: what is there decodes, and a writer that miscounted by one has still said something.
    if ((data.Length & 1) != 0)
      data = data[..^1];

    if (data.IsEmpty)
      return null;

    var text = Encoding.Unicode.GetString(data);
    var end = text.IndexOf('\0');
    if (end >= 0)
      text = text[..end];

    return text.Length == 0 ? null : text;
  }
}
