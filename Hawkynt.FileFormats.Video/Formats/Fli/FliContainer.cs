using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.FlicVideo;

/// <summary>
/// An Autodesk FLIC file (<c>.fli</c>, <c>.flc</c>, an eight-bit <c>.flx</c>) taken apart into the
/// one stream it declares and the frame chunks it holds — and nothing else.
/// </summary>
/// <remarks>
/// FLIC is unusual among the formats this package reads because it is a container and a codec fused
/// into one file: there is no wrapper the way an AVI wraps Microsoft RLE, and no elementary stream the
/// way a raw <c>.mjpg</c> is one. What makes it honest to split into the same four contracts as every
/// other format here is that "where the packets are" and "what is in them" are still two different
/// questions with two different answers, even for a file that answers both from the same 128-byte
/// header. This type answers only the first: it reads the header, decides where frame one begins —
/// trusting <c>oframe1</c> where the header states one, which matters for at least one real file whose
/// header and first frame are separated by an undocumented prefix chunk — and hands out each
/// <c>FRAME_TYPE</c> chunk's sub-chunks as one packet apiece. <see cref="FileFormat.Codecs.FlicVideoDecoder"/>
/// is the only thing here that reads a palette packet or a delta opcode.
/// <para/>
/// The one piece of container-shaped judgement this makes about a packet's contents, rather than
/// leaving to the codec, is <see cref="CodedPacket.IsKeyFrame"/> — whether a frame carries a
/// whole-frame picture chunk (<c>BLACK</c>, <c>BRUN</c> or <c>COPY</c>) rather than only a delta or a
/// palette update. That is a structural fact about which sub-chunk types are present, the same kind of
/// fact an MP4 sample flag or an ASF key-frame bit states directly, and reading it here is a chunk-type
/// scan rather than a decode of any one of them.
/// <para/>
/// The last <c>FRAME_TYPE</c> chunk of a <c>.fli</c> file is not a picture of the film. It is the ring
/// frame: a delta back to frame one, written only so a player can loop without paying to re-decode the
/// run-length-coded first frame — see <see cref="FliReader"/> for the corpus evidence that every clean
/// file carries exactly one more frame chunk than its header's <c>frames</c> field states. This reader
/// stops at exactly that count, so the ring frame is never handed out as an ordinary extra packet.
/// </remarks>
[FormatMimeType("video/x-flic", "video/fli", "video/flc")]
[FormatMagicBytes([0x11, 0xAF], 4)]
[FormatMagicBytes([0x12, 0xAF], 4)]
public sealed class FliContainer : IVideoContainerReader<FliContainer> {

  /// <summary>The whole file, which every packet is a window onto.</summary>
  public required ReadOnlyMemory<byte> Data { get; init; }

  /// <summary>The header's magic — <c>0xAF11</c> for <c>.fli</c>, <c>0xAF12</c> for <c>.flc</c>/<c>.flx</c>.</summary>
  public required ushort Magic { get; init; }

  /// <summary>Picture width in pixels, as the header states it.</summary>
  public required int Width { get; init; }

  /// <summary>Picture height in pixels, as the header states it.</summary>
  public required int Height { get; init; }

  /// <summary>
  /// The number of frames the header declares, which excludes the ring frame. See
  /// <see cref="FliReader"/> for why that exclusion is deliberate rather than an oversight.
  /// </summary>
  public required ushort FrameCount { get; init; }

  /// <summary>
  /// The header's own delay between frames, in the stream's time base — 1/70-second ticks for
  /// <c>.fli</c>, milliseconds for <c>.flc</c>.
  /// </summary>
  public required uint Speed { get; init; }

  /// <summary>Where the first <c>FRAME_TYPE</c> chunk begins.</summary>
  public required int FirstFrameOffset { get; init; }

  // -------- Format identity --------

  public static string PrimaryExtension => ".fli";

  public static string[] FileExtensions => [".fli", ".flc", ".flx"];

  // -------- Demux --------

  public static FliContainer FromSpan(ReadOnlySpan<byte> data) => FliReader.Open(data.ToArray());

  /// <summary>Opens a file over the caller's array, keeping it rather than copying it.</summary>
  public static FliContainer FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);

    return FliReader.Open(data);
  }

  public static FliContainer FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("FLIC file not found.", file.FullName);

    return FliReader.Open(File.ReadAllBytes(file.FullName));
  }

  /// <summary>The one stream a FLIC file holds — it carries no sound and no second picture stream.</summary>
  public static IReadOnlyList<MediaStreamInfo> Streams(FliContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    return [_StreamInfo(container)];
  }

  private static MediaStreamInfo _StreamInfo(FliContainer container) {
    var timeBase = container.Magic == FliReader.MAGIC_FLI ? new Rational(1, 70) : new Rational(1, 1000);
    var frameRate = container.Speed > 0 ? new Rational(timeBase.Denominator, container.Speed) : Rational.Unknown;

    return new() {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("FLIC"),
      Width = container.Width,
      Height = container.Height,
      BitsPerPixel = 8,
      TimeBase = timeBase,
      FrameRate = frameRate,
      DeclaredFrameCount = container.FrameCount,
    };
  }

  /// <summary>Walks the film's frames, one packet a frame, stopping before the ring frame.</summary>
  public static IEnumerable<CodedPacket> ReadPackets(FliContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    return FliReader.Split(container);
  }

  /// <summary>The file has one stream, so anything but index zero walks nothing.</summary>
  public static IEnumerable<CodedPacket> ReadPackets(FliContainer container, int streamIndex)
    => streamIndex == 0 ? ReadPackets(container) : [];

  /// <summary>
  /// Nothing beyond the one stream. A FLIC header has no field for a title, an author or a creation
  /// date — <c>created</c>/<c>creator</c>/<c>updated</c>/<c>updater</c> exist but name a serial number
  /// and an MS-DOS timestamp with no text anywhere near them, not a work's metadata.
  /// </summary>
  public static VideoMetadata Metadata(FliContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    var stream = _StreamInfo(container);
    return new() { Streams = [new(0, MediaStreamKind.Video, stream.Codec)] };
  }
}
