using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Av1Video;

/// <summary>An AV1 low-overhead OBU elementary stream split at temporal delimiter OBUs.</summary>
[FormatMimeType("video/AV1", "video/av1", "video/x-av1")]
[FormatDetectionPriority(-1)]
public sealed class Av1VideoContainer : IVideoContainerReader<Av1VideoContainer> {

  private static readonly MediaStreamInfo[] _STREAM = [
    new() {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("av01"),
    },
  ];

  public required ReadOnlyMemory<byte> Data { get; init; }

  public static string PrimaryExtension => ".obu";
  public static string[] FileExtensions => [".obu"];

  public static bool? MatchesSignature(ReadOnlySpan<byte> header)
    => Av1VideoReader.LooksLikeByteStream(header) ? true : null;

  public static Av1VideoContainer FromSpan(ReadOnlySpan<byte> data) => Av1VideoReader.FromSpan(data);

  public static Av1VideoContainer FromBytes(byte[] data) => Av1VideoReader.FromBytes(data);

  public static Av1VideoContainer FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("AV1 OBU video file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static IReadOnlyList<MediaStreamInfo> Streams(Av1VideoContainer container) {
    ArgumentNullException.ThrowIfNull(container);
    return _STREAM;
  }

  public static IEnumerable<CodedPacket> ReadPackets(Av1VideoContainer container) {
    ArgumentNullException.ThrowIfNull(container);
    return Av1VideoReader.Split(container.Data);
  }

  public static IEnumerable<CodedPacket> ReadPackets(Av1VideoContainer container, int streamIndex)
    => streamIndex == 0 ? ReadPackets(container) : [];

  public static VideoMetadata Metadata(Av1VideoContainer container) {
    ArgumentNullException.ThrowIfNull(container);
    return new() { Streams = [new(0, MediaStreamKind.Video, _STREAM[0].Codec)] };
  }
}
