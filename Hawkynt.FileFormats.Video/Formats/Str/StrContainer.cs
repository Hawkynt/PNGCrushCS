using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Str;

/// <summary>
/// A Sony PlayStation STR file (<c>.str</c>) — the console's own movie format, a raw run of CD-XA
/// sectors carrying MDEC video and XA-ADPCM audio, taken apart into the packets its per-sector
/// headers describe and nothing else.
/// </summary>
/// <remarks>
/// STR states no top-level header of its own the way RoQ, VMD or BFI do — there is no fixed
/// structure at the front of the file naming a picture size or a frame count, because a PlayStation
/// disc's own file system already carries the length and nothing here plays without the disc's
/// player already knowing what it is about to open. Every fact this container states about itself —
/// picture size, frame count, whether sound is present — is recovered by walking the sectors once
/// during <see cref="FromSpan"/>, the same way <see cref="FileFormat.Vmd.VmdContainer"/> counts its
/// records before handing any of them out. See <see cref="StrReader"/> for the sector walk and for
/// what a real sample's own RIFF/CDXA wrapper does and does not change about it.
/// <para/>
/// What a video packet's bytes mean — the per-block quantiser, the DC and AC Huffman tables, the
/// inverse DCT — is not this container's business and is not decoded here.
/// </remarks>
[FormatMagicBytes([0x43, 0x44, 0x58, 0x41], 8)] // "CDXA" at offset 8: a RIFF/CDXA-wrapped file
[FormatMagicBytes([0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00])] // a raw file's own first sector sync
[FormatMimeType("video/x-psx-str")]
public sealed class StrContainer : IVideoContainerReader<StrContainer> {

  /// <summary>The whole file, which every packet is a small reconstruction onto.</summary>
  public required ReadOnlyMemory<byte> Data { get; init; }

  /// <summary>
  /// Where the run of CD sectors begins: byte zero of a raw file, or the offset within a RIFF/CDXA
  /// wrapper where the first sector's own sync pattern was found.
  /// </summary>
  public required int SyncStart { get; init; }

  /// <summary>How many whole 2352-byte CD sectors follow <see cref="SyncStart"/>. A file whose last
  /// sector is cut short by truncation is walked up to the last whole one and no further.</summary>
  public required int SectorCount { get; init; }

  /// <summary>Picture width in pixels, read from the first video chunk's own header.</summary>
  public required int Width { get; init; }

  /// <summary>Picture height in pixels, read from the first video chunk's own header.</summary>
  public required int Height { get; init; }

  /// <summary>How many complete video frames the sector walk assembled.</summary>
  public required int VideoFrameCount { get; init; }

  /// <summary>Whether any Form 2 sector carrying the CD-XA audio bit was found.</summary>
  public required bool HasAudio { get; init; }

  /// <summary>How many XA-ADPCM audio sectors the walk found, whether or not <see cref="HasAudio"/>'s
  /// stream is reported — kept for a caller inspecting the container directly.</summary>
  public required int AudioPacketCount { get; init; }

  // -------- Format identity --------

  public static string PrimaryExtension => ".str";

  public static string[] FileExtensions => [".str"];

  public static bool? MatchesSignature(ReadOnlySpan<byte> header) => StrReader.LooksPlausible(header);

  // -------- Demux --------

  public static StrContainer FromSpan(ReadOnlySpan<byte> data) => StrReader.Open(data.ToArray());

  /// <summary>Opens a file over the caller's array, keeping it rather than copying it.</summary>
  public static StrContainer FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);

    return StrReader.Open(data);
  }

  public static StrContainer FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Sony PlayStation STR file not found.", file.FullName);

    return StrReader.Open(File.ReadAllBytes(file.FullName));
  }

  public static IReadOnlyList<MediaStreamInfo> Streams(StrContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    var video = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = StrReader.VideoCodec,
      Width = container.Width,
      Height = container.Height,
      // STR states no frame rate anywhere in a chunk header — every field of the thirty-two bytes is
      // accounted for by chunk bookkeeping, the picture size, the frame's own byte length and a
      // handful of decode-time values, and none of it is a rate. What the timestamps below count is
      // frame order, not seconds, and a caller wanting seconds has nothing this container states to
      // give it.
      TimeBase = Rational.Unknown,
      FrameRate = Rational.Unknown,
      DeclaredFrameCount = container.VideoFrameCount,
    };

    if (!container.HasAudio)
      return [video];

    var audio = new MediaStreamInfo {
      Index = 1,
      Kind = MediaStreamKind.Audio,
      Codec = StrReader.AudioCodec,
      TimeBase = Rational.Unknown,
    };

    return [video, audio];
  }

  public static IEnumerable<CodedPacket> ReadPackets(StrContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    return StrReader.ReadPackets(container);
  }

  /// <summary>Nothing beyond the streams themselves. STR carries no field for a title, an author or a
  /// creation date — a PlayStation disc's own file system is where that lived, if anywhere.</summary>
  public static VideoMetadata Metadata(StrContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    var streams = Streams(container);
    var declared = new MediaStreamMetadata[streams.Count];
    for (var i = 0; i < streams.Count; ++i)
      declared[i] = new(streams[i].Index, streams[i].Kind, streams[i].Codec);

    return new() { Streams = declared };
  }
}
