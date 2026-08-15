using System.Collections.Generic;

namespace FileFormat.Core;

/// <summary>
/// A decoder already bound to one stream: packets go in, pictures come out.
/// </summary>
/// <remarks>
/// The instance half of <see cref="IVideoCodecDecoder{TSelf}"/>, split off from it so that a decoder
/// can be held without naming its type. A registry chooses a codec at run time from a stream's tag,
/// and what it hands back cannot be a generic type parameter — but it must still not be reached by
/// reflection. Two interfaces solve that: the generic one carries the identity and the factory, both
/// static and resolved at compile time; this one carries the work.
/// </remarks>
public interface IVideoFrameDecoder {

  /// <summary>
  /// Offers one packet to the decoder and takes the picture that falls out of it, if one does.
  /// </summary>
  /// <returns><c>true</c> when <paramref name="frame"/> holds a picture that is ready to be shown.</returns>
  bool TryDecode(CodedPacket packet, out RawImage frame);

  /// <summary>
  /// Takes the pictures the decoder is still holding once the packets have run out.
  /// </summary>
  /// <remarks>
  /// Empty for a codec whose every packet is a whole frame, which is why it has a default. A codec
  /// that reorders frames finishes a stream holding some, and those are pictures of the film — a
  /// caller that stopped at the last packet would lose the end of it.
  /// </remarks>
  IEnumerable<RawImage> Flush() => [];
}
