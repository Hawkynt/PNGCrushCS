using System;
using System.Collections.Generic;
using FileFormat.Core;

namespace Hawkynt.FileFormats.Video;

/// <summary>
/// Everything registered for a single container format. Produced by the source-generated
/// <c>VideoFormatRegistration.RegisterAll()</c> at startup; every operation is a typed delegate over
/// static interface dispatch, so nothing here is reached by reflection.
/// </summary>
/// <remarks>
/// The operations are all demux and none of them decode. A container entry can say which streams a
/// file holds and hand out its packets; turning a packet into a picture needs a codec, which is
/// looked up separately in <see cref="VideoFormatRegistry.CreateDecoder"/>. Keeping the two lookups
/// apart in the registry is the same separation the interfaces have, carried through to the place a
/// caller actually reaches them.
/// </remarks>
/// <param name="Format">The generated identity of this container.</param>
/// <param name="Name">Its name as text.</param>
/// <param name="PrimaryExtension">The canonical extension, e.g. ".avi".</param>
/// <param name="AllExtensions">Every extension this container claims.</param>
/// <param name="MimeTypes">Every media type this container claims.</param>
/// <param name="MagicSignatures">Compile-time signatures from <c>[FormatMagicBytes]</c>.</param>
/// <param name="MatchesSignature">The container's own opinion on a header, where it has one. Over a
/// memory rather than an array because detection is handed whole files: copying one out to ask
/// whether its first twelve bytes say <c>AVI </c> would copy the film to answer a question about its
/// head.</param>
/// <param name="DetectionPriority">Lower is tried first.</param>
/// <param name="ReadStreams">The streams the container declares.</param>
/// <param name="ReadPackets">Every packet, lazily.</param>
/// <param name="ReadStreamPackets">The packets of one stream, lazily.</param>
/// <param name="ReadMetadata">What the container says about itself.</param>
public sealed record VideoFormatEntry(
  VideoFormat Format,
  string Name,
  string PrimaryExtension,
  string[] AllExtensions,
  string[] MimeTypes,
  MagicSignature[] MagicSignatures,
  Func<ReadOnlyMemory<byte>, bool?>? MatchesSignature,
  int DetectionPriority,
  Func<byte[], IReadOnlyList<MediaStreamInfo>> ReadStreams,
  Func<byte[], IEnumerable<CodedPacket>> ReadPackets,
  Func<byte[], int, IEnumerable<CodedPacket>> ReadStreamPackets,
  Func<byte[], VideoMetadata> ReadMetadata) {

  /// <summary>The first/preferred media type, or <c>"application/octet-stream"</c> if none is registered.</summary>
  public string PrimaryMimeType => this.MimeTypes.Length > 0 ? this.MimeTypes[0] : "application/octet-stream";
}

/// <summary>
/// One registered codec: what it is called, which streams it takes, and how to build a decoder for
/// one of them.
/// </summary>
/// <remarks>
/// <see cref="CreateDecoder"/> hands back the non-generic <see cref="IVideoFrameDecoder"/> because
/// which codec a stream needs is known only once the stream has been read. The delegate itself is
/// closed over a generated call to the codec's own static factory, so the choice costs a dictionary
/// walk and not a reflection lookup.
/// </remarks>
/// <param name="CodecName">The codec's name as a person would say it.</param>
/// <param name="Accepts">Whether this codec is the one a stream is coded with, judged by its tag.</param>
/// <param name="CreateDecoder">Builds a decoder for one stream; throws
/// <see cref="NotSupportedException"/> for a stream this codec names but cannot decode.</param>
public sealed record VideoCodecEntry(
  string CodecName,
  Func<MediaStreamInfo, bool> Accepts,
  Func<MediaStreamInfo, IVideoFrameDecoder> CreateDecoder);

/// <summary>
/// One registered encoder: what it is called, the code it writes, and how to build an encoder that
/// produces that codec's packets.
/// </summary>
/// <remarks>
/// The mirror of <see cref="VideoCodecEntry"/> and a separate table from it, because the two answer
/// different questions. A decoder is chosen by what a stream <i>says it is</i>, so it is asked
/// whether it accepts a whole stream description; an encoder is chosen by what a caller <i>wants
/// written</i>, which is one four-character code. A codec with both has a row in each table under
/// the same <see cref="CodecName"/>, and that shared name is what joins them.
/// <para/>
/// <see cref="Codec"/> is the one code the encoder writes, not every code it would answer to when
/// reading. HuffYUV writes <c>HFYU</c> and FFVHUFF <c>FFVH</c>, and the same encoder produces either
/// on request; the table names the one it writes when nothing is asked for.
/// </remarks>
/// <param name="CodecName">The codec's name as a person would say it, spelt exactly as the decoder
/// of the same codec spells it.</param>
/// <param name="Codec">The code a container names this codec by in its stream headers.</param>
/// <param name="CreateEncoder">Builds an encoder producing the stream described; throws
/// <see cref="NotSupportedException"/> for a stream this codec cannot be asked to write.</param>
public sealed record VideoCodecEncoderEntry(
  string CodecName,
  CodecTag Codec,
  Func<MediaStreamInfo, IVideoPacketEncoder> CreateEncoder);
