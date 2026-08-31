using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Bfi;

/// <summary>
/// A BFI file — Tsunami Media's "Brute Force &amp; Ignorance" — taken apart into its palette and the flat
/// run of <c>IVAS</c> chunks it holds, each one frame's interleaved audio and video, and nothing else.
/// </summary>
/// <remarks>
/// Like id Cinematic, RoQ, Interplay MVE, CDXL and IFF ANIM, this is its own container: nothing wraps
/// it. See <see cref="BfiChunkReader"/> for the header and chunk layout this reads and where it comes
/// from.
/// </remarks>
[FormatMimeType("video/x-bfi")]
public sealed class BfiContainer : IVideoContainerReader<BfiContainer> {
  /// <summary>Initializes a new instance of this type.</summary>
  public BfiContainer() { }

  /// <summary>The whole file, which every packet is a window onto.</summary>
  public required ReadOnlyMemory<byte> Data { get; init; }

  /// <summary>Picture width in pixels, read from the header.</summary>
  public required int Width { get; init; }

  /// <summary>Picture height in pixels, read from the header.</summary>
  public required int Height { get; init; }

  /// <summary>The 256-entry, six-bit-per-channel VGA palette every frame in the file is drawn through.</summary>
  public required ReadOnlyMemory<byte> Palette { get; init; }

  /// <summary>How many <c>IVAS</c> chunks the file holds, counted by walking it once.</summary>
  public required int FrameCount { get; init; }

  /// <summary>The header's own audio sample rate, in hertz.</summary>
  public required int SampleRate { get; init; }

  /// <summary>The header's own channel count — one or two, defaulting to one where the field states
  /// neither, since the field's own meaning is not confirmed by any source this was measured against.</summary>
  public required int Channels { get; init; }

  private static readonly CodecTag _VideoCodec = CodecTag.FromCharacters("BFIV");

  // -------- Format identity --------

  /// <summary>Gets the primary file extension for this format.</summary>
  public static string PrimaryExtension => ".bfi";

  /// <summary>Gets the file extensions supported by this format.</summary>
  public static string[] FileExtensions => [".bfi"];

  /// <summary>Determines whether the supplied header matches this file format.</summary>
  public static bool? MatchesSignature(ReadOnlySpan<byte> header) => BfiChunkReader.LooksPlausible(header) ? true : null;

  // -------- Demux --------

  /// <summary>Reads an instance from the specified byte span.</summary>
  public static BfiContainer FromSpan(ReadOnlySpan<byte> data) => BfiChunkReader.Open(data.ToArray());

  /// <summary>Opens a file over the caller's array, keeping it rather than copying it.</summary>
  public static BfiContainer FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);

    return BfiChunkReader.Open(data);
  }

  /// <summary>Reads an instance from the specified file.</summary>
  public static BfiContainer FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("BFI file not found.", file.FullName);

    return BfiChunkReader.Open(File.ReadAllBytes(file.FullName));
  }

  /// <summary>Gets the media streams declared by the specified container.</summary>
  public static IReadOnlyList<MediaStreamInfo> Streams(BfiContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    var video = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = _VideoCodec,
      Width = container.Width,
      Height = container.Height,
      DeclaredFrameCount = container.FrameCount,
      CodecPrivateData = container.Palette,
    };

    // Standard unsigned 8-bit PCM, per the header's own sample rate — a synthetic tag the same way
    // CDXL's and id Cinematic's audio is, since nothing here decodes the sound: no sample this was
    // measured against was checked past the video.
    var audio = new MediaStreamInfo {
      Index = 1,
      Kind = MediaStreamKind.Audio,
      Codec = CodecTag.FromCharacters(container.Channels == 2 ? "BFI2" : "BFI1"),
      TimeBase = new(1, container.SampleRate > 0 ? container.SampleRate : 11025),
    };

    return [video, audio];
  }

  /// <summary>Enumerates coded packets from the specified container.</summary>
  public static IEnumerable<CodedPacket> ReadPackets(BfiContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    return BfiChunkReader.ReadPackets(container);
  }

  /// <summary>Nothing beyond the streams themselves. BFI has no field for a title, an author or a
  /// creation date.</summary>
  public static VideoMetadata Metadata(BfiContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    var streams = Streams(container);
    var declared = new MediaStreamMetadata[streams.Count];
    for (var i = 0; i < streams.Count; ++i)
      declared[i] = new(streams[i].Index, streams[i].Kind, streams[i].Codec);

    return new() { Streams = declared };
  }
}
