namespace FileFormat.Core;

/// <summary>
/// Turns the coded packets of one stream into pictures. The second of the four things a video
/// pipeline is made of: decode.
/// </summary>
/// <remarks>
/// A decoder is created for one stream, from that stream's <see cref="MediaStreamInfo"/>, and never
/// learns which container the packets came out of. The same Motion JPEG decoder therefore serves an
/// AVI, a raw <c>.mjpg</c> and whatever container is added next, without any of them knowing it
/// exists.
/// <para/>
/// Decoding is an instance affair while identity is static. The identity — which tags this codec
/// answers to — has to be answerable before a decoder exists, so a caller can ask "is there anything
/// that reads this stream" without building one. The decoding is stateful, because most codecs are:
/// a predicted frame means nothing without the frames before it, so the state has to live somewhere,
/// and one decoder per stream is where.
/// <para/>
/// <see cref="TryDecode"/> returns a picture only sometimes on purpose. A packet may be a fragment of
/// a frame, or a frame that cannot be shown until a later one has been decoded; either way the
/// answer for that packet is "not yet", not a picture invented to have something to return.
/// </remarks>
public interface IVideoCodecDecoder<TSelf> : IVideoFrameDecoder where TSelf : IVideoCodecDecoder<TSelf> {

  /// <summary>The codec's name as a person would say it, for messages and for listings.</summary>
  static abstract string CodecName { get; }

  /// <summary>
  /// Whether this codec is the one that stream is coded with, judged by its tag alone.
  /// </summary>
  /// <remarks>
  /// By the tag and not by trying: a decoder that answered by attempting a decode would have to be
  /// built and fed a packet before a caller could learn it was the wrong one, and a wrong decoder
  /// fed a packet returns noise rather than an error often enough that the attempt proves nothing.
  /// </remarks>
  static abstract bool Accepts(MediaStreamInfo stream);

  /// <summary>
  /// Builds a decoder for one stream.
  /// </summary>
  /// <remarks>
  /// Throws <see cref="System.NotSupportedException"/> when the stream is one this codec names but
  /// cannot decode — an uncompressed stream at a depth no bitmap is stored at, say. That refusal
  /// belongs here rather than in <see cref="Accepts"/>, so the message can say what was wrong with
  /// this particular stream instead of the caller being told only that nothing matched.
  /// </remarks>
  static abstract TSelf Create(MediaStreamInfo stream);
}
