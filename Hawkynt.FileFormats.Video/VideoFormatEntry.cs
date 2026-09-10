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

  /// <summary>
  /// Tools from outside this repository that have read a file this container's muxer wrote.
  /// </summary>
  /// <remarks>
  /// Declared by <see cref="VerifiedByAttribute"/> on the writer type and carried here by the
  /// registry generator. Empty means nothing but this package's own demuxer has ever opened what
  /// the muxer produces, which for the containers here is the ordinary answer.
  /// </remarks>
  public ConformanceOracle[] VerifiedBy { get; init; } = [];
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
/// One registered encoder: what it is called, the code it writes by default, which requested codes
/// it can produce, and how to build an encoder that produces those packets.
/// </summary>
/// <remarks>
/// The mirror of <see cref="VideoCodecEntry"/> and a separate table from it, because the two answer
/// different questions. A decoder is chosen by what a stream <i>says it is</i>, and an encoder by what
/// a caller <i>wants written</i>; both therefore carry their own acceptance predicate. A codec with
/// both has a row in each table under the same <see cref="CodecName"/>, and that shared name is what
/// joins them.
/// <para/>
/// <see cref="Codec"/> remains the preferred/default code for display and for encoders that have only
/// one spelling. <see cref="Accepts"/> is what decides lookup, so an encoder whose identical bitstream
/// is carried under aliases can advertise those without multiplying registry rows.
/// </remarks>
/// <param name="CodecName">The codec's name as a person would say it, spelt exactly as the decoder
/// of the same codec spells it.</param>
/// <param name="Codec">The preferred/default code a container names this codec by.</param>
/// <param name="Accepts">Whether this encoder can produce the code the stream asks to have written.</param>
/// <param name="CreateEncoder">Builds an encoder producing the stream described; throws
/// <see cref="NotSupportedException"/> for a stream this codec cannot be asked to write.</param>
public sealed record VideoCodecEncoderEntry(
  string CodecName,
  CodecTag Codec,
  Func<MediaStreamInfo, bool> Accepts,
  Func<MediaStreamInfo, IVideoPacketEncoder> CreateEncoder) {

  /// <summary>
  /// Tools from outside this repository that have read what this encoder produces.
  /// </summary>
  /// <remarks>
  /// This package's own decoder is not one of them and never can be. An encoder and a decoder
  /// written from the same reading of a format agree with each other whether or not that reading is
  /// right, and the codecs here are mostly ones whose description had to be recovered by
  /// measurement — exactly the case where a shared mistake is likeliest. Empty is the honest answer
  /// wherever nothing else has looked.
  /// </remarks>
  public ConformanceOracle[] VerifiedBy { get; init; } = [];
}
