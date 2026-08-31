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
  /// <summary>Initializes a new instance of this type.</summary>
  public AnimContainer() { }

  /// <summary>The whole file, which every packet is a window onto.</summary>
  public required ReadOnlyMemory<byte> Data { get; init; }

  /// <summary>Picture width in pixels, read from the first frame's BMHD.</summary>
  public required int Width { get; init; }

  /// <summary>Picture height in pixels, read from the first frame's BMHD.</summary>
  public required int Height { get; init; }

  /// <summary>How many <c>FORM ILBM</c> frames the file holds, counted by walking it once.</summary>
  public required int FrameCount { get; init; }

  private static readonly CodecTag _VideoCodec = CodecTag.FromCharacters("ANIM");

  // -------- Format identity --------

  /// <summary>Gets the primary file extension for this format.</summary>
  public static string PrimaryExtension => ".anim";

  /// <summary>Gets the file extensions supported by this format.</summary>
  public static string[] FileExtensions => [".anim", ".iff"];

  /// <summary>Determines whether the supplied header matches this file format.</summary>
  public static bool? MatchesSignature(ReadOnlySpan<byte> header) => AnimChunkReader.LooksPlausible(header) ? true : null;

  // -------- Demux --------

  /// <summary>Reads an instance from the specified byte span.</summary>
  public static AnimContainer FromSpan(ReadOnlySpan<byte> data) => AnimChunkReader.Open(data.ToArray());

  /// <summary>Opens a file over the caller's array, keeping it rather than copying it.</summary>
  public static AnimContainer FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);

    return AnimChunkReader.Open(data);
  }

  /// <summary>Reads an instance from the specified file.</summary>
  public static AnimContainer FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("IFF ANIM file not found.", file.FullName);

    return AnimChunkReader.Open(File.ReadAllBytes(file.FullName));
  }

  /// <summary>Gets the media streams declared by the specified container.</summary>
  public static IReadOnlyList<MediaStreamInfo> Streams(AnimContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    return [
      new MediaStreamInfo {
        Index = 0,
        Kind = MediaStreamKind.Video,
        Codec = _VideoCodec,
        Width = container.Width,
        Height = container.Height,
        DeclaredFrameCount = container.FrameCount,
      }
    ];
  }

  /// <summary>Enumerates coded packets from the specified container.</summary>
  public static IEnumerable<CodedPacket> ReadPackets(AnimContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    return AnimChunkReader.ReadPackets(container);
  }

  /// <summary>Nothing beyond the stream itself. IFF ANIM has no field for a title, an author or a
  /// creation date.</summary>
  public static VideoMetadata Metadata(AnimContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    var streams = Streams(container);
    var declared = new MediaStreamMetadata[streams.Count];
    for (var i = 0; i < streams.Count; ++i)
      declared[i] = new(streams[i].Index, streams[i].Kind, streams[i].Codec);

    return new() { Streams = declared };
  }
}
