using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Str;

/// <summary>
/// A Sony PlayStation STR file (<c>.str</c>) — the console's own movie format, a raw run of CD-XA
/// sectors carrying MDEC video and XA-ADPCM audio, taken apart into the packets its per-sector
/// headers describe and nothing else.
/// </summary>
[FormatMagicBytes([0x43, 0x44, 0x58, 0x41], 8)]
[FormatMagicBytes([0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00])]
[FormatMimeType("video/x-psx-str")]
public sealed class StrContainer : IVideoContainerReader<StrContainer> {

  public required ReadOnlyMemory<byte> Data { get; init; }
  public required int SyncStart { get; init; }
  public required int SectorCount { get; init; }
  public required int Width { get; init; }
  public required int Height { get; init; }
  public required int VideoFrameCount { get; init; }
  public required bool HasAudio { get; init; }
  public required int AudioPacketCount { get; init; }

  /// <summary>XA-ADPCM sample rate stated by the audio sectors' coding-information byte.</summary>
  public required int AudioSampleRate { get; init; }

  /// <summary>XA-ADPCM channel count stated by the audio sectors' coding-information byte.</summary>
  public required int AudioChannels { get; init; }

  /// <summary>XA-ADPCM coded sample precision stated by the audio sectors' coding-information byte.</summary>
  public required int AudioBitsPerSample { get; init; }

  public static string PrimaryExtension => ".str";
  public static string[] FileExtensions => [".str"];
  public static bool? MatchesSignature(ReadOnlySpan<byte> header) => StrReader.LooksPlausible(header);

  public static StrContainer FromSpan(ReadOnlySpan<byte> data) => StrReader.Open(data.ToArray());

  public static StrContainer FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return StrReader.Open(data);
  }

  public static StrContainer FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Sony PlayStation STR file not found.", file.FullName);
    return StrReader.Open(File.ReadAllBytes(file.FullName));
  }

  public static IReadOnlyList<MediaStreamInfo> Streams(StrContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    var video = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = StrReader.VideoCodec,
      Width = container.Width,
      Height = container.Height,
      TimeBase = Rational.Unknown,
      FrameRate = Rational.Unknown,
      DeclaredFrameCount = container.VideoFrameCount,
    };

    if (!container.HasAudio)
      return [video];

    var audio = new MediaStreamInfo {
      Index = 1,
      Kind = MediaStreamKind.Audio,
      Codec = StrReader.AudioCodec,
      TimeBase = container.AudioSampleRate > 0 ? new Rational(1, container.AudioSampleRate) : Rational.Unknown,
      SampleRate = container.AudioSampleRate,
      Channels = container.AudioChannels,
      BitsPerSample = container.AudioBitsPerSample,
    };

    return [video, audio];
  }

  public static IEnumerable<CodedPacket> ReadPackets(StrContainer container) {
    ArgumentNullException.ThrowIfNull(container);
    return StrReader.ReadPackets(container);
  }

  public static VideoMetadata Metadata(StrContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    var streams = Streams(container);
    var declared = new MediaStreamMetadata[streams.Count];
    for (var i = 0; i < streams.Count; ++i)
      declared[i] = new(streams[i].Index, streams[i].Kind, streams[i].Codec);

    return new() { Streams = declared };
  }
}
