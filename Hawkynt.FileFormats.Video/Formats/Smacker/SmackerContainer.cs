using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.SmackerVideo;

/// <summary>
/// A Smacker file (<c>.smk</c>) — RAD Game Tools' own FMV format, behind more games than any other one
/// in this package's game-and-FMV family — taken apart into its streams and its frames, and nothing
/// else.
/// </summary>
/// <remarks>
/// Smacker is its own container the way RoQ, Interplay MVE and Westwood VQA are: a file holds Smacker
/// Video and, optionally, up to seven tracks of RAD's own audio coding, and nothing wraps either one.
/// Unlike those three, nothing here is chunked — a fixed header states two parallel per-frame arrays
/// and one shared section of packed Huffman trees, and the frames themselves follow with no framing of
/// their own beyond what those arrays already said. See <see cref="SmackerReader"/> for the header and
/// array layout, checked against real files rather than taken on the documentation's word alone.
/// <para/>
/// <b>Two signatures, one codec.</b> <c>SMK2</c> is the original bitstream and <c>SMK4</c> a later
/// revision whose "full colour" block gained two coding shapes SMK2 never emitted; both are handed
/// through as the stream's own <see cref="MediaStreamInfo.Codec"/> tag exactly as the file states it,
/// the same way this package keeps ffmpeg's own <c>WMV1</c>/<c>WMV2</c> or <c>RV10</c>/<c>RV13</c>
/// apart — a decoder reads whichever tag names the bitstream shape it is looking at.
/// </remarks>
[FormatMimeType("video/x-smacker")]
public sealed class SmackerContainer : IVideoContainerReader<SmackerContainer> {

  /// <summary>The whole file, which every packet is a window onto.</summary>
  public required ReadOnlyMemory<byte> Data { get; init; }

  /// <summary>The file's own four-byte signature, <c>SMK2</c> or <c>SMK4</c>, carried through as the
  /// video stream's codec tag.</summary>
  public required uint Signature { get; init; }

  /// <summary>Picture width in pixels.</summary>
  public required int Width { get; init; }

  /// <summary>Picture height in pixels.</summary>
  public required int Height { get; init; }

  /// <summary>How many video frames the file holds, the header's own <c>Frames</c> field plus one more
  /// where the header's flags state an extra "ring" frame not counted in that field.</summary>
  public required int VideoFrameCount { get; init; }

  /// <summary>The time one video frame occupies, derived from the header's signed <c>FrameRate</c>
  /// field — see <see cref="SmackerReader"/> for the two ways that field is read.</summary>
  public required Rational VideoTimeBase { get; init; }

  /// <summary>Every frame's stated length in bytes, used exactly as the header states it.</summary>
  public required int[] FrameSizes { get; init; }

  /// <summary>Every frame's flag byte: bit zero for a palette chunk, bits one through seven for which
  /// of the seven possible audio tracks contribute a chunk to that frame.</summary>
  public required byte[] FrameTypes { get; init; }

  /// <summary>The video stream's private data: the four in-memory table sizes the header states for
  /// the MMap, MClr, Full and Type Huffman tables, followed by the packed tree bytes themselves.</summary>
  public required ReadOnlyMemory<byte> CodecPrivateData { get; init; }

  /// <summary>Where in <see cref="Data"/> the frames themselves begin, past the header, the two
  /// per-frame arrays and the shared tree section.</summary>
  public required int FramesDataOffset { get; init; }

  /// <summary>The header's own seven <c>AudioRate</c> dwords, one a possible track, verbatim.</summary>
  public required uint[] AudioTrackRates { get; init; }

  private static readonly CodecTag _AUDIO_CODEC = CodecTag.FromCharacters("SMKA");

  // -------- Format identity --------

  public static string PrimaryExtension => ".smk";

  public static string[] FileExtensions => [".smk"];

  public static bool? MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 4 && (header[..4].SequenceEqual("SMK2"u8) || header[..4].SequenceEqual("SMK4"u8))
      ? true
      : null;

  // -------- Demux --------

  public static SmackerContainer FromSpan(ReadOnlySpan<byte> data) => SmackerReader.Open(data.ToArray());

  /// <summary>Opens a file over the caller's array, keeping it rather than copying it.</summary>
  public static SmackerContainer FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);

    return SmackerReader.Open(data);
  }

  public static SmackerContainer FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Smacker file not found.", file.FullName);

    return SmackerReader.Open(File.ReadAllBytes(file.FullName));
  }

  public static IReadOnlyList<MediaStreamInfo> Streams(SmackerContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    var video = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = new(container.Signature),
      Width = container.Width,
      Height = container.Height,
      TimeBase = container.VideoTimeBase,
      FrameRate = new(container.VideoTimeBase.Denominator, container.VideoTimeBase.Numerator),
      DeclaredFrameCount = container.VideoFrameCount,
      CodecPrivateData = container.CodecPrivateData,
    };

    var streams = new List<MediaStreamInfo> { video };
    var nextIndex = 1;
    for (var t = 0; t < container.AudioTrackRates.Length; ++t) {
      var rate = container.AudioTrackRates[t];
      if ((rate & 1u << 30) == 0) // data-present bit; see SmackerReader for the bit layout
        continue;

      var sampleRate = (int)(rate & 0x00FFFFFF);
      var bytes = new byte[4];
      BinaryPrimitives.WriteUInt32LittleEndian(bytes, rate);

      streams.Add(new() {
        Index = nextIndex++,
        Kind = MediaStreamKind.Audio,
        Codec = _AUDIO_CODEC,
        TimeBase = sampleRate > 0 ? new(1, sampleRate) : Rational.Unknown,
        CodecPrivateData = bytes,
      });
    }

    return streams;
  }

  public static IEnumerable<CodedPacket> ReadPackets(SmackerContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    return SmackerReader.ReadPackets(container);
  }

  /// <summary>Nothing beyond the streams themselves. A Smacker file has no field for a title, an
  /// author or a creation date.</summary>
  public static VideoMetadata Metadata(SmackerContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    var streams = Streams(container);
    var declared = new MediaStreamMetadata[streams.Count];
    for (var i = 0; i < streams.Count; ++i)
      declared[i] = new(streams[i].Index, streams[i].Kind, streams[i].Codec);

    return new() { Streams = declared };
  }
}
