using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.InterplayMve;

[FormatMimeType("video/x-interplay-mve")]
[FormatMagicBytes([0x49, 0x6E, 0x74, 0x65, 0x72, 0x70, 0x6C, 0x61, 0x79, 0x20, 0x4D, 0x56, 0x45, 0x20, 0x46, 0x69, 0x6C, 0x65, 0x1A, 0x00])]
public sealed class MveContainer : IVideoContainerReader<MveContainer> {

  public required ReadOnlyMemory<byte> Data { get; init; }
  public required int Width { get; init; }
  public required int Height { get; init; }
  public required int VideoFrameCount { get; init; }
  public required bool HasAudio { get; init; }
  public required bool AudioIsStereo { get; init; }
  public required bool AudioIs16Bit { get; init; }
  public required int AudioSampleRate { get; init; }
  public required long FrameDurationMicroseconds { get; init; }

  private static readonly Rational _VIDEO_TIME_BASE = new(1, 1_000_000);
  private static readonly Rational _AUDIO_TIME_BASE_UNIT = new(1, 1);

  public static string PrimaryExtension => ".mve";
  public static string[] FileExtensions => [".mve"];

  public static MveContainer FromSpan(ReadOnlySpan<byte> data) => MveReader.Open(data.ToArray());

  public static MveContainer FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return MveReader.Open(data);
  }

  public static MveContainer FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Interplay MVE file not found.", file.FullName);
    return MveReader.Open(File.ReadAllBytes(file.FullName));
  }

  public static IReadOnlyList<MediaStreamInfo> Streams(MveContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    var frameRate = container.FrameDurationMicroseconds > 0
      ? new Rational(1_000_000, container.FrameDurationMicroseconds)
      : Rational.Unknown;

    var video = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("IMVE"),
      Width = container.Width,
      Height = container.Height,
      TimeBase = _VIDEO_TIME_BASE,
      FrameRate = frameRate,
      DeclaredFrameCount = container.VideoFrameCount,
    };

    if (!container.HasAudio)
      return [video];

    var audio = new MediaStreamInfo {
      Index = 1,
      Kind = MediaStreamKind.Audio,
      Codec = CodecTag.FromCharacters(container.AudioIsStereo ? "IMVS" : "IMVM"),
      TimeBase = container.AudioSampleRate > 0 ? new Rational(1, container.AudioSampleRate) : _AUDIO_TIME_BASE_UNIT,
      SampleRate = container.AudioSampleRate,
      Channels = container.AudioIsStereo ? 2 : 1,
      BitsPerSample = container.AudioIs16Bit ? 16 : 8,
    };

    return [video, audio];
  }

  public static IEnumerable<CodedPacket> ReadPackets(MveContainer container) {
    ArgumentNullException.ThrowIfNull(container);
    return MveReader.ReadPackets(container);
  }

  public static VideoMetadata Metadata(MveContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    var streams = Streams(container);
    var declared = new MediaStreamMetadata[streams.Count];
    for (var i = 0; i < streams.Count; ++i)
      declared[i] = new(streams[i].Index, streams[i].Kind, streams[i].Codec);
    return new() { Streams = declared };
  }
}
