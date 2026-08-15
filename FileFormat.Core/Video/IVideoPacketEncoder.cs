using System.Collections.Generic;

namespace FileFormat.Core;

/// <summary>
/// An encoder already bound to one stream: pictures go in, packets come out.
/// </summary>
/// <remarks>
/// The instance half of <see cref="IVideoCodecEncoder{TSelf}"/>, split from it for the same reason
/// <see cref="IVideoFrameDecoder"/> is split from the decoder — so a chosen codec can be held and
/// driven without its type being named at the call site, and still without reflection.
/// </remarks>
public interface IVideoPacketEncoder {

  /// <summary>Offers one picture to the encoder and takes the packet that falls out of it, if one does.</summary>
  bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet);

  /// <summary>Takes the packets the encoder is still holding once the pictures have run out.</summary>
  IEnumerable<CodedPacket> Flush() => [];

  /// <summary>
  /// The stream this encoder is producing, as a muxer needs it described — including the codec
  /// private data the container must carry for the result to be decodable again.
  /// </summary>
  MediaStreamInfo DescribeStream();
}
