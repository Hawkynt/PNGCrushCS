namespace FileFormat.Core;

/// <summary>
/// Turns pictures into the coded packets of one stream. The third of the four things a video
/// pipeline is made of: encode.
/// </summary>
/// <remarks>
/// The mirror of <see cref="IVideoCodecDecoder{TSelf}"/>, and separate from it for the same reason
/// the decoder is separate from the demuxer: an encoder produces packets and has no idea which
/// container they will be written into, so one encoder serves every muxer.
/// <para/>
/// Declared here with the rest of the shape although nothing implements it yet. It is the half of
/// the seam that says what a muxer may be handed, and <see cref="DescribeStream"/> is the piece that
/// makes a transcode possible at all: the muxer needs a <see cref="MediaStreamInfo"/> to write its
/// stream headers from, and only the encoder knows what it is going to produce.
/// </remarks>
public interface IVideoCodecEncoder<TSelf> : IVideoPacketEncoder where TSelf : IVideoCodecEncoder<TSelf> {

  /// <summary>The codec's name as a person would say it.</summary>
  static abstract string CodecName { get; }

  /// <summary>The code a container should name this codec by in its stream headers.</summary>
  static abstract CodecTag Codec { get; }

  /// <summary>
  /// Builds an encoder producing the stream described.
  /// </summary>
  /// <remarks>
  /// Takes the same type the demuxer produces, so that "read this stream and write it again" is one
  /// value passed along rather than a translation between two descriptions of the same thing.
  /// </remarks>
  static abstract TSelf Create(MediaStreamInfo stream);
}
