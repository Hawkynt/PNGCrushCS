using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Cdxl;

/// <summary>
/// A Commodore CDXL file (the Amiga CDTV's own motion-video format, and later the AGA machines') taken
/// apart into the flat run of chunks it holds — and nothing else.
/// </summary>
/// <remarks>
/// Like id Cinematic, RoQ and Interplay MVE, this is its own container: nothing wraps it, and a file
/// only ever holds this one video codec and raw PCM audio, interleaved one chunk at a time rather than
/// alternating separate commands. See <see cref="CdxlChunkReader"/> for the format's own shape, the
/// plausibility check that stands in for the signature it does not carry, and what measurement against
/// four real files settled that its own documentation does not state.
/// </remarks>
[FormatMimeType("video/x-cdxl")]
public sealed class CdxlContainer : IVideoContainerReader<CdxlContainer> {

  /// <summary>The whole file, which every packet is a window onto.</summary>
  public required ReadOnlyMemory<byte> Data { get; init; }

  /// <summary>Picture width in pixels, read from the first chunk's header.</summary>
  public required int Width { get; init; }

  /// <summary>Picture height in pixels, read from the first chunk's header.</summary>
  public required int Height { get; init; }

  /// <summary>Whether the first chunk carries any sound at all.</summary>
  public required bool HasAudio { get; init; }

  /// <summary>The first chunk's stereo flag — bit 3 of its info byte.</summary>
  public required bool Stereo { get; init; }

  /// <summary>How many chunks the file holds, counted by walking it once.</summary>
  public required int FrameCount { get; init; }

  private static readonly CodecTag _VideoCodec = CodecTag.FromCharacters("CDXL");

  // -------- Format identity --------

  public static string PrimaryExtension => ".cdxl";

  public static string[] FileExtensions => [".cdxl"];

  /// <summary>
  /// The format carries no signature: a file opens straight into its first chunk's own header, with no
  /// fixed bytes anywhere for a reader to check. What is checked instead is <see
  /// cref="CdxlChunkReader.LooksPlausible"/> — a documented file type and video encoding, the one plane
  /// arrangement this reader can size a packet for, and a plausible picture size.
  /// </summary>
  public static bool? MatchesSignature(ReadOnlySpan<byte> header) => CdxlChunkReader.LooksPlausible(header) ? true : null;

  // -------- Demux --------

  public static CdxlContainer FromSpan(ReadOnlySpan<byte> data) => CdxlChunkReader.Open(data.ToArray());

  /// <summary>Opens a file over the caller's array, keeping it rather than copying it.</summary>
  public static CdxlContainer FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);

    return CdxlChunkReader.Open(data);
  }

  public static CdxlContainer FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("CDXL file not found.", file.FullName);

    return CdxlChunkReader.Open(File.ReadAllBytes(file.FullName));
  }

  public static IReadOnlyList<MediaStreamInfo> Streams(CdxlContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    var video = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = _VideoCodec,
      Width = container.Width,
      Height = container.Height,
      DeclaredFrameCount = container.FrameCount,
    };

    if (!container.HasAudio)
      return [video];

    // Standard uncompressed signed 8-bit PCM, mono or stereo by the first chunk's own stereo flag — a
    // synthetic tag the same way id Cinematic's audio is, since CDXL states no codec tag of its own for
    // either stream and nothing here decodes the sound: no sample this was measured against was checked
    // past the video. The file itself carries no sample rate; 11025 Hz is what every source describing
    // the format names as standard, and it is also what ffmpeg's own reader assumes for the same reason.
    var audio = new MediaStreamInfo {
      Index = 1,
      Kind = MediaStreamKind.Audio,
      Codec = CodecTag.FromCharacters(container.Stereo ? "CDX2" : "CDX1"),
      TimeBase = new(1, 11025),
    };

    return [video, audio];
  }

  public static IEnumerable<CodedPacket> ReadPackets(CdxlContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    return CdxlChunkReader.ReadPackets(container);
  }

  /// <summary>Nothing beyond the streams themselves. CDXL has no field for a title, an author or a
  /// creation date, and states no frame rate at all — playback speed is a decision made outside the
  /// file, which is why neither stream states a <see cref="MediaStreamInfo.FrameRate"/>.</summary>
  public static VideoMetadata Metadata(CdxlContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    var streams = Streams(container);
    var declared = new MediaStreamMetadata[streams.Count];
    for (var i = 0; i < streams.Count; ++i)
      declared[i] = new(streams[i].Index, streams[i].Kind, streams[i].Codec);

    return new() { Streams = declared };
  }
}
