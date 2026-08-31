using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Vqa;

/// <summary>
/// A Westwood VQA file (<c>.vqa</c>) taken apart into its header fields and the chunks it holds — and
/// nothing else.
/// </summary>
/// <remarks>
/// Like RoQ, Interplay MVE and id Cinematic, this is its own container: nothing wraps it, because a
/// file only ever holds this one video codec and Westwood's own ADPCM audio. Unlike any of those three,
/// its chunk layout is RIFF's own — a four-character ID and a size on every chunk, <c>FORM</c>
/// wrapping the rest — except that the size is big-endian where RIFF's own chunks are little-endian.
/// See <see cref="VqaReader"/> for the chunk walk that answers only where a picture's bytes are; what a
/// codebook entry or an index byte means is <see cref="Codecs.VqaVideoDecoder"/>'s alone.
/// <para/>
/// <b>Only the 256-colour, version-2 form is decoded.</b> The header states a version — <c>1</c> for
/// the format's original use in Legend of Kyrandia III, <c>2</c> for the far more common form behind
/// Command &amp; Conquer, Red Alert and most of Westwood's later catalogue — and a flag byte that marks
/// a separate fifteen-bit-colour form. Version 1's index table decodes to values with no structure this
/// reader's own two real version-1 samples could find a fit for: the two-value split-table reading
/// version 2 files decode exactly under is not it, and nothing else this reader was built against states
/// what version 1 uses instead. Version 1 and the high-colour form are therefore left to the codec to
/// refuse by name, the same way a container never itself refuses a codec it has not decoded.
/// </remarks>
[FormatMimeType("video/x-vqa")]
public sealed class VqaContainer : IVideoContainerReader<VqaContainer> {
  /// <summary>Initializes a new instance of this type.</summary>
  public VqaContainer() { }

  /// <summary>The whole file, which every packet is a window onto.</summary>
  public required ReadOnlyMemory<byte> Data { get; init; }

  /// <summary>Picture width in pixels.</summary>
  public required int Width { get; init; }

  /// <summary>Picture height in pixels.</summary>
  public required int Height { get; init; }

  /// <summary>A codebook entry's width in pixels — four in every sample this was measured against.</summary>
  public required int BlockWidth { get; init; }

  /// <summary>A codebook entry's height in pixels — two in every sample this was measured against.</summary>
  public required int BlockHeight { get; init; }

  /// <summary>How many <c>VQFR</c> chunks the header states the file holds.</summary>
  public required int VideoFrameCount { get; init; }

  /// <summary>The audio sample rate the header states, in hertz, or zero for a file with no sound.</summary>
  public required int AudioSampleRate { get; init; }

  /// <summary>Audio channel count, or zero where <see cref="AudioSampleRate"/> is zero.</summary>
  public required int AudioChannels { get; init; }

  /// <summary>The forty-two-byte <c>VQHD</c> payload verbatim, carried through as the video stream's
  /// private data so the codec can read the version and high-colour flag this container does not
  /// interpret.</summary>
  public required ReadOnlyMemory<byte> HeaderPayload { get; init; }

  private static readonly Rational _VIDEO_TIME_BASE = new(1, 15);
  private static readonly CodecTag _VIDEO_CODEC = CodecTag.FromCharacters("WSVQ");
  private static readonly CodecTag _AUDIO_CODEC = CodecTag.FromCharacters("WSAD");

  // -------- Format identity --------

  /// <summary>Gets the primary file extension for this format.</summary>
  public static string PrimaryExtension => ".vqa";

  /// <summary>Gets the file extensions supported by this format.</summary>
  public static string[] FileExtensions => [".vqa"];

  /// <summary>
  /// <c>FORM</c> alone is IFF's own signature, shared by AIFF and other unrelated formats built on the
  /// same envelope — the four bytes right after <c>FORM</c>'s own size that name the form's type,
  /// <c>WVQA</c>, are what actually says "this is a Westwood VQA file".
  /// </summary>
  public static bool? MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 12 && header[..4].SequenceEqual("FORM"u8) && header.Slice(8, 4).SequenceEqual("WVQA"u8)
      ? true
      : null;

  // -------- Demux --------

  /// <summary>Reads an instance from the specified byte span.</summary>
  public static VqaContainer FromSpan(ReadOnlySpan<byte> data) => VqaReader.Open(data.ToArray());

  /// <summary>Opens a file over the caller's array, keeping it rather than copying it.</summary>
  public static VqaContainer FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);

    return VqaReader.Open(data);
  }

  /// <summary>Reads an instance from the specified file.</summary>
  public static VqaContainer FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Westwood VQA file not found.", file.FullName);

    return VqaReader.Open(File.ReadAllBytes(file.FullName));
  }

  /// <summary>Gets the media streams declared by the specified container.</summary>
  public static IReadOnlyList<MediaStreamInfo> Streams(VqaContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    var video = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = _VIDEO_CODEC,
      Width = container.Width,
      Height = container.Height,
      TimeBase = _VIDEO_TIME_BASE,
      FrameRate = new(15, 1),
      DeclaredFrameCount = container.VideoFrameCount,
      CodecPrivateData = container.HeaderPayload,
    };

    if (container.AudioSampleRate == 0 || container.AudioChannels == 0)
      return [video];

    var audio = new MediaStreamInfo {
      Index = 1,
      Kind = MediaStreamKind.Audio,
      Codec = _AUDIO_CODEC,
      TimeBase = new(1, container.AudioSampleRate),
    };

    return [video, audio];
  }

  /// <summary>Enumerates coded packets from the specified container.</summary>
  public static IEnumerable<CodedPacket> ReadPackets(VqaContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    return VqaReader.ReadPackets(container);
  }

  /// <summary>Nothing beyond the streams themselves. A VQA file has no field for a title, an author or
  /// a creation date.</summary>
  public static VideoMetadata Metadata(VqaContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    var streams = Streams(container);
    var declared = new MediaStreamMetadata[streams.Count];
    for (var i = 0; i < streams.Count; ++i)
      declared[i] = new(streams[i].Index, streams[i].Kind, streams[i].Codec);

    return new() { Streams = declared };
  }
}
