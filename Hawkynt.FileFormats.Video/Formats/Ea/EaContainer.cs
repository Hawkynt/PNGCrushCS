using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Ea;

/// <summary>
/// An Electronic Arts multimedia file — the flat chunked container behind <c>.wve</c>, <c>.cmv</c>,
/// <c>.tgv</c>, <c>.uv</c> and the rest of EA's own family of cinematic wrappers — taken apart into the
/// chunks it holds and nothing else.
/// </summary>
[FormatMimeType("video/x-ea")]
public sealed class EaContainer : IVideoContainerReader<EaContainer> {

  public required ReadOnlyMemory<byte> Data { get; init; }
  public required EaVideoCodecKind VideoCodec { get; init; }
  public required int Width { get; init; }
  public required int Height { get; init; }
  public required int FrameRate { get; init; }
  public required int VideoFrameCount { get; init; }

  /// <summary>Whether at least one documented EA sound-family chunk occurs in the file.</summary>
  public required bool HasAudio { get; init; }

  public static string PrimaryExtension => ".wve";
  public static string[] FileExtensions => [".wve", ".cmv", ".tgv", ".uv", ".uv2"];

  public static bool? MatchesSignature(ReadOnlySpan<byte> header) => EaReader.LooksPlausible(header) ? true : null;

  public static EaContainer FromSpan(ReadOnlySpan<byte> data) => EaReader.Open(data.ToArray());

  public static EaContainer FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return EaReader.Open(data);
  }

  public static EaContainer FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Electronic Arts multimedia file not found.", file.FullName);
    return EaReader.Open(File.ReadAllBytes(file.FullName));
  }

  public static IReadOnlyList<MediaStreamInfo> Streams(EaContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    var streams = new List<MediaStreamInfo>(2);
    if (container.VideoCodec != EaVideoCodecKind.None) {
      var (tag, timeBase, frameRate) = container.VideoCodec == EaVideoCodecKind.Cmv
        ? (CodecTag.FromCharacters("cmv "), container.FrameRate > 0 ? new Rational(1, container.FrameRate) : Rational.Unknown, container.FrameRate > 0 ? new Rational(container.FrameRate, 1) : Rational.Unknown)
        : (CodecTag.FromCharacters("tgv "), Rational.Unknown, Rational.Unknown);

      streams.Add(new() {
        Index = streams.Count,
        Kind = MediaStreamKind.Video,
        Codec = tag,
        Width = container.Width,
        Height = container.Height,
        TimeBase = timeBase,
        FrameRate = frameRate,
        DeclaredFrameCount = container.VideoFrameCount,
      });
    }

    if (container.HasAudio)
      streams.Add(new() {
        Index = streams.Count,
        Kind = MediaStreamKind.Audio,
        // This is the chunk protocol, not a claim about the codec nested inside SCHl/SEAD/etc.
        Codec = CodecTag.FromCharacters("EAAU"),
      });

    return streams;
  }

  public static IEnumerable<CodedPacket> ReadPackets(EaContainer container) {
    ArgumentNullException.ThrowIfNull(container);
    return EaReader.ReadPackets(container);
  }

  public static VideoMetadata Metadata(EaContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    var streams = Streams(container);
    var declared = new MediaStreamMetadata[streams.Count];
    for (var i = 0; i < streams.Count; ++i)
      declared[i] = new(streams[i].Index, streams[i].Kind, streams[i].Codec);

    return new() { Streams = declared };
  }
}

/// <summary>Which of Electronic Arts' own video codecs an <see cref="EaContainer"/>'s chunks belong to.</summary>
public enum EaVideoCodecKind {
  None,
  Cmv,
  Tgv,
}
