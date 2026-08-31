using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Idcin;

/// <summary>
/// An id Cinematic file (Quake II's <c>.cin</c>) taken apart into its header fields, its 64KiB Huffman
/// table, and the frame commands it holds — and nothing else.
/// </summary>
/// <remarks>
/// Like RoQ and Interplay MVE, this is its own container: nothing wraps it, because a file only ever
/// holds this one video codec and raw PCM audio. Unlike either of those, it carries no signature at
/// all — see <see cref="IdcinReader"/> for the plausibility check that stands in for one. What still
/// makes it worth splitting into demux and decode is the same seam as always: where a frame's bytes
/// are is a different question from what a Huffman-coded byte means, and only <see
/// cref="Codecs.IdcinVideoDecoder"/> answers the second one.
/// </remarks>
[FormatMimeType("video/x-idcin")]
public sealed class IdcinContainer : IVideoContainerReader<IdcinContainer> {
  /// <summary>Initializes a new instance of this type.</summary>
  public IdcinContainer() { }

  /// <summary>The whole file, which every packet is a window onto.</summary>
  public required ReadOnlyMemory<byte> Data { get; init; }

  /// <summary>Picture width in pixels, read directly from the header.</summary>
  public required int Width { get; init; }

  /// <summary>Picture height in pixels, read directly from the header.</summary>
  public required int Height { get; init; }

  /// <summary>The audio sample rate the header states, in hertz, or zero for a file with no sound.</summary>
  public required int AudioSampleRate { get; init; }

  /// <summary>Audio sample width in bytes — one or two — or zero where <see cref="AudioSampleRate"/> is zero.</summary>
  public required int AudioBytesPerSample { get; init; }

  /// <summary>Audio channel count — one or two — or zero where <see cref="AudioSampleRate"/> is zero.</summary>
  public required int AudioChannels { get; init; }

  /// <summary>How many video frame commands the file holds, counted by walking it once.</summary>
  public required int VideoFrameCount { get; init; }

  /// <summary>
  /// The 64KiB table right after the header: 256 histograms of 256 bytes each, one per possible
  /// previous-pixel value, from which <see cref="Codecs.IdcinVideoDecoder"/> builds 256 Huffman trees.
  /// </summary>
  public required ReadOnlyMemory<byte> HuffmanTable { get; init; }

  private static readonly Rational _VIDEO_TIME_BASE = new(1, 14);
  private static readonly CodecTag _VIDEO_CODEC = CodecTag.FromCharacters("IDCV");

  // -------- Format identity --------

  /// <summary>Gets the primary file extension for this format.</summary>
  public static string PrimaryExtension => ".cin";

  /// <summary>Gets the file extensions supported by this format.</summary>
  public static string[] FileExtensions => [".cin"];

  /// <summary>
  /// The format carries no signature: a file opens with five header words and then straight into a
  /// Huffman table, with no fixed bytes anywhere in it a reader could check. What is checked instead is
  /// a plausibility heuristic of <see cref="IdcinReader"/>'s own devising: a plausible picture size,
  /// and — where a sample rate is stated at all — a plausible sample width and channel count.
  /// </summary>
  public static bool? MatchesSignature(ReadOnlySpan<byte> header) => IdcinReader.LooksPlausible(header) ? true : null;

  // -------- Demux --------

  /// <summary>Reads an instance from the specified byte span.</summary>
  public static IdcinContainer FromSpan(ReadOnlySpan<byte> data) => IdcinReader.Open(data.ToArray());

  /// <summary>Opens a file over the caller's array, keeping it rather than copying it.</summary>
  public static IdcinContainer FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);

    return IdcinReader.Open(data);
  }

  /// <summary>Reads an instance from the specified file.</summary>
  public static IdcinContainer FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("id Cinematic file not found.", file.FullName);

    return IdcinReader.Open(File.ReadAllBytes(file.FullName));
  }

  /// <summary>Gets the media streams declared by the specified container.</summary>
  public static IReadOnlyList<MediaStreamInfo> Streams(IdcinContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    var video = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = _VIDEO_CODEC,
      Width = container.Width,
      Height = container.Height,
      TimeBase = _VIDEO_TIME_BASE,
      FrameRate = new(14, 1),
      DeclaredFrameCount = container.VideoFrameCount,
      CodecPrivateData = container.HuffmanTable,
    };

    if (container.AudioSampleRate == 0)
      return [video];

    var audio = new MediaStreamInfo {
      Index = 1,
      Kind = MediaStreamKind.Audio,
      Codec = _AudioCodec(container.AudioBytesPerSample, container.AudioChannels),
      TimeBase = new(1, container.AudioSampleRate),
    };

    return [video, audio];
  }

  /// <summary>
  /// A synthetic tag naming raw PCM as this format actually carries it: unsigned bytes at one sample
  /// width, signed little-endian words at the other, mono or stereo either way. Nothing in this
  /// library decodes it — no sample this was measured against was checked past the video — but a
  /// stream that goes undescribed cannot later be told apart from one this container failed to read.
  /// </summary>
  private static CodecTag _AudioCodec(int bytesPerSample, int channels) => CodecTag.FromCharacters(
    (bytesPerSample, channels) switch {
      (1, 1) => "ICBM",
      (1, _) => "ICBS",
      (_, 1) => "ICWM",
      _ => "ICWS",
    });

  /// <summary>Enumerates coded packets from the specified container.</summary>
  public static IEnumerable<CodedPacket> ReadPackets(IdcinContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    return IdcinReader.ReadPackets(container);
  }

  /// <summary>Nothing beyond the streams themselves. An id Cinematic file has no field for a title, an
  /// author or a creation date.</summary>
  public static VideoMetadata Metadata(IdcinContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    var streams = Streams(container);
    var declared = new MediaStreamMetadata[streams.Count];
    for (var i = 0; i < streams.Count; ++i)
      declared[i] = new(streams[i].Index, streams[i].Kind, streams[i].Codec);

    return new() { Streams = declared };
  }
}
