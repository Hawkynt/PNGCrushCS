using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Ivf;

/// <summary>Duck IVF: a deliberately small packet container used by VP8, VP9 and AV1 tools.</summary>
/// <remarks>
/// IVF has one video stream, a fixed 32-byte version-zero file header and a 12-byte prefix before
/// every coded frame. The payload is kept opaque: this class is a demuxer, not a VPx/AV1 parser.
/// </remarks>
[FormatMimeType("video/x-ivf", "video/ivf")]
public sealed class IvfContainer : IVideoContainerReader<IvfContainer> {
  /// <summary>Initializes a new instance of this type.</summary>
  public IvfContainer() { }

  private const int _MINIMUM_HEADER_SIZE = 32;
  private const int _FRAME_HEADER_SIZE = 12;

  /// <summary>Gets the data.</summary>
  public required ReadOnlyMemory<byte> Data { get; init; }
  /// <summary>Gets the header Size.</summary>
  public required ushort HeaderSize { get; init; }
  /// <summary>Gets the codec.</summary>
  public required CodecTag Codec { get; init; }
  /// <summary>Gets the width.</summary>
  public required int Width { get; init; }
  /// <summary>Gets the height.</summary>
  public required int Height { get; init; }
  /// <summary>Gets the rate.</summary>
  public required uint Rate { get; init; }
  /// <summary>Gets the scale.</summary>
  public required uint Scale { get; init; }
  /// <summary>Gets the declared Frame Count.</summary>
  public required uint DeclaredFrameCount { get; init; }

  /// <summary>Gets the primary file extension for this format.</summary>
  public static string PrimaryExtension => ".ivf";
  /// <summary>Gets the file extensions supported by this format.</summary>
  public static string[] FileExtensions => [".ivf"];

  /// <summary>Determines whether the supplied header matches this file format.</summary>
  public static bool? MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 4 && header[..4].SequenceEqual("DKIF"u8) ? true : null;

  /// <summary>Reads an instance from the specified byte span.</summary>
  public static IvfContainer FromSpan(ReadOnlySpan<byte> data) => _Open(data.ToArray());

  /// <summary>Reads an instance from the specified byte array.</summary>
  public static IvfContainer FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return _Open(data);
  }

  /// <summary>Reads an instance from the specified file.</summary>
  public static IvfContainer FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("IVF video file not found.", file.FullName);

    return _Open(File.ReadAllBytes(file.FullName));
  }

  /// <summary>Gets the media streams declared by the specified container.</summary>
  public static IReadOnlyList<MediaStreamInfo> Streams(IvfContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    var timeBase = container.Rate == 0 || container.Scale == 0
      ? Rational.Unknown
      : new Rational(container.Scale, container.Rate);
    var frameRate = container.Rate == 0 || container.Scale == 0
      ? Rational.Unknown
      : new Rational(container.Rate, container.Scale);

    return [new() {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = container.Codec,
      Width = container.Width,
      Height = container.Height,
      TimeBase = timeBase,
      FrameRate = frameRate,
      DeclaredFrameCount = container.DeclaredFrameCount,
    }];
  }

  /// <summary>Enumerates coded packets from the specified container.</summary>
  public static IEnumerable<CodedPacket> ReadPackets(IvfContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    var data = container.Data;
    var at = (int)container.HeaderSize;
    while (at < data.Length) {
      if (at > data.Length - _FRAME_HEADER_SIZE)
        throw new InvalidDataException(
          $"An IVF frame header starts at byte {at}, but only {data.Length - at} byte(s) remain.");

      var frameHeader = data.Span.Slice(at, _FRAME_HEADER_SIZE);
      var size = BinaryPrimitives.ReadUInt32LittleEndian(frameHeader);
      var timestamp = BinaryPrimitives.ReadInt64LittleEndian(frameHeader[4..]);
      var payloadAt = at + _FRAME_HEADER_SIZE;

      if (size > int.MaxValue || payloadAt + (long)size > data.Length)
        throw new InvalidDataException(
          $"The IVF frame at byte {at} declares {size} payload byte(s), which run past the {data.Length}-byte file.");

      yield return new(
        StreamIndex: 0,
        Data: data.Slice(payloadAt, (int)size),
        PresentationTimestamp: timestamp,
        DecodeTimestamp: timestamp);

      at = payloadAt + (int)size;
    }
  }

  /// <summary>Enumerates coded packets for the selected stream of the specified container.</summary>
  public static IEnumerable<CodedPacket> ReadPackets(IvfContainer container, int streamIndex)
    => streamIndex == 0 ? ReadPackets(container) : [];

  /// <summary>Gets the metadata exposed by the specified container.</summary>
  public static VideoMetadata Metadata(IvfContainer container) {
    ArgumentNullException.ThrowIfNull(container);
    return new() { Streams = [new(0, MediaStreamKind.Video, container.Codec)] };
  }

  private static IvfContainer _Open(ReadOnlyMemory<byte> data) {
    if (data.Length < _MINIMUM_HEADER_SIZE)
      throw new InvalidDataException(
        $"An IVF file needs at least {_MINIMUM_HEADER_SIZE} bytes for its file header; this one has {data.Length}.");

    var header = data.Span[.._MINIMUM_HEADER_SIZE];
    if (!header[..4].SequenceEqual("DKIF"u8))
      throw new NotSupportedException("The file does not begin with the IVF DKIF signature.");

    var version = BinaryPrimitives.ReadUInt16LittleEndian(header[4..]);
    if (version != 0)
      throw new NotSupportedException($"IVF version {version} is not defined by the version-zero layout this reader implements.");

    var headerSize = BinaryPrimitives.ReadUInt16LittleEndian(header[6..]);
    if (headerSize < _MINIMUM_HEADER_SIZE || headerSize > data.Length)
      throw new InvalidDataException(
        $"IVF declares a {headerSize}-byte file header; it must be at least {_MINIMUM_HEADER_SIZE} bytes and fit in the file.");

    var codec = new CodecTag(BinaryPrimitives.ReadUInt32LittleEndian(header[8..]));
    var width = BinaryPrimitives.ReadUInt16LittleEndian(header[12..]);
    var height = BinaryPrimitives.ReadUInt16LittleEndian(header[14..]);
    if (width == 0 || height == 0)
      throw new InvalidDataException($"IVF declares a {width}x{height} video stream, which has no pixels.");

    return new() {
      Data = data,
      HeaderSize = headerSize,
      Codec = codec,
      Width = width,
      Height = height,
      Rate = BinaryPrimitives.ReadUInt32LittleEndian(header[16..]),
      Scale = BinaryPrimitives.ReadUInt32LittleEndian(header[20..]),
      DeclaredFrameCount = BinaryPrimitives.ReadUInt32LittleEndian(header[24..]),
    };
  }
}
