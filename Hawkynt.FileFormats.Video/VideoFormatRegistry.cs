using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;

namespace Hawkynt.FileFormats.Video;

/// <summary>
/// Public, zero-runtime-reflection registry of every container and codec discovered at compile time
/// by <c>FileFormat.Registry.Generator</c>.
/// </summary>
/// <remarks>
/// Two tables, not one. Containers are looked up by what a file looks like; codecs are looked up by
/// what a stream says it is coded with. Nothing joins them but <see cref="DecodeFrames(byte[], int)"/>,
/// which is the one method in this library that does demux and decode in the same breath — and it
/// does so by calling the two lookups in turn, so a caller that wants only packets never builds a
/// decoder and a caller with packets from elsewhere can still get a decoder.
/// </remarks>
public static class VideoFormatRegistry {

  private const int _HEADER_READ_SIZE = 4096;

  private static readonly Dictionary<VideoFormat, VideoFormatEntry> _byFormat = new();
  private static readonly Dictionary<string, List<VideoFormat>> _candidatesByExtension = new(StringComparer.OrdinalIgnoreCase);
  private static readonly Dictionary<string, VideoFormat> _byMimeType = new(StringComparer.OrdinalIgnoreCase);
  private static readonly List<VideoCodecEntry> _codecs = [];
  private static VideoFormatEntry[] _detectionOrder = [];

  static VideoFormatRegistry() => VideoFormatRegistration.Initialize();

  // ============================================================================================
  // Internal registration API — called only by generated code in VideoFormatRegistration.g.cs
  // ============================================================================================

  internal static void Register(VideoFormatEntry entry) {
    if (entry.Format != VideoFormat.Unknown)
      _byFormat.TryAdd(entry.Format, entry);

    foreach (var extension in entry.AllExtensions) {
      if (!_candidatesByExtension.TryGetValue(extension, out var candidates))
        _candidatesByExtension[extension] = candidates = [];

      if (!candidates.Contains(entry.Format))
        candidates.Add(entry.Format);
    }

    foreach (var mime in entry.MimeTypes)
      _byMimeType.TryAdd(mime, entry.Format);
  }

  internal static void RegisterCodec(VideoCodecEntry entry) => _codecs.Add(entry);

  /// <summary>Sorts the containers into the order detection tries them. Called once, after registration.</summary>
  internal static void BuildDetectionOrder()
    => _detectionOrder = _byFormat.Values
      .OrderBy(e => e.DetectionPriority)
      .ThenBy(e => e.Format.ToString(), StringComparer.Ordinal)
      .ToArray();

  // ============================================================================================
  // Lookup
  // ============================================================================================

  /// <summary>Every registered container, in detection order.</summary>
  public static IEnumerable<VideoFormatEntry> AllFormats => _detectionOrder;

  /// <summary>Every registered codec, in registration order.</summary>
  public static IEnumerable<VideoCodecEntry> AllCodecs => _codecs;

  /// <summary>The entry for a container, or <c>null</c> when nothing registered it.</summary>
  public static VideoFormatEntry? GetEntry(VideoFormat format) => _byFormat.GetValueOrDefault(format);

  /// <summary>The containers that claim an extension, in detection order.</summary>
  public static IReadOnlyList<VideoFormat> ByExtension(string extension) {
    ArgumentNullException.ThrowIfNull(extension);

    if (!extension.StartsWith('.'))
      extension = "." + extension;

    return _candidatesByExtension.TryGetValue(extension, out var candidates) ? candidates : [];
  }

  /// <summary>The container that claims a media type, or <see cref="VideoFormat.Unknown"/>.</summary>
  public static VideoFormat ByMimeType(string mimeType) {
    ArgumentNullException.ThrowIfNull(mimeType);

    return _byMimeType.GetValueOrDefault(mimeType, VideoFormat.Unknown);
  }

  /// <summary>Identifies the container a header belongs to, or <see cref="VideoFormat.Unknown"/>.</summary>
  /// <remarks>
  /// By the bytes only. An extension is a hint a person typed and a container that went by it would
  /// be wrong about every file that was renamed; the signature is what the writer actually wrote.
  /// <para/>
  /// Callers hand whole files to this, so only the head of what arrives is looked at and none of it
  /// is copied. Every signature this can decide on lives in the first few bytes.
  /// </remarks>
  public static VideoFormat Detect(ReadOnlyMemory<byte> data) {
    var header = data.Length > _HEADER_READ_SIZE ? data[.._HEADER_READ_SIZE] : data;

    foreach (var entry in _detectionOrder) {
      if (entry.MatchesSignature != null) {
        // The container's own opinion outranks the attribute: RIFF alone does not tell an AVI from a
        // WAVE or a WebP, and only the container knows which further bytes decide it.
        var verdict = entry.MatchesSignature(header);
        if (verdict == true)
          return entry.Format;
        if (verdict == false)
          continue;
      }

      foreach (var magic in entry.MagicSignatures) {
        if (header.Length < magic.MinHeaderLength)
          continue;

        if (header.Span.Slice(magic.Offset, magic.Signature.Length).SequenceEqual(magic.Signature))
          return entry.Format;
      }
    }

    return VideoFormat.Unknown;
  }

  /// <summary>Identifies the container a file belongs to, reading only its head.</summary>
  public static VideoFormat Detect(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      return VideoFormat.Unknown;

    using var stream = file.OpenRead();
    var header = new byte[(int)Math.Min(_HEADER_READ_SIZE, stream.Length)];
    stream.ReadExactly(header);
    return Detect(header);
  }

  // ============================================================================================
  // Demux
  // ============================================================================================

  /// <summary>The streams a file declares.</summary>
  public static IReadOnlyList<MediaStreamInfo> ReadStreams(byte[] data) => _Entry(data, null).ReadStreams(data);

  /// <summary>Every packet of a file, lazily.</summary>
  public static IEnumerable<CodedPacket> ReadPackets(byte[] data) => _Entry(data, null).ReadPackets(data);

  /// <summary>The packets of one stream of a file, lazily.</summary>
  public static IEnumerable<CodedPacket> ReadPackets(byte[] data, int streamIndex) => _Entry(data, null).ReadStreamPackets(data, streamIndex);

  /// <summary>What a file says about itself.</summary>
  public static VideoMetadata ReadMetadata(byte[] data) => _Entry(data, null).ReadMetadata(data);

  /// <summary>The streams a file declares.</summary>
  public static IReadOnlyList<MediaStreamInfo> ReadStreams(FileInfo file) {
    var data = _ReadAllBytes(file);
    return _Entry(data, file.Extension).ReadStreams(data);
  }

  /// <summary>What a file says about itself.</summary>
  public static VideoMetadata ReadMetadata(FileInfo file) {
    var data = _ReadAllBytes(file);
    return _Entry(data, file.Extension).ReadMetadata(data);
  }

  // ============================================================================================
  // Decode
  // ============================================================================================

  /// <summary>Whether any registered codec takes this stream.</summary>
  public static bool CanDecode(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    foreach (var codec in _codecs)
      if (codec.Accepts(stream))
        return true;

    return false;
  }

  /// <summary>
  /// Builds a decoder for one stream, or refuses the stream by name.
  /// </summary>
  /// <exception cref="NotSupportedException">
  /// No registered codec takes this stream's tag, or the codec that does cannot decode this
  /// particular stream. Either way the message names the four-character code, so a refusal says
  /// which codec is missing rather than only that something is.
  /// </exception>
  public static IVideoFrameDecoder CreateDecoder(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    foreach (var codec in _codecs)
      if (codec.Accepts(stream))
        return codec.CreateDecoder(stream);

    throw new NotSupportedException(
      $"Stream {stream.Index} is coded as '{stream.Codec}' (0x{stream.Codec.Value:X8}, stream handler '{stream.Handler}'), "
      + $"which no registered codec decodes. Decoders present: {string.Join(", ", _codecs.Select(c => c.CodecName))}.");
  }

  // ============================================================================================
  // Demux + decode
  // ============================================================================================

  /// <summary>Walks the pictures of one stream of a file, decoding each packet as it is reached.</summary>
  public static IEnumerable<DecodedFrame> DecodeFrames(byte[] data, int streamIndex) => _DecodeFrames(data, null, streamIndex);

  /// <summary>Walks the pictures of the first stream of a file that carries any.</summary>
  public static IEnumerable<DecodedFrame> DecodeFrames(byte[] data) => _DecodeFrames(data, null, null);

  /// <summary>Walks the pictures of the first stream of a file that carries any.</summary>
  public static IEnumerable<DecodedFrame> DecodeFrames(FileInfo file) {
    var data = _ReadAllBytes(file);
    return _DecodeFrames(data, file.Extension, null);
  }

  /// <summary>Walks the pictures of one stream of a file.</summary>
  public static IEnumerable<DecodedFrame> DecodeFrames(FileInfo file, int streamIndex) {
    var data = _ReadAllBytes(file);
    return _DecodeFrames(data, file.Extension, streamIndex);
  }

  private static IEnumerable<DecodedFrame> _DecodeFrames(byte[] data, string? extension, int? streamIndex) {
    var entry = _Entry(data, extension);
    var streams = entry.ReadStreams(data);
    var stream = streamIndex is { } index
      ? streams.FirstOrDefault(s => s.Index == index)
        ?? throw new ArgumentOutOfRangeException(nameof(streamIndex), $"The file has no stream {index}.")
      : streams.FirstOrDefault(s => s.Kind == MediaStreamKind.Video)
        ?? throw new InvalidDataException("The file contains no video stream.");

    return VideoIO.Decode(entry.ReadStreamPackets(data, stream.Index), stream, CreateDecoder);
  }

  private static byte[] _ReadAllBytes(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Video file not found.", file.FullName);

    return File.ReadAllBytes(file.FullName);
  }

  /// <summary>Picks the container an opened file belongs to, by its bytes and — failing that — its name.</summary>
  /// <remarks>
  /// The bytes come first because they are what the writer wrote. The name is consulted only for
  /// containers that have no signature to be recognised by, and one of the two here is exactly that:
  /// a single-frame <c>.mjpg</c> is a valid JPEG byte for byte, so claiming a signature for it would
  /// put it in competition with every photograph in existence and win nothing.
  /// </remarks>
  private static VideoFormatEntry _Entry(byte[] data, string? extension) {
    ArgumentNullException.ThrowIfNull(data);

    var format = Detect(data);
    if (_byFormat.TryGetValue(format, out var entry))
      return entry;

    if (extension != null)
      foreach (var candidate in ByExtension(extension))
        if (_byFormat.TryGetValue(candidate, out var byName))
          return byName;

    throw new InvalidDataException(
      "The data does not begin with any container signature this library recognises"
      + (extension == null ? "." : $", and nothing registered claims '{extension}'."));
  }
}
