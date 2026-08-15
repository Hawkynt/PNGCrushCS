using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Avi;

/// <summary>
/// An AVI taken apart into the streams it declares and the packets it holds — and nothing else.
/// </summary>
/// <remarks>
/// This container knows where every packet is and not one thing about what is inside any of them. It
/// does not decode, it does not report pictures, and it does not refuse a file for naming a codec
/// nothing here reads: a container full of H.264 is a perfectly good AVI, and copying its packets
/// into another container needs no decoder at all. Refusal by four-character code still happens, at
/// the moment a decoder is asked for — see <see cref="FileFormat.Codecs.RawVideoDecoder"/> and
/// <see cref="FileFormat.Codecs.MotionJpegDecoder"/> for the two codes that are taken.
/// <para/>
/// It reads the stream format chunk as a <c>BITMAPINFOHEADER</c>, which looks like knowledge of a
/// codec and is not: the AVI specification says that is what a video stream's <c>strf</c> is,
/// whatever the frames themselves turn out to be. What that header describes beyond width, height
/// and depth is the codec's business, so the chunk is also carried across verbatim as
/// <see cref="MediaStreamInfo.CodecPrivateData"/> for whichever decoder ends up wanting it.
/// <para/>
/// The predecessor of this type decoded as it demuxed — an <c>AviFile</c> holding a list of frame
/// byte arrays, turning index <c>n</c> into a picture through the JPEG or the bitmap reader. That is
/// what made transcoding impossible: the only thing it could ever hand over was pixels.
/// </remarks>
[FormatMimeType("video/avi", "video/msvideo", "video/x-msvideo")]
public sealed class AviContainer : IVideoContainerReader<AviContainer> {

  private const string _RECORD_LIST = "rec ";

  /// <summary>The <c>avih</c> header of the file.</summary>
  public required AviMainHeader Header { get; init; }

  /// <summary>Every stream the file declares, in declaration order.</summary>
  public required IReadOnlyList<MediaStreamInfo> StreamInfos { get; init; }

  /// <summary>What the file says about itself.</summary>
  public required VideoMetadata FileMetadata { get; init; }

  /// <summary>The body of the <c>LIST movi</c>, as a window onto the file rather than a copy.</summary>
  public required ReadOnlyMemory<byte> MovieList { get; init; }

  // -------- Format identity --------

  public static string PrimaryExtension => ".avi";

  public static string[] FileExtensions => [".avi"];

  /// <summary>
  /// A RIFF file of form <c>AVI </c>. The form type is checked as well as the signature because
  /// WAVE, ANI and WebP are all RIFF too, and the first four bytes alone do not tell them apart.
  /// </summary>
  public static bool? MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 12
       && header[0] == (byte)'R' && header[1] == (byte)'I' && header[2] == (byte)'F' && header[3] == (byte)'F'
       && header[8] == (byte)'A' && header[9] == (byte)'V' && header[10] == (byte)'I' && header[11] == (byte)' '
      ? true
      : null;

  // -------- Demux --------

  public static AviContainer FromSpan(ReadOnlySpan<byte> data) => AviReader.FromSpan(data);

  /// <summary>Opens an AVI over the caller's array, keeping it rather than copying it.</summary>
  public static AviContainer FromBytes(byte[] data) => AviReader.FromBytes(data);

  public static AviContainer FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("AVI file not found.", file.FullName);

    return AviReader.FromBytes(File.ReadAllBytes(file.FullName));
  }

  /// <summary>Every stream the container declares — sound and text as well as pictures.</summary>
  public static IReadOnlyList<MediaStreamInfo> Streams(AviContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    return container.StreamInfos;
  }

  /// <summary>What the container says about itself.</summary>
  public static VideoMetadata Metadata(AviContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    return container.FileMetadata;
  }

  /// <summary>Walks every packet of the file, in the order it stores them.</summary>
  /// <remarks>
  /// Lazy and re-runnable: nothing of <c>movi</c> is touched until a packet is asked for, and each
  /// packet's data is a window onto the buffer the file was read into rather than a copy of it. A
  /// two-hour recording enumerated for its first frame costs one frame.
  /// </remarks>
  public static IEnumerable<CodedPacket> ReadPackets(AviContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    return _Walk(container, null);
  }

  /// <summary>Walks the packets of one stream, in storage order.</summary>
  public static IEnumerable<CodedPacket> ReadPackets(AviContainer container, int streamIndex) {
    ArgumentNullException.ThrowIfNull(container);

    return _Walk(container, streamIndex);
  }

  private static IEnumerable<CodedPacket> _Walk(AviContainer container, int? onlyStream) {
    // One counter per stream: an AVI does not store timestamps, it implies them from position, and a
    // stream's time base says what one position is worth. The counters have to advance for every
    // stream even when only one is wanted, or a filtered walk would number that stream differently
    // from an unfiltered one.
    var ordinals = new long[container.StreamInfos.Count];

    foreach (var element in RiffScanner.Walk(container.MovieList, 0, container.MovieList.Length)) {
      // An interleaved file wraps each group of chunks in a 'rec ' list instead of putting them
      // straight in movi. Walking into it keeps the packets in the order the file stores them, which
      // is the order they are due in.
      if (element.IsList) {
        if (element.ListType.ToString() != _RECORD_LIST)
          continue;

        foreach (var record in RiffScanner.Walk(element))
          if (_TryPacket(container, record, ordinals, onlyStream, out var recorded))
            yield return recorded;

        continue;
      }

      if (_TryPacket(container, element, ordinals, onlyStream, out var packet))
        yield return packet;
    }
  }

  private static bool _TryPacket(
    AviContainer container, RiffElement element, long[] ordinals, int? onlyStream, out CodedPacket packet) {
    packet = default;

    var id = element.Id.ToString();
    if (id.Length != 4 || !char.IsAsciiDigit(id[0]) || !char.IsAsciiDigit(id[1]))
      return false;

    // The two letters say what the chunk is. 'db' and 'dc' are frames, 'wb' is sound, 'tx' is text;
    // 'pc' is a palette change, which modifies a stream rather than being a unit of it, and 'ix' is
    // an index rather than data. Neither of the last two is a packet.
    if (id.Substring(2) is not ("db" or "dc" or "wb" or "tx"))
      return false;

    var streamIndex = (id[0] - '0') * 10 + (id[1] - '0');
    if ((uint)streamIndex >= (uint)ordinals.Length)
      return false;

    // A zero-length chunk carries nothing, and ffmpeg does not invent a frame for it: an AVI of four
    // '00dc' chunks one of which is empty is reported by `ffprobe -count_frames` as three frames.
    // Counting it here would make our frame count disagree with the oracle's.
    if (element.Body.Length == 0)
      return false;

    var ordinal = ordinals[streamIndex]++;
    if (onlyStream != null && streamIndex != onlyStream)
      return false;

    // A video stream's dwScale/dwRate count frames, so a chunk's position is its timestamp. A sound
    // stream's chunks hold a variable number of samples, so position says nothing about when one is
    // due — and a timestamp that is wrong is worse than one that is absent.
    var isVideo = container.StreamInfos[streamIndex].Kind == MediaStreamKind.Video;

    packet = new(streamIndex, element.Body, isVideo ? ordinal : null, isVideo ? ordinal : null);
    return true;
  }
}
