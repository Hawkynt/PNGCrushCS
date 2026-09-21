using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Avi;

/// <summary>An AVI split into its declared streams and RIFF <c>movi</c> packets.</summary>
[FormatMimeType("video/avi", "video/msvideo", "video/x-msvideo")]
public sealed class AviContainer : IVideoContainerReader<AviContainer> {

  private const string _RECORD_LIST = "rec ";

  public required AviMainHeader Header { get; init; }
  public required IReadOnlyList<MediaStreamInfo> StreamInfos { get; init; }
  public required VideoMetadata FileMetadata { get; init; }
  public required ReadOnlyMemory<byte> MovieList { get; init; }
  internal IReadOnlyList<ReadOnlyMemory<byte>> MovieLists { get; init; } = [];

  /// <summary>The <c>idx1</c> chunk's body, or empty where the file carries none.</summary>
  internal ReadOnlyMemory<byte> LegacyIndex { get; init; }

  public static string PrimaryExtension => ".avi";
  public static string[] FileExtensions => [".avi"];

  public static bool? MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 12
       && header[0] == (byte)'R' && header[1] == (byte)'I' && header[2] == (byte)'F' && header[3] == (byte)'F'
       && header[8] == (byte)'A' && header[9] == (byte)'V' && header[10] == (byte)'I' && header[11] == (byte)' '
      ? true
      : null;

  public static AviContainer FromSpan(ReadOnlySpan<byte> data) => AviReader.FromSpan(data);
  public static AviContainer FromBytes(byte[] data) => AviReader.FromBytes(data);

  public static AviContainer FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("AVI file not found.", file.FullName);
    return AviReader.FromBytes(File.ReadAllBytes(file.FullName));
  }

  /// <summary>
  /// Returns stream declarations with WAVEFORMATEX geometry promoted into the common audio fields.
  /// The <c>strf</c> bytes remain intact in <see cref="MediaStreamInfo.CodecPrivateData"/>.
  /// </summary>
  public static IReadOnlyList<MediaStreamInfo> Streams(AviContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    var result = new MediaStreamInfo[container.StreamInfos.Count];
    for (var i = 0; i < result.Length; ++i) {
      var info = container.StreamInfos[i];
      result[i] = info.Kind == MediaStreamKind.Audio ? _WithWaveGeometry(info) : info;
    }
    return result;
  }

  private static MediaStreamInfo _WithWaveGeometry(MediaStreamInfo source) {
    var format = source.CodecPrivateData.Span;
    var channels = format.Length >= 4 ? BinaryPrimitives.ReadUInt16LittleEndian(format[2..]) : 0;
    var sampleRateRaw = format.Length >= 8 ? BinaryPrimitives.ReadUInt32LittleEndian(format[4..]) : 0u;
    var sampleRate = sampleRateRaw <= int.MaxValue ? (int)sampleRateRaw : 0;
    var bitsPerSample = format.Length >= 16 ? BinaryPrimitives.ReadUInt16LittleEndian(format[14..]) : 0;

    if (channels == 0 && sampleRate == 0 && bitsPerSample == 0)
      return source;

    return new() {
      Index = source.Index,
      Kind = source.Kind,
      Codec = source.Codec,
      Handler = source.Handler,
      CodecId = source.CodecId,
      TimeBase = source.TimeBase,
      FrameRate = source.FrameRate,
      DeclaredFrameCount = source.DeclaredFrameCount,
      Width = source.Width,
      Height = source.Height,
      BitsPerPixel = source.BitsPerPixel,
      SampleRate = sampleRate,
      Channels = channels,
      BitsPerSample = bitsPerSample,
      CodecPrivateData = source.CodecPrivateData,
      Language = source.Language,
      Name = source.Name,
    };
  }

  public static VideoMetadata Metadata(AviContainer container) {
    ArgumentNullException.ThrowIfNull(container);
    return container.FileMetadata;
  }

  public static IEnumerable<CodedPacket> ReadPackets(AviContainer container) {
    ArgumentNullException.ThrowIfNull(container);
    return _Walk(container, null);
  }

  public static IEnumerable<CodedPacket> ReadPackets(AviContainer container, int streamIndex) {
    ArgumentNullException.ThrowIfNull(container);
    return _Walk(container, streamIndex);
  }

  private static IEnumerable<CodedPacket> _Walk(AviContainer container, int? onlyStream) {
    var ordinals = new long[container.StreamInfos.Count];
    var movieLists = container.MovieLists.Count == 0 ? new[] { container.MovieList } : container.MovieLists;
    var keyFlags = _ReadKeyFrameFlags(container, movieLists);

    foreach (var movieList in movieLists)
      foreach (var element in RiffScanner.Walk(movieList, 0, movieList.Length)) {
        if (element.IsList) {
          if (element.ListType.ToString() != _RECORD_LIST)
            continue;

          foreach (var record in RiffScanner.Walk(element))
            if (_TryPacket(container, record, ordinals, keyFlags, onlyStream, out var recorded))
              yield return recorded;
          continue;
        }

        if (_TryPacket(container, element, ordinals, keyFlags, onlyStream, out var packet))
          yield return packet;
      }
  }

  // ============================================================================================
  // Which packets a decoder may begin at
  // ============================================================================================

  /// <summary>The <c>idx1</c> entry flag that marks a chunk a decoder may begin at.</summary>
  private const uint _AVIIF_KEYFRAME = 0x10;

  /// <summary>Set in an OpenDML index entry's size field to mean the opposite: <i>not</i> a key frame.</summary>
  private const uint _AVISTDINDEX_NOT_KEYFRAME = 0x8000_0000;

  /// <summary>Bytes of <c>AVISTDINDEX</c> before its first entry.</summary>
  private const int _STANDARD_INDEX_HEADER_SIZE = 24;

  /// <summary><c>bIndexType</c> naming an index that points at chunks rather than at other indexes.</summary>
  private const byte _AVI_INDEX_OF_CHUNKS = 1;

  /// <summary>
  /// Reads which of each stream's chunks a decoder may begin at, in the order the chunks lie in
  /// <c>movi</c>, or <c>null</c> where the file states nothing.
  /// </summary>
  /// <remarks>
  /// An AVI puts this nowhere near the picture it describes. There is no per-chunk header in
  /// <c>movi</c> and no flag inside the payload — a codec whose I and P pictures are told apart by
  /// nothing else is told apart by the file's index or not at all. ZeroCodec is exactly that codec:
  /// its inter picture writes a zero byte to mean "keep the byte under this one", so reading a P
  /// picture as an I picture yields a frame that is mostly black and reading an I picture as a P
  /// picture yields one built on a reference that does not exist. This used to return nothing at all,
  /// so every AVI packet this package produced said it was not a key frame, and that is the whole of
  /// why the round trip below could not tell the two picture types apart.
  /// <para/>
  /// Two indexes may be present and they are not equivalent. The OpenDML <c>ix##</c> chunks sit
  /// inside each <c>movi</c> list, one per stream per segment, and so describe every segment of a
  /// file that has more than one; the legacy <c>idx1</c> sits once after the first <c>movi</c> and by
  /// convention describes only that first RIFF. The OpenDML index is therefore preferred where it
  /// exists and <c>idx1</c> is the fallback.
  /// <para/>
  /// Entries are matched to chunks by their position in each stream's own run and not by the offsets
  /// they carry. An <c>idx1</c> offset is measured from the <c>movi</c> list's form type in most
  /// files and from the start of the file in some, the specification never said which, and a reader
  /// that picks wrong silently attaches every flag to the wrong picture. The ordering is not
  /// ambiguous in either convention, so the ordering is what is used, and the same chunks are
  /// filtered out here as in <see cref="_TryPacket"/> so the two counts stay in step.
  /// <para/>
  /// Where a file carries no index nothing is claimed: the flags stay false, a codec that needs them
  /// refuses the stream by name, and no picture is invented. An AVI that does not say which of its
  /// pictures stand alone has not said it, and guessing "all of them" would turn every inter picture
  /// of every predicted codec into a plausible wrong frame rather than an error.
  /// </remarks>
  private static bool[][]? _ReadKeyFrameFlags(
    AviContainer container, IReadOnlyList<ReadOnlyMemory<byte>> movieLists) {
    var streamCount = container.StreamInfos.Count;
    if (streamCount == 0)
      return null;

    var flags = new List<bool>[streamCount];
    for (var i = 0; i < streamCount; ++i)
      flags[i] = [];

    return _ReadStandardIndexes(movieLists, flags) || _ReadLegacyIndex(container.LegacyIndex.Span, flags)
      ? Array.ConvertAll(flags, static stream => stream.ToArray())
      : null;
  }

  /// <summary>Collects the OpenDML <c>ix##</c> indexes inside every <c>movi</c> list, in file order.</summary>
  private static bool _ReadStandardIndexes(
    IReadOnlyList<ReadOnlyMemory<byte>> movieLists, List<bool>[] flags) {
    var found = false;

    foreach (var movieList in movieLists)
      foreach (var element in RiffScanner.Walk(movieList, 0, movieList.Length)) {
        if (element.IsList)
          continue;

        // OpenDML spells the standard index with the "ix" in front of the stream digits — ix00 —
        // and not behind them. _TryPacket's filter catches the other spelling; neither reaches a
        // decoder, because a chunk beginning with a letter fails its leading-digit test as well.
        var id = element.Id.ToString();
        if (id.Length != 4 || id[0] != 'i' || id[1] != 'x'
            || !char.IsAsciiDigit(id[2]) || !char.IsAsciiDigit(id[3]))
          continue;

        if (_ReadStandardIndex(element.Body.Span, flags))
          found = true;
      }

    return found;
  }

  private static bool _ReadStandardIndex(ReadOnlySpan<byte> index, List<bool>[] flags) {
    if (index.Length < _STANDARD_INDEX_HEADER_SIZE)
      return false;

    var longsPerEntry = BinaryPrimitives.ReadUInt16LittleEndian(index);
    if (index[3] != _AVI_INDEX_OF_CHUNKS || longsPerEntry != 2)
      return false; // a superindex points at other indexes, and an unexpected stride cannot be walked.

    // AVISTDINDEX: wLongsPerEntry, bIndexSubType, bIndexType, nEntriesInUse, dwChunkId,
    // qwBaseOffset, dwReserved3 — so the count is at four and the chunk id at eight.
    var chunkId = index.Slice(8, 4);
    if (!char.IsAsciiDigit((char)chunkId[0]) || !char.IsAsciiDigit((char)chunkId[1]))
      return false;

    var streamIndex = (chunkId[0] - '0') * 10 + (chunkId[1] - '0');
    if ((uint)streamIndex >= (uint)flags.Length)
      return false;

    var entries = BinaryPrimitives.ReadUInt32LittleEndian(index[4..]);
    var available = (index.Length - _STANDARD_INDEX_HEADER_SIZE) / 8;
    if (entries > (uint)available)
      entries = (uint)available; // a truncated index describes the chunks it reached and no more.

    for (var i = 0; i < entries; ++i) {
      var size = BinaryPrimitives.ReadUInt32LittleEndian(index[(_STANDARD_INDEX_HEADER_SIZE + i * 8 + 4)..]);
      flags[streamIndex].Add((size & _AVISTDINDEX_NOT_KEYFRAME) == 0);
    }

    return entries != 0;
  }

  /// <summary>Walks <c>idx1</c>, whose sixteen-byte entries lie in <c>movi</c> order across all streams.</summary>
  private static bool _ReadLegacyIndex(ReadOnlySpan<byte> index, List<bool>[] flags) {
    var found = false;

    for (var at = 0; at + 16 <= index.Length; at += 16) {
      var entry = index.Slice(at, 16);
      if (!char.IsAsciiDigit((char)entry[0]) || !char.IsAsciiDigit((char)entry[1]))
        continue;

      var suffix = $"{(char)entry[2]}{(char)entry[3]}";
      if (suffix is _INDEX_SUFFIX or _PALETTE_CHANGE_SUFFIX)
        continue;

      var streamIndex = (entry[0] - '0') * 10 + (entry[1] - '0');
      if ((uint)streamIndex >= (uint)flags.Length)
        continue;

      if (BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]) == 0)
        continue; // an empty chunk yields no packet, so it must consume no entry either.

      flags[streamIndex].Add((BinaryPrimitives.ReadUInt32LittleEndian(entry[4..]) & _AVIIF_KEYFRAME) != 0);
      found = true;
    }

    return found;
  }

  /// <summary>The OpenDML index, which points at chunks rather than being one.</summary>
  private const string _INDEX_SUFFIX = "ix";

  /// <summary>A palette change, which alters how later frames are shown without being a frame.</summary>
  private const string _PALETTE_CHANGE_SUFFIX = "pc";

  /// <summary>
  /// Decides whether a chunk inside the movie list is a stream's payload, and whose.
  /// </summary>
  /// <remarks>
  /// The two digits name the stream and the two characters after them name what the chunk holds.
  /// This used to accept only <c>db</c>, <c>dc</c>, <c>wb</c> and <c>tx</c>, which are what the
  /// specification lists — and it meant a file whose encoder spelled the suffix its own way yielded
  /// no packets at all rather than an error. Intel's own Indeo 4 files do exactly that: the two on
  /// <c>samples.ffmpeg.org</c> write their video as <c>00iv</c>, ffmpeg plays them, and this returned
  /// an empty stream and called it a successful read.
  /// <para/>
  /// So the test is inverted. A chunk is payload unless its suffix names something that is not — the
  /// OpenDML index, which points at chunks, and a palette change, which alters how later frames are
  /// shown without being one. Anything else goes to the stream its digits name, and the stream's own
  /// declared kind decides what to do with it. Being wrong this way produces a packet a decoder then
  /// refuses by name; being wrong the other way produced silence.
  /// </remarks>
  private static bool _TryPacket(
    AviContainer container,
    RiffElement element,
    long[] ordinals,
    bool[][]? keyFlags,
    int? onlyStream,
    out CodedPacket packet) {
    packet = default;

    var id = element.Id.ToString();
    if (id.Length != 4 || !char.IsAsciiDigit(id[0]) || !char.IsAsciiDigit(id[1]))
      return false;
    if (id.Substring(2) is _INDEX_SUFFIX or _PALETTE_CHANGE_SUFFIX)
      return false;

    var streamIndex = (id[0] - '0') * 10 + (id[1] - '0');
    if ((uint)streamIndex >= (uint)ordinals.Length || element.Body.Length == 0)
      return false;

    var ordinal = ordinals[streamIndex]++;
    if (onlyStream != null && streamIndex != onlyStream)
      return false;

    var isVideo = container.StreamInfos[streamIndex].Kind == MediaStreamKind.Video;
    var stream = keyFlags?[streamIndex];
    packet = new(
      streamIndex,
      element.Body,
      isVideo ? ordinal : null,
      isVideo ? ordinal : null,
      IsKeyFrame: stream != null && ordinal < stream.Length && stream[ordinal]);
    return true;
  }
}
