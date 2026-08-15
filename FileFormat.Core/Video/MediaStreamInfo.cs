using System;

namespace FileFormat.Core;

/// <summary>
/// Everything a demuxer can say about one stream of a container without decoding any of it.
/// </summary>
/// <remarks>
/// This is the whole of the contract between demuxing and decoding. A container fills it in from its
/// headers; a decoder reads it to decide whether it takes this stream and how to set itself up. The
/// container never learns what the codec does with it, and the codec never learns which container it
/// came out of — which is the seam that lets one container be read and another written.
/// </remarks>
public sealed class MediaStreamInfo {

  /// <summary>The stream's position in the container, counted across every stream it holds.</summary>
  public required int Index { get; init; }

  /// <summary>What this stream carries.</summary>
  public required MediaStreamKind Kind { get; init; }

  /// <summary>The code naming the codec the packets of this stream are coded with.</summary>
  public CodecTag Codec { get; init; } = CodecTag.None;

  /// <summary>
  /// The code naming the decoder the writer expected to be used, where the container has such a
  /// field of its own.
  /// </summary>
  /// <remarks>
  /// Not what decides the codec — an AVI's stream handler is four zero bytes as often as it is
  /// <c>DIB </c> for the same <c>rawvideo</c> stream, and ffmpeg decides on the stream format's
  /// code instead. Kept because a refusal reads better naming both.
  /// </remarks>
  public CodecTag Handler { get; init; } = CodecTag.None;

  /// <summary>
  /// The seconds one unit of this stream's timestamps stands for, as an exact ratio.
  /// </summary>
  public Rational TimeBase { get; init; } = Rational.Unknown;

  /// <summary>The frames a second the writer stated, as an exact ratio, or unknown.</summary>
  public Rational FrameRate { get; init; } = Rational.Unknown;

  /// <summary>The number of frames the container's header claims, which a file left unfinished may
  /// state wrongly — hence a claim rather than a count.</summary>
  public long? DeclaredFrameCount { get; init; }

  /// <summary>Picture width in pixels, or zero for a stream that has none.</summary>
  public int Width { get; init; }

  /// <summary>Picture height in pixels, always positive; how the rows run is the codec's business.</summary>
  public int Height { get; init; }

  /// <summary>Bits per pixel as the container stated it, or zero when it did not.</summary>
  public int BitsPerPixel { get; init; }

  /// <summary>
  /// The codec's own description of the stream, verbatim — an AVI's <c>strf</c>, an MP4 sample
  /// entry's codec configuration, a Matroska track's private data.
  /// </summary>
  /// <remarks>
  /// Handed across as bytes on purpose. What is in here is defined by the codec and not by the
  /// container, so a container that parsed it would be doing the codec's work, and a codec added
  /// later would need every container reopened to describe it. The demuxer's only job is to find the
  /// bytes and say which stream they belong to.
  /// </remarks>
  public ReadOnlyMemory<byte> CodecPrivateData { get; init; } = ReadOnlyMemory<byte>.Empty;

  /// <summary>The RFC 5646 language tag of this stream, where the container states one.</summary>
  public string? Language { get; init; }

  /// <summary>The name the writer gave this stream, where the container has a field for one.</summary>
  public string? Name { get; init; }
}
