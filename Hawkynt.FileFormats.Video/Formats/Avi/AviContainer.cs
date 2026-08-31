using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Avi;

/// <summary>An AVI split into its declared streams and RIFF <c>movi</c> packets.</summary>
[FormatMimeType("video/avi", "video/msvideo", "video/x-msvideo")]
public sealed class AviContainer : IVideoContainerReader<AviContainer> {
  /// <summary>Initializes a new instance of this type.</summary>
  public AviContainer() { }

  private const string _RECORD_LIST = "rec ";

  /// <summary>Gets the header.</summary>
  public required AviMainHeader Header { get; init; }
  /// <summary>Gets the media streams declared by the container.</summary>
  public required IReadOnlyList<MediaStreamInfo> StreamInfos { get; init; }
  /// <summary>Gets the file Metadata.</summary>
  public required VideoMetadata FileMetadata { get; init; }
  /// <summary>Gets the movie List.</summary>
  public required ReadOnlyMemory<byte> MovieList { get; init; }

  /// <summary>Gets the primary file extension for this format.</summary>
  public static string PrimaryExtension => ".avi";
  /// <summary>Gets the file extensions supported by this format.</summary>
  public static string[] FileExtensions => [".avi"];

  /// <summary>Determines whether the supplied header matches this file format.</summary>
  public static bool? MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 12
       && header[0] == (byte)'R' && header[1] == (byte)'I' && header[2] == (byte)'F' && header[3] == (byte)'F'
       && header[8] == (byte)'A' && header[9] == (byte)'V' && header[10] == (byte)'I' && header[11] == (byte)' '
      ? true
      : null;

  /// <summary>Reads an instance from the specified byte span.</summary>
  public static AviContainer FromSpan(ReadOnlySpan<byte> data) => AviReader.FromSpan(data);
  /// <summary>Reads an instance from the specified byte array.</summary>
  public static AviContainer FromBytes(byte[] data) => AviReader.FromBytes(data);

  /// <summary>Reads an instance from the specified file.</summary>
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

  /// <summary>Gets the metadata exposed by the specified container.</summary>
  public static VideoMetadata Metadata(AviContainer container) {
    ArgumentNullException.ThrowIfNull(container);
    return container.FileMetadata;
  }

  /// <summary>Enumerates coded packets from the specified container.</summary>
  public static IEnumerable<CodedPacket> ReadPackets(AviContainer container) {
    ArgumentNullException.ThrowIfNull(container);
    return _Walk(container, null);
  }

  /// <summary>Enumerates coded packets for the selected stream of the specified container.</summary>
  public static IEnumerable<CodedPacket> ReadPackets(AviContainer container, int streamIndex) {
    ArgumentNullException.ThrowIfNull(container);
    return _Walk(container, streamIndex);
  }

  private static IEnumerable<CodedPacket> _Walk(AviContainer container, int? onlyStream) {
    var ordinals = new long[container.StreamInfos.Count];

    foreach (var element in RiffScanner.Walk(container.MovieList, 0, container.MovieList.Length)) {
      if (element.IsList) {
        if (element.ListType.ToString() != _RECORD_LIST)
          continue;

        foreach (var record in RiffScanner.Walk(element))
          if (_TryPacket(container, record, ordinals, onlyStream, out var recorded))
            yield return recorded;
        continue;
      }

      if (_TryPacket(container, element, ordinals, onlyStream, out var packet))
        yield return packet;
    }
  }

  private static bool _TryPacket(
    AviContainer container,
    RiffElement element,
    long[] ordinals,
    int? onlyStream,
    out CodedPacket packet) {
    packet = default;

    var id = element.Id.ToString();
    if (id.Length != 4 || !char.IsAsciiDigit(id[0]) || !char.IsAsciiDigit(id[1]))
      return false;
    if (id.Substring(2) is not ("db" or "dc" or "wb" or "tx"))
      return false;

    var streamIndex = (id[0] - '0') * 10 + (id[1] - '0');
    if ((uint)streamIndex >= (uint)ordinals.Length || element.Body.Length == 0)
      return false;

    var ordinal = ordinals[streamIndex]++;
    if (onlyStream != null && streamIndex != onlyStream)
      return false;

    var isVideo = container.StreamInfos[streamIndex].Kind == MediaStreamKind.Video;
    packet = new(streamIndex, element.Body, isVideo ? ordinal : null, isVideo ? ordinal : null);
    return true;
  }
}
