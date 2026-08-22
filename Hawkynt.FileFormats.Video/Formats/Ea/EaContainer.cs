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
/// <remarks>
/// Like RoQ, Interplay MVE and id Cinematic, this is a container with essentially nothing beneath it:
/// a flat run of self-delimiting chunks, each a four-character FourCC, a four-byte little-endian size
/// that includes those first eight bytes, and a payload — the block structure Electronic Arts used for
/// every one of its own game and cinematic formats, PC/PS/Xbox little-endian and Mac/Saturn/GameCube
/// big-endian alike; only the little-endian PC form is read here, since every sample this was measured
/// against is one. <see cref="EaReader"/> answers only where each chunk is and which of the handful of
/// FourCCs this reader gives meaning to it is; <see cref="Codecs.EaCmvVideoDecoder"/> is the only thing
/// here that reads a motion byte or a coded pixel.
/// <para/>
/// A file is not one video stream from start to end. <c>TITLE.CMV</c> — the one sample this container
/// was built and measured against — closes its first forty-nine pictures with an <c>MVIe</c> chunk and
/// then opens straight back into a fresh <c>MVIh</c>, restating the palette for a second run of
/// pictures the same stream continues into; nothing here treats that boundary as anything but another
/// header restatement; <c>MVIe</c> itself carries no payload and needs no handling beyond being skipped.
/// </remarks>
[FormatMimeType("video/x-ea")]
public sealed class EaContainer : IVideoContainerReader<EaContainer> {

  /// <summary>The whole file, which every packet is a window onto.</summary>
  public required ReadOnlyMemory<byte> Data { get; init; }

  /// <summary>Which of Electronic Arts' own video codecs this file's chunks belong to, judged by
  /// whichever family of video chunk FourCCs is seen first while summarising the file.</summary>
  public required EaVideoCodecKind VideoCodec { get; init; }

  /// <summary>Picture width in pixels, read from the first chunk that states one.</summary>
  public required int Width { get; init; }

  /// <summary>Picture height in pixels, read from the first chunk that states one.</summary>
  public required int Height { get; init; }

  /// <summary>The frame rate an <c>MVIh</c> chunk states, in frames per second — zero for a codec
  /// whose header carries none, which is every one of EA's video codecs except CMV.</summary>
  public required int FrameRate { get; init; }

  /// <summary>How many coded pictures the file holds, counted by walking it once.</summary>
  public required int VideoFrameCount { get; init; }

  // -------- Format identity --------

  public static string PrimaryExtension => ".wve";

  public static string[] FileExtensions => [".wve", ".cmv", ".tgv", ".uv", ".uv2"];

  /// <summary>
  /// The format carries no single fixed signature — a CMV file opens with <c>MVIh</c>, a TGV file with
  /// either <c>kVGT</c> directly or an audio header chunk before it — so what stands in for one is
  /// whether the first four bytes name a chunk kind this family is built from and the four after it
  /// state a size that is at least the eight-byte header those bytes are themselves part of.
  /// </summary>
  public static bool? MatchesSignature(ReadOnlySpan<byte> header) => EaReader.LooksPlausible(header) ? true : null;

  // -------- Demux --------

  public static EaContainer FromSpan(ReadOnlySpan<byte> data) => EaReader.Open(data.ToArray());

  /// <summary>Opens a file over the caller's array, keeping it rather than copying it.</summary>
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

    if (container.VideoCodec == EaVideoCodecKind.None)
      return [];

    var (tag, timeBase, frameRate) = container.VideoCodec == EaVideoCodecKind.Cmv
      ? (CodecTag.FromCharacters("cmv "), container.FrameRate > 0 ? new Rational(1, container.FrameRate) : Rational.Unknown, container.FrameRate > 0 ? new Rational(container.FrameRate, 1) : Rational.Unknown)
      : (CodecTag.FromCharacters("tgv "), Rational.Unknown, Rational.Unknown);

    var video = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = tag,
      Width = container.Width,
      Height = container.Height,
      TimeBase = timeBase,
      FrameRate = frameRate,
      DeclaredFrameCount = container.VideoFrameCount,
    };

    return [video];
  }

  public static IEnumerable<CodedPacket> ReadPackets(EaContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    return EaReader.ReadPackets(container);
  }

  /// <summary>Nothing beyond the streams themselves. Nothing found in this family states a title, an
  /// author or a creation date at the container level.</summary>
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
  /// <summary>No chunk this reader recognises as video was found.</summary>
  None,
  /// <summary>The file's video chunks are <c>MVIh</c>/<c>MVIf</c>/<c>MVIe</c> — Electronic Arts CMV.</summary>
  Cmv,
  /// <summary>The file's video chunks are <c>kVGT</c>/<c>fVGT</c> — Electronic Arts TGV.</summary>
  Tgv,
}
