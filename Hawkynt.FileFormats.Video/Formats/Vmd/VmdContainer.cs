using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Vmd;

/// <summary>
/// A Sierra VMD file (<c>.vmd</c>) — "Video and Music Data", the format behind Phantasmagoria, Gabriel
/// Knight 2 and Sierra's other CD-ROM adventures — taken apart into its header fields and the packets
/// its table of contents describes, and nothing else.
/// </summary>
/// <remarks>
/// Like RoQ, Interplay MVE and Westwood VQA, this is its own container: nothing wraps it, and a file
/// only ever holds this one video codec and this format's own audio. Unlike any of those three, its
/// packet boundaries are not found by walking chunks forward from the start — they are stated in a
/// table of contents near the end of the file, which is also where a video frame's rectangle and a
/// new-palette flag live, apart from the compressed bytes they describe. See <see cref="VmdReader"/>
/// for the header and table-of-contents walk that answers only where a frame's bytes are; what a
/// frame's bytes mean is <see cref="Codecs.VmdVideoDecoder"/>'s alone.
/// <para/>
/// <b>Only the classic 816-byte header is read.</b> A later revision of the format — carried by
/// Coktel Vision's own edutainment titles rather than by any Sierra release this reader was measured
/// against — states a shorter, palette-free 52-byte header, or the same 816 bytes with two extra
/// fields naming an external audio codec; and at least one sample carrying that later revision's own
/// table of contents does not fit this reader's frame-type-1-or-2 model at all, holding record types
/// this reader has no independent description of the meaning of. Both are refused by name rather than
/// guessed at.
/// </remarks>
[FormatMimeType("video/x-vmd")]
public sealed class VmdContainer : IVideoContainerReader<VmdContainer> {

  /// <summary>The whole file, which every packet is a window or a small reconstruction onto.</summary>
  public required ReadOnlyMemory<byte> Data { get; init; }

  /// <summary>Picture width in pixels.</summary>
  public required int Width { get; init; }

  /// <summary>Picture height in pixels.</summary>
  public required int Height { get; init; }

  /// <summary>How many video-type records the table of contents holds.</summary>
  public required int VideoFrameCount { get; init; }

  /// <summary>Whether the file carries sound: the header's own flag, corroborated by at least one
  /// audio-type record actually present in the table of contents.</summary>
  public required bool HasAudio { get; init; }

  /// <summary>The audio sample rate the header states, in hertz, or zero for a file with none.</summary>
  public required int AudioSampleRate { get; init; }

  /// <summary>The audio frame length the header states, in samples — negative in the header for
  /// sixteen-bit sound, unsigned here since nothing in this container reads sound.</summary>
  public required int AudioFrameLength { get; init; }

  /// <summary>The video codec version the header states — <c>1</c> for the eight-bit palettised form
  /// every sample this reader was measured against carries. Left for the codec to read and to refuse
  /// by name, the same way a container never itself refuses a codec it has not decoded.</summary>
  public required int CodecVersion { get; init; }

  /// <summary>The 816-byte header verbatim, carried through as the video stream's private data so the
  /// codec can read the codec version and the initial palette this container does not interpret.</summary>
  public required ReadOnlyMemory<byte> HeaderPayload { get; init; }

  /// <summary>The table of contents' own absolute file offset, as the header states it.</summary>
  public required uint TocOffset { get; init; }

  /// <summary>How many records the block offset table holds — the same count as <see cref="BlockOffsets"/>'s
  /// length, kept as its own field because it is also what locates the frame information table.</summary>
  public required int NumBlocks { get; init; }

  /// <summary>
  /// Each block's own absolute file offset, in the order the block offset table states them. Not
  /// needed to walk the frame data sequentially — see <see cref="VmdReader"/>'s remarks — but needed
  /// for the one thing this table exists for: naming which block a video frame belongs to, which is
  /// the presentation timestamp <see cref="ReadPackets(VmdContainer)"/> reports for it. A block may
  /// hold more than one audio frame and no video frame at all — measured directly against a real
  /// file, where a run of blocks carrying only extra sound leaves the timestamps of the video frames
  /// around them further apart than one block — so a plain running count of video frames does not
  /// reproduce it and the block table is what does.
  /// </summary>
  public required IReadOnlyList<int> BlockOffsets { get; init; }

  /// <summary>How many records the frame information table holds.</summary>
  public required int FrameCount { get; init; }

  /// <summary>The frame information table's own absolute file offset.</summary>
  public required int FrameTableStart { get; init; }

  /// <summary>Where the coded frame data begins — always 816, immediately after the fixed-size
  /// header, in every sample this reader was measured against.</summary>
  public required uint MultimediaDataOffset { get; init; }

  private static readonly CodecTag _VIDEO_CODEC = CodecTag.FromCharacters("VMDV");
  private static readonly CodecTag _AUDIO_CODEC = CodecTag.FromCharacters("VMDA");

  // -------- Format identity --------

  public static string PrimaryExtension => ".vmd";

  public static string[] FileExtensions => [".vmd"];

  public static bool? MatchesSignature(ReadOnlySpan<byte> header) => VmdReader.LooksPlausible(header) ? true : null;

  // -------- Demux --------

  public static VmdContainer FromSpan(ReadOnlySpan<byte> data) => VmdReader.Open(data.ToArray());

  /// <summary>Opens a file over the caller's array, keeping it rather than copying it.</summary>
  public static VmdContainer FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);

    return VmdReader.Open(data);
  }

  public static VmdContainer FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Sierra VMD file not found.", file.FullName);

    return VmdReader.Open(File.ReadAllBytes(file.FullName));
  }

  public static IReadOnlyList<MediaStreamInfo> Streams(VmdContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    var video = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = _VIDEO_CODEC,
      Width = container.Width,
      Height = container.Height,
      TimeBase = container.AudioSampleRate != 0 && container.AudioFrameLength != 0
        ? new Rational(container.AudioFrameLength, container.AudioSampleRate)
        : Rational.Unknown,
      FrameRate = container.AudioSampleRate != 0 && container.AudioFrameLength != 0
        ? new Rational(container.AudioSampleRate, container.AudioFrameLength)
        : Rational.Unknown,
      DeclaredFrameCount = container.VideoFrameCount,
      CodecPrivateData = container.HeaderPayload,
    };

    if (!container.HasAudio)
      return [video];

    var audio = new MediaStreamInfo {
      Index = 1,
      Kind = MediaStreamKind.Audio,
      Codec = _AUDIO_CODEC,
      TimeBase = new Rational(1, container.AudioSampleRate),
    };

    return [video, audio];
  }

  public static IEnumerable<CodedPacket> ReadPackets(VmdContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    return VmdReader.ReadPackets(container);
  }

  /// <summary>Nothing beyond the streams themselves. A VMD file has no field for a title, an author or
  /// a creation date.</summary>
  public static VideoMetadata Metadata(VmdContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    var streams = Streams(container);
    var declared = new MediaStreamMetadata[streams.Count];
    for (var i = 0; i < streams.Count; ++i)
      declared[i] = new(streams[i].Index, streams[i].Kind, streams[i].Codec);

    return new() { Streams = declared };
  }
}
