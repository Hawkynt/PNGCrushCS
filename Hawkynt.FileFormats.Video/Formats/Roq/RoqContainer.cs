using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.RoqVideo;

/// <summary>A RoQ movie: id's quadtree video plus optional RoQ DPCM sound.</summary>
[FormatMimeType("video/x-roq")]
[FormatMagicBytes([0x84, 0x10])]
public sealed class RoqContainer : IVideoContainerReader<RoqContainer> {
  public required ReadOnlyMemory<byte> Data { get; init; }
  public required int Width { get; init; }
  public required int Height { get; init; }
  public required int VideoFrameCount { get; init; }
  public required bool HasAudio { get; init; }
  public required bool AudioIsStereo { get; init; }
  public required int FrameRate { get; init; }
  public required int MotionScale { get; init; }

  private static readonly Rational _AudioTimeBase = new(1, 22050);

  public static string PrimaryExtension => ".roq";
  public static string[] FileExtensions => [".roq"];

  public static RoqContainer FromSpan(ReadOnlySpan<byte> data) => RoqReader.Open(data.ToArray());

  public static RoqContainer FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return RoqReader.Open(data);
  }

  public static RoqContainer FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("RoQ file not found.", file.FullName);
    return RoqReader.Open(File.ReadAllBytes(file.FullName));
  }

  public static IReadOnlyList<MediaStreamInfo> Streams(RoqContainer container) {
    ArgumentNullException.ThrowIfNull(container);
    var rate = new Rational(container.FrameRate, 1);
    var video = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("RoQV"),
      Width = container.Width,
      Height = container.Height,
      TimeBase = new Rational(1, container.FrameRate),
      FrameRate = rate,
      DeclaredFrameCount = container.VideoFrameCount,
      CodecPrivateData = new byte[] { checked((byte)container.MotionScale) },
    };

    if (!container.HasAudio)
      return [video];

    return [video, new MediaStreamInfo {
      Index = 1,
      Kind = MediaStreamKind.Audio,
      Codec = CodecTag.FromCharacters(container.AudioIsStereo ? "RoQS" : "RoQM"),
      TimeBase = _AudioTimeBase,
    }];
  }

  public static IEnumerable<CodedPacket> ReadPackets(RoqContainer container) {
    ArgumentNullException.ThrowIfNull(container);
    return RoqReader.ReadPackets(container);
  }

  public static VideoMetadata Metadata(RoqContainer container) {
    ArgumentNullException.ThrowIfNull(container);
    var streams = Streams(container);
    var declared = new MediaStreamMetadata[streams.Count];
    for (var i = 0; i < streams.Count; ++i)
      declared[i] = new(streams[i].Index, streams[i].Kind, streams[i].Codec);
    return new() { Streams = declared };
  }
}
