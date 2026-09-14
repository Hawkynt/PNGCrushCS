using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.FlicVideo;

/// <summary>
/// An Autodesk/DTA FLIC file (<c>.fli</c>, <c>.flc</c>, <c>.flx</c>, <c>.flh</c>, <c>.flt</c>)
/// split into its one video stream and frame chunks.
/// </summary>
[FormatMimeType("video/x-flic", "video/fli", "video/flc")]
[FormatMagicBytes([0x11, 0xAF], 4)]
[FormatMagicBytes([0x12, 0xAF], 4)]
[FormatMagicBytes([0x44, 0xAF], 4)]
public sealed class FliContainer : IVideoContainerReader<FliContainer> {

  public required ReadOnlyMemory<byte> Data { get; init; }
  public required ushort Magic { get; init; }
  public required int Width { get; init; }
  public required int Height { get; init; }

  /// <summary>Effective coded depth. Autodesk FLX's stored 16 is normalized to its actual RGB555 15.</summary>
  public required ushort Depth { get; init; }

  public required ushort FrameCount { get; init; }
  public required uint Speed { get; init; }
  public required int FirstFrameOffset { get; init; }

  public static string PrimaryExtension => ".fli";
  public static string[] FileExtensions => [".fli", ".flc", ".flx", ".flh", ".flt"];

  public static FliContainer FromSpan(ReadOnlySpan<byte> data) => FliReader.Open(data.ToArray());

  public static FliContainer FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FliReader.Open(data);
  }

  public static FliContainer FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("FLIC file not found.", file.FullName);
    return FliReader.Open(File.ReadAllBytes(file.FullName));
  }

  public static IReadOnlyList<MediaStreamInfo> Streams(FliContainer container) {
    ArgumentNullException.ThrowIfNull(container);
    return [_StreamInfo(container)];
  }

  private static MediaStreamInfo _StreamInfo(FliContainer container) {
    var timeBase = container.Magic == FliReader.MAGIC_FLI ? new Rational(1, 70) : new Rational(1, 1000);
    var frameRate = container.Speed > 0 ? new Rational(timeBase.Denominator, container.Speed) : Rational.Unknown;

    return new() {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("FLIC"),
      Width = container.Width,
      Height = container.Height,
      BitsPerPixel = container.Depth,
      TimeBase = timeBase,
      FrameRate = frameRate,
      DeclaredFrameCount = container.FrameCount,
    };
  }

  public static IEnumerable<CodedPacket> ReadPackets(FliContainer container) {
    ArgumentNullException.ThrowIfNull(container);
    return FliReader.Split(container);
  }

  public static IEnumerable<CodedPacket> ReadPackets(FliContainer container, int streamIndex)
    => streamIndex == 0 ? ReadPackets(container) : [];

  public static VideoMetadata Metadata(FliContainer container) {
    ArgumentNullException.ThrowIfNull(container);
    var stream = _StreamInfo(container);
    return new() { Streams = [new(0, MediaStreamKind.Video, stream.Codec)] };
  }
}
