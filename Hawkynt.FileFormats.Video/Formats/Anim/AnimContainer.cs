using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Anim;

/// <summary>
/// An IFF <c>FORM ANIM</c> file — the Amiga's own CEL animation format — taken apart into the flat run
/// of <c>FORM ILBM</c> frames it holds, and nothing else.
/// </summary>
/// <remarks>
/// Like id Cinematic, RoQ, Interplay MVE and CDXL, this is its own container built directly on IFF
/// rather than something that wraps another format. See <see cref="AnimChunkReader"/> for how this
/// differs from decoding an ordinary IFF file — the whole point of a container is windows onto the
/// file, not a tree of copies — and for the specification this format's own shape is read from.
/// </remarks>
[FormatMimeType("video/x-anim")]
public sealed class AnimContainer : IVideoContainerReader<AnimContainer> {

  public required ReadOnlyMemory<byte> Data { get; init; }
  public required int Width { get; init; }
  public required int Height { get; init; }
  public required int FrameCount { get; init; }

  private static readonly CodecTag _VideoCodec = CodecTag.FromCharacters("ANIM");
  private static readonly Rational _TimeBase = new(1, 60);

  public static string PrimaryExtension => ".anim";
  public static string[] FileExtensions => [".anim", ".iff"];
  public static bool? MatchesSignature(ReadOnlySpan<byte> header) => AnimChunkReader.LooksPlausible(header) ? true : null;

  public static AnimContainer FromSpan(ReadOnlySpan<byte> data) => AnimChunkReader.Open(data.ToArray());

  public static AnimContainer FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return AnimChunkReader.Open(data);
  }

  public static AnimContainer FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("IFF ANIM file not found.", file.FullName);
    return AnimChunkReader.Open(File.ReadAllBytes(file.FullName));
  }

  public static IReadOnlyList<MediaStreamInfo> Streams(AnimContainer container) {
    ArgumentNullException.ThrowIfNull(container);
    return [
      new MediaStreamInfo {
        Index = 0,
        Kind = MediaStreamKind.Video,
        Codec = _VideoCodec,
        TimeBase = _TimeBase,
        Width = container.Width,
        Height = container.Height,
        DeclaredFrameCount = container.FrameCount,
      }
    ];
  }

  public static IEnumerable<CodedPacket> ReadPackets(AnimContainer container) {
    ArgumentNullException.ThrowIfNull(container);
    return AnimChunkReader.ReadPackets(container);
  }

  public static VideoMetadata Metadata(AnimContainer container) {
    ArgumentNullException.ThrowIfNull(container);
    var streams = Streams(container);
    var declared = new MediaStreamMetadata[streams.Count];
    for (var i = 0; i < streams.Count; ++i)
      declared[i] = new(streams[i].Index, streams[i].Kind, streams[i].Codec);
    return new() { Streams = declared };
  }
}
