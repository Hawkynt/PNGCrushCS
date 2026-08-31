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
  /// <summary>Initializes a new instance of this type.</summary>
  public StrContainer() { }

  /// <summary>Gets the data.</summary>
  public required ReadOnlyMemory<byte> Data { get; init; }
  /// <summary>Gets the sync Start.</summary>
  public required int SyncStart { get; init; }
  /// <summary>Gets the sector Count.</summary>
  public required int SectorCount { get; init; }
  /// <summary>Gets the width.</summary>
  public required int Width { get; init; }
  /// <summary>Gets the height.</summary>
  public required int Height { get; init; }
  /// <summary>Gets the video Frame Count.</summary>
  public required int VideoFrameCount { get; init; }
  /// <summary>Gets a value indicating whether this instance has audio.</summary>
  public required bool HasAudio { get; init; }
  /// <summary>Gets the audio Packet Count.</summary>
  public required int AudioPacketCount { get; init; }

  /// <summary>XA-ADPCM sample rate stated by the audio sectors' coding-information byte.</summary>
  public required int AudioSampleRate { get; init; }

  /// <summary>XA-ADPCM channel count stated by the audio sectors' coding-information byte.</summary>
  public required int AudioChannels { get; init; }

  /// <summary>XA-ADPCM coded sample precision stated by the audio sectors' coding-information byte.</summary>
  public required int AudioBitsPerSample { get; init; }

  /// <summary>Gets the primary file extension for this format.</summary>
  public static string PrimaryExtension => ".str";
  /// <summary>Gets the file extensions supported by this format.</summary>
  public static string[] FileExtensions => [".str"];
  /// <summary>Determines whether the supplied header matches this file format.</summary>
  public static bool? MatchesSignature(ReadOnlySpan<byte> header) => StrReader.LooksPlausible(header);

  /// <summary>Reads an instance from the specified byte span.</summary>
  public static StrContainer FromSpan(ReadOnlySpan<byte> data) => StrReader.Open(data.ToArray());

  /// <summary>Reads an instance from the specified byte array.</summary>
  public static StrContainer FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return StrReader.Open(data);
  }

  /// <summary>Reads an instance from the specified file.</summary>
  public static StrContainer FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Sony PlayStation STR file not found.", file.FullName);
    return StrReader.Open(File.ReadAllBytes(file.FullName));
  }

  /// <summary>Gets the media streams declared by the specified container.</summary>
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

  /// <summary>Enumerates coded packets from the specified container.</summary>
  public static IEnumerable<CodedPacket> ReadPackets(StrContainer container) {
    ArgumentNullException.ThrowIfNull(container);
    return StrReader.ReadPackets(container);
  }

  /// <summary>Gets the metadata exposed by the specified container.</summary>
  public static VideoMetadata Metadata(StrContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    var streams = Streams(container);
    var declared = new MediaStreamMetadata[streams.Count];
    for (var i = 0; i < streams.Count; ++i)
      declared[i] = new(streams[i].Index, streams[i].Kind, streams[i].Codec);

    return new() { Streams = declared };
  }
}
