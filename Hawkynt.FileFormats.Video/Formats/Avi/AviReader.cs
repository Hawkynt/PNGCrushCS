using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using FileFormat.Bmp;
using FileFormat.Core;
using FileFormat.Riff;

namespace FileFormat.Avi;

/// <summary>
/// Takes an AVI's structure apart: which streams it declares, what it says about itself, and where
/// its packets are.
/// </summary>
/// <remarks>
/// An AVI starts with a RIFF file of form <c>AVI </c>. <c>LIST hdrl</c> holds the main header and one
/// <c>LIST strl</c> per stream; <c>LIST movi</c> holds the packets, one chunk each, named for the
/// stream they belong to. OpenDML files may continue with <c>RIFF AVIX</c> chunks carrying additional
/// <c>movi</c> lists. None of that is codec-specific and all of it is shared with every other AVI,
/// which is why this reader keeps container structure separate from codec decoding.
/// <para/>
/// Nothing in <c>movi</c> is decoded here. Only the header lists and RIFF boundaries are parsed; the
/// packets are walked on demand by <see cref="AviContainer.ReadPackets(AviContainer)"/>.
/// </remarks>
public static class AviReader {

  private const string _FORM_TYPE = "AVI ";
  private const string _EXTENDED_FORM_TYPE = "AVIX";
  private const string _HEADER_LIST = "hdrl";
  private const string _STREAM_LIST = "strl";
  private const string _OPEN_DML_LIST = "odml";
  private const string _MOVIE_LIST = "movi";
  private const string _INFO_LIST = "INFO";
  private const string _MAIN_HEADER_ID = "avih";
  private const string _EXTENDED_HEADER_ID = "dmlh";
  private const string _STREAM_HEADER_ID = "strh";
  private const string _STREAM_FORMAT_ID = "strf";
  private const string _STREAM_NAME_ID = "strn";

  public static AviContainer FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("AVI file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AviContainer FromStream(Stream stream) {
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

  public static AviContainer FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);

    return _Parse(data);
  }

  /// <summary>
  /// Reads an AVI out of a span.
  /// </summary>
  /// <remarks>
  /// The bytes are copied once here, and only here. A container has to outlive the call that built
  /// it — its packets are windows onto the file and are walked long afterwards — and a span promises
  /// nothing about how long the memory behind it stays valid. Callers that already hold an array
  /// should use <see cref="FromBytes"/>, which keeps theirs.
  /// </remarks>
  public static AviContainer FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < RiffHeader.StructSize)
      throw new InvalidDataException("Data is too small to be a valid AVI file.");

    return _Parse(data.ToArray());
  }

  private static AviContainer _Parse(byte[] data) {
    if (data.Length < RiffHeader.StructSize)
      throw new InvalidDataException("Data is too small to be a valid AVI file.");

    var memory = new ReadOnlyMemory<byte>(data);
    var riffHeader = RiffHeader.ReadFrom(data);
    if (riffHeader.ChunkId.ToString() != "RIFF")
      throw new InvalidDataException("Invalid RIFF signature.");

    if (riffHeader.FormType.ToString() != _FORM_TYPE)
      throw new InvalidDataException($"Invalid AVI form type: expected '{_FORM_TYPE}', got '{riffHeader.FormType}'.");
    if (riffHeader.Size < 4)
      throw new InvalidDataException("Invalid AVI RIFF size: the form type is not included in the declared chunk.");

    var firstRiffEnd = _RiffEnd(0, riffHeader.Size, data.Length);
    RiffElement? headerList = null;
    RiffElement? infoList = null;
    var movieLists = new List<ReadOnlyMemory<byte>>();

    foreach (var element in RiffScanner.Walk(memory, RiffHeader.StructSize, firstRiffEnd)) {
      if (!element.IsList)
        continue;

      switch (element.ListType.ToString()) {
        case _HEADER_LIST:
          headerList ??= element;
          break;
        case _MOVIE_LIST:
          movieLists.Add(element.Body);
          break;
        case _INFO_LIST:
          infoList ??= element;
          break;
      }
    }

    if (headerList == null)
      throw new InvalidDataException($"Missing '{_HEADER_LIST}' list.");
    if (movieLists.Count == 0)
      throw new InvalidDataException($"Missing '{_MOVIE_LIST}' list.");

    for (var position = _NextRiffOffset(0, riffHeader.Size, data.Length); position < data.Length;) {
      if (data.Length - position < RiffHeader.StructSize)
        break; // a cut-off trailing RIFF header has no complete form type to inspect.

      var extendedHeader = RiffHeader.ReadFrom(data.AsSpan(position));
      if (extendedHeader.ChunkId.ToString() != "RIFF" || extendedHeader.FormType.ToString() != _EXTENDED_FORM_TYPE)
        break; // OpenDML extensions are consecutive RIFF AVIX chunks; unrelated trailing bytes are not AVI media.
      if (extendedHeader.Size < 4)
        throw new InvalidDataException("Invalid OpenDML AVIX RIFF size: the form type is not included in the declared chunk.");

      var extendedEnd = _RiffEnd(position, extendedHeader.Size, data.Length);
      foreach (var element in RiffScanner.Walk(memory, checked(position + RiffHeader.StructSize), extendedEnd))
        if (element.IsList && element.ListType.ToString() == _MOVIE_LIST)
          movieLists.Add(element.Body);

      var next = _NextRiffOffset(position, extendedHeader.Size, data.Length);
      if (next <= position)
        break;
      position = next;
    }

    var (header, streams, extendedTotalFrames) = _ReadHeaderList(headerList.Value);

    return new() {
      Header = header,
      StreamInfos = streams,
      MovieList = movieLists[0],
      MovieLists = movieLists,
      FileMetadata = _ReadMetadata(header, streams, infoList, extendedTotalFrames),
    };
  }

  private static int _RiffEnd(int start, uint size, int dataLength) {
    var declaredEnd = (long)start + 8 + size;
    return declaredEnd >= dataLength ? dataLength : checked((int)declaredEnd);
  }

  private static int _NextRiffOffset(int start, uint size, int dataLength) {
    var declaredEnd = (long)start + 8 + size;
    var next = declaredEnd + (size & 1u);
    return next >= dataLength ? dataLength : checked((int)next);
  }

  /// <summary>Reads <c>avih</c>, every <c>LIST strl</c>, and the OpenDML total-frame extension.</summary>
  private static (AviMainHeader Header, MediaStreamInfo[] Streams, uint? ExtendedTotalFrames) _ReadHeaderList(RiffElement headerList) {
    AviMainHeader? header = null;
    uint? extendedTotalFrames = null;
    var streams = new List<MediaStreamInfo>();

    foreach (var element in RiffScanner.Walk(headerList)) {
      if (!element.IsList) {
        if (element.Id.ToString() != _MAIN_HEADER_ID)
          continue;

        if (element.Body.Length < AviMainHeader.StructSize)
          throw new InvalidDataException(
            $"Invalid '{_MAIN_HEADER_ID}' chunk size: expected at least {AviMainHeader.StructSize}, got {element.Body.Length}.");

        header ??= AviMainHeader.ReadFrom(element.Body.Span);
        continue;
      }

      switch (element.ListType.ToString()) {
        case _STREAM_LIST:
          // Every strl counts towards the stream number, video or not: the two digits a packet chunk's
          // name starts with are the stream's position in this list, and skipping any of them would make
          // the rest go looking under the wrong name.
          streams.Add(_ReadStream(element, streams.Count));
          break;
        case _OPEN_DML_LIST:
          extendedTotalFrames ??= _ReadExtendedTotalFrames(element);
          break;
      }
    }

    if (header == null)
      throw new InvalidDataException($"Missing '{_MAIN_HEADER_ID}' chunk.");

    return (header.Value, streams.ToArray(), extendedTotalFrames);
  }

  private static uint? _ReadExtendedTotalFrames(RiffElement openDmlList) {
    foreach (var element in RiffScanner.Walk(openDmlList)) {
      if (element.IsList || element.Id.ToString() != _EXTENDED_HEADER_ID)
        continue;
      if (element.Body.Length < 4)
        throw new InvalidDataException($"Invalid '{_EXTENDED_HEADER_ID}' chunk size: expected at least 4, got {element.Body.Length}.");
      return BinaryPrimitives.ReadUInt32LittleEndian(element.Body.Span);
    }
    return null;
  }

  /// <summary>Describes one <c>LIST strl</c> without deciding anything about its codec.</summary>
  private static MediaStreamInfo _ReadStream(RiffElement streamList, int index) {
    AviStreamHeader? streamHeader = null;
    var format = ReadOnlyMemory<byte>.Empty;
    string? name = null;

    foreach (var element in RiffScanner.Walk(streamList)) {
      if (element.IsList)
        continue;

      switch (element.Id.ToString()) {
        case _STREAM_HEADER_ID:
          streamHeader ??= _ReadStreamHeader(element.Body.Span);
          break;
        case _STREAM_FORMAT_ID:
          if (format.IsEmpty)
            format = element.Body;
          break;
        case _STREAM_NAME_ID:
          name ??= _ReadZeroTerminated(element.Body.Span);
          break;
      }
    }

    if (streamHeader == null)
      return new() { Index = index, Kind = MediaStreamKind.Unknown, Name = name };

    var head = streamHeader.Value;
    var kind = head.Type.ToString() switch {
      AviStreamHeader.VIDEO_STREAM_TYPE => MediaStreamKind.Video,
      "auds" => MediaStreamKind.Audio,
      "txts" => MediaStreamKind.Subtitle,
      _ => MediaStreamKind.Data,
    };

    // An AVI states a stream's timing as a ratio it calls scale over rate, and its timestamps count
    // those units. For a video stream one unit is one frame, so the frame rate is that ratio the
    // other way up.
    var timeBase = head.Rate == 0 ? Rational.Unknown : new Rational(head.Scale == 0 ? 1 : head.Scale, head.Rate);
    var frameRate = head.Rate == 0 ? Rational.Unknown : new Rational(head.Rate, head.Scale == 0 ? 1 : head.Scale);

    var handler = new CodecTag(_ToUInt32(head.Handler));
    var language = _LanguageOf(head.Language);

    if (kind != MediaStreamKind.Video)
      return new() {
        Index = index,
        Kind = kind,
        // A sound stream's strf is a WAVEFORMATEX whose first field is the format tag. Reading that
        // one number is not decoding sound; it is the same thing the video branch does with
        // biCompression, and it is what lets a muxer copy the stream across intact.
        Codec = format.Length >= 2 ? new CodecTag(_ToUInt16(format.Span)) : CodecTag.None,
        Handler = handler,
        TimeBase = timeBase,
        DeclaredFrameCount = head.Length,
        CodecPrivateData = format,
        Language = language,
        Name = name,
      };

    if (format.IsEmpty)
      throw new InvalidDataException($"Video stream {index} has no '{_STREAM_FORMAT_ID}' chunk.");

    if (format.Length < BitmapInfoHeader.StructSize)
      throw new InvalidDataException(
        $"Invalid '{_STREAM_FORMAT_ID}' chunk size: expected at least {BitmapInfoHeader.StructSize}, got {format.Length}.");

    // The AVI specification says a video stream's strf is a BITMAPINFOHEADER, so reading width,
    // height, depth and compression out of it is the container's own business rather than any
    // codec's. What those fields mean for the packets is the codec's, and the chunk goes across
    // whole for whichever decoder wants it.
    var info = BitmapInfoHeader.ReadFrom(format.Span);

    if (info.Width <= 0 || info.Height == 0)
      throw new InvalidDataException($"Video stream {index} states an impossible size of {info.Width}x{info.Height}.");

    if (info.HeaderSize > format.Length)
      throw new InvalidDataException($"Video stream {index} states a {info.HeaderSize}-byte stream format but the chunk holds {format.Length}.");

    return new() {
      Index = index,
      Kind = kind,
      Codec = new((uint)info.Compression),
      Handler = handler,
      TimeBase = timeBase,
      FrameRate = frameRate,
      DeclaredFrameCount = head.Length,
      Width = info.Width,
      // Always positive. The sign of biHeight says which way the rows run, which is a property of
      // the pixels and therefore the codec's to read out of the header it was handed.
      Height = Math.Abs(info.Height),
      BitsPerPixel = info.BitsPerPixel,
      CodecPrivateData = format,
      Language = language,
      Name = name,
    };
  }

  /// <summary>Gathers what the file says about itself from <c>avih</c>, OpenDML, and the <c>INFO</c> list.</summary>
  /// <remarks>
  /// AVI has no cover art of its own — the RIFF <c>INFO</c> list is text and nothing else — so
  /// <see cref="VideoMetadata.CoverArt"/> stays empty here. It is in the model for the containers
  /// that do carry one; an empty list means this file had none, not that it was not looked for.
  /// </remarks>
  private static VideoMetadata _ReadMetadata(
    AviMainHeader header,
    MediaStreamInfo[] streams,
    RiffElement? infoList,
    uint? extendedTotalFrames
  ) {
    string? title = null, artist = null, album = null, encodedBy = null;
    DateTimeOffset? created = null;
    var texts = new List<TextMetadataEntry>();

    if (infoList != null)
      foreach (var element in RiffScanner.Walk(infoList.Value)) {
        if (element.IsList)
          continue;

        var value = _ReadZeroTerminated(element.Body.Span);
        if (value == null)
          continue;

        switch (element.Id.ToString()) {
          case "INAM": title ??= value; break;
          case "IART": artist ??= value; break;
          case "IPRD": album ??= value; break;
          case "ISFT": encodedBy ??= value; break;
          case "ICRD":
            // A date written as text, in whatever the writer felt like. Only what parses is taken;
            // an unrecognised spelling is kept as an annotation rather than guessed at.
            if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed))
              created ??= parsed;
            else
              texts.Add(new("Creation Time", value));
            break;
          case "ICMT": texts.Add(new("Comment", value)); break;
          case "ICOP": texts.Add(new("Copyright", value)); break;
          default: texts.Add(new(element.Id.ToString(), value)); break;
        }
      }

    var streamMetadata = new MediaStreamMetadata[streams.Length];
    for (var i = 0; i < streams.Length; ++i)
      streamMetadata[i] = new(streams[i].Index, streams[i].Kind, streams[i].Codec, streams[i].Language, streams[i].Name);

    return new() {
      Title = title,
      Artist = artist,
      Album = album,
      EncodedBy = encodedBy,
      CreationTime = created,
      Duration = _DeclaredDuration(header, extendedTotalFrames),
      Streams = streamMetadata,
      TextEntries = texts,
    };
  }

  /// <summary>How long the AVI headers claim the film runs.</summary>
  /// <remarks>
  /// OpenDML keeps <c>avih.dwTotalFrames</c> scoped to the first RIFF and puts the whole-file frame
  /// count in <c>dmlh</c>, so the extended value wins when present. Counting packets instead would
  /// mean walking the whole file to answer a question already stated by its header.
  /// </remarks>
  private static TimeSpan? _DeclaredDuration(AviMainHeader header, uint? extendedTotalFrames) {
    var totalFrames = extendedTotalFrames ?? header.TotalFrames;
    if (header.MicroSecondsPerFrame == 0 || totalFrames == 0)
      return null;

    return TimeSpan.FromTicks((long)header.MicroSecondsPerFrame * totalFrames * (TimeSpan.TicksPerSecond / 1_000_000));
  }

  /// <summary>Turns an AVI's Windows locale identifier into a language tag.</summary>
  /// <remarks>
  /// The field is an LCID rather than an ISO code, and there is no arithmetic that turns one into the
  /// other — it is a lookup, and the framework already has the table. Zero means the writer said
  /// nothing, and an identifier the table does not know is left unstated rather than guessed at.
  /// </remarks>
  private static string? _LanguageOf(short language) {
    if (language == 0)
      return null;

    try {
      return CultureInfo.GetCultureInfo((ushort)language).Name;
    } catch (Exception e) when (e is CultureNotFoundException or ArgumentOutOfRangeException) {
      return null;
    }
  }

  /// <summary>
  /// Reads a <c>strh</c> whose trailing rectangle may simply not be there.
  /// </summary>
  /// <remarks>
  /// The stream header is fifty-six bytes with its <c>rcFrame</c> rectangle and forty-eight without,
  /// and the shorter form is what older writers produce — <c>samples.ffmpeg.org</c>'s own Creative YUV
  /// recording is one, with an otherwise ordinary <c>vids</c> header naming <c>cyuv</c> in both its
  /// handler and its stream format. Requiring the full fifty-six meant that header was skipped
  /// silently and the stream came back with no kind and no codec tag at all, so a file every other
  /// tool reads was reported as holding no video. The rectangle says where in the movie rectangle a
  /// stream draws and nothing here reads it, so its absence costs nothing: the four fields are left
  /// at zero, which is what a writer omitting them means.
  /// </remarks>
  private static AviStreamHeader? _ReadStreamHeader(ReadOnlySpan<byte> body) {
    if (body.Length >= AviStreamHeader.StructSize)
      return AviStreamHeader.ReadFrom(body);

    if (body.Length < AviStreamHeader.StructSizeWithoutFrameRectangle)
      return null;

    Span<byte> padded = stackalloc byte[AviStreamHeader.StructSize];
    body[..AviStreamHeader.StructSizeWithoutFrameRectangle].CopyTo(padded);
    return AviStreamHeader.ReadFrom(padded);
  }

  private static string? _ReadZeroTerminated(ReadOnlySpan<byte> data) {
    var end = data.IndexOf((byte)0);
    if (end >= 0)
      data = data[..end];

    if (data.IsEmpty)
      return null;

    return Encoding.Latin1.GetString(data).Trim();
  }

  private static uint _ToUInt32(FourCC value) => value.A | ((uint)value.B << 8) | ((uint)value.C << 16) | ((uint)value.D << 24);

  private static uint _ToUInt16(ReadOnlySpan<byte> data) => (uint)(data[0] | (data[1] << 8));
}
