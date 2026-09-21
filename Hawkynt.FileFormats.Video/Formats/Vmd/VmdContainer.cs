using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Vmd;

/// <summary>A classic Sierra VMD file split into its streams, block table and fixed per-block part table.</summary>
/// <remarks>
/// VMD's table of contents is a two-level index. The header states a block count and a fixed number of
/// sixteen-byte part records per block. Each six-byte block record gives the absolute data offset for
/// that block; the part records following the block table give each piece's type and byte length.
/// Consequently data offsets restart from each block's own absolute offset rather than being inferred
/// by summing every record from byte 816. The reader retains both mappings so gaps between blocks and
/// blocks containing non-audio/video parts do not move a later packet onto the wrong bytes.
/// <para/>
/// Older revisions of this repository wrote the fixed-parts-per-block header field as zero. The reader
/// keeps a narrow compatibility path for those files, deriving the old flat table exactly as that
/// writer did. Newly written files always use the real fixed-stride table.
/// </remarks>
[FormatMimeType("video/x-vmd")]
public sealed class VmdContainer : IVideoContainerReader<VmdContainer> {

  public required ReadOnlyMemory<byte> Data { get; init; }
  public required int Width { get; init; }
  public required int Height { get; init; }
  public required int VideoFrameCount { get; init; }
  public required bool HasAudio { get; init; }
  public required int AudioSampleRate { get; init; }
  public required int AudioFrameLength { get; init; }
  public required int CodecVersion { get; init; }
  public required bool IsIndeo3 { get; init; }
  public required ReadOnlyMemory<byte> HeaderPayload { get; init; }
  public required uint TocOffset { get; init; }
  public required int NumBlocks { get; init; }
  public required int FramesPerBlock { get; init; }
  public required IReadOnlyList<int> BlockOffsets { get; init; }
  public required int FrameCount { get; init; }
  public required int FrameTableStart { get; init; }
  public required IReadOnlyList<int> FrameDataOffsets { get; init; }
  public required IReadOnlyList<int> FrameBlockIndices { get; init; }
  public required uint MultimediaDataOffset { get; init; }

  private static readonly CodecTag _VIDEO_CODEC = CodecTag.FromCharacters("VMDV");
  private static readonly CodecTag _AUDIO_CODEC = CodecTag.FromCharacters("VMDA");

  public static string PrimaryExtension => ".vmd";
  public static string[] FileExtensions => [".vmd"];

  public static bool? MatchesSignature(ReadOnlySpan<byte> header) => VmdReader.LooksPlausible(header) ? true : null;

  public static VmdContainer FromSpan(ReadOnlySpan<byte> data) => VmdReader.Open(data.ToArray());

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

    var codec = container.IsIndeo3
      ? new CodecTag(BinaryPrimitives.ReadUInt32LittleEndian(container.HeaderPayload.Span[24..]))
      : _VIDEO_CODEC;

    var video = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = codec,
      Handler = codec,
      Width = container.Width,
      Height = container.Height,
      BitsPerPixel = container.CodecVersion switch {
        1 => 8,
        5 => 24,
        13 => 16,
        _ => 0,
      },
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

    var flags = BinaryPrimitives.ReadUInt16LittleEndian(container.HeaderPayload.Span[810..]);
    var channels = (flags & 0x8000) != 0 || (flags & 0x0200) != 0 ? 2 : 1;
    var audioLengthRaw = BinaryPrimitives.ReadInt16LittleEndian(container.HeaderPayload.Span[806..]);
    var bitsPerSample = audioLengthRaw < 0 ? 16 : 8;

    var audio = new MediaStreamInfo {
      Index = 1,
      Kind = MediaStreamKind.Audio,
      Codec = _AUDIO_CODEC,
      TimeBase = new Rational(1, container.AudioSampleRate),
      SampleRate = container.AudioSampleRate,
      Channels = channels,
      BitsPerSample = bitsPerSample,
    };

    return [video, audio];
  }

  public static IEnumerable<CodedPacket> ReadPackets(VmdContainer container) {
    ArgumentNullException.ThrowIfNull(container);
    return VmdReader.ReadPackets(container);
  }

  public static VideoMetadata Metadata(VmdContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    var streams = Streams(container);
    var declared = new MediaStreamMetadata[streams.Count];
    for (var i = 0; i < streams.Count; ++i)
      declared[i] = new(streams[i].Index, streams[i].Kind, streams[i].Codec);

    return new() { Streams = declared };
  }
}
