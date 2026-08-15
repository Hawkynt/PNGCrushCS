using System.Collections.Generic;

namespace FileFormat.Core;

/// <summary>
/// Assembles coded packets into a container. The fourth of the four things a video pipeline is made
/// of: mux.
/// </summary>
/// <remarks>
/// A muxer takes packets and never makes them. It is handed the stream descriptions up front —
/// either the ones a demuxer read out of another file or the ones an encoder produced — and from
/// then on it only places bytes and keeps the index straight.
/// <para/>
/// That is what makes the four-part split worth having: with a demuxer on one end and a muxer on the
/// other, remuxing a film into another container is those two and no codec in between, and the
/// picture that comes out the far side is bit for bit the one that went in. Put decoding in the
/// container reader and that is impossible — everything becomes a re-encode.
/// <para/>
/// Declared here with the rest of the shape although nothing implements it yet.
/// </remarks>
public interface IVideoContainerWriter<TSelf> : IVideoFormatMetadata<TSelf> where TSelf : IVideoContainerWriter<TSelf> {

  /// <summary>
  /// Begins a container holding the given streams, described as a demuxer or an encoder describes
  /// them, and carrying the given metadata as far as this container can hold it.
  /// </summary>
  static abstract TSelf Create(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata);

  /// <summary>Places one packet. Its <see cref="CodedPacket.StreamIndex"/> selects the stream.</summary>
  void WritePacket(CodedPacket packet);

  /// <summary>Closes the container — indices, sizes, header counts — and returns the finished file.</summary>
  byte[] Finish();
}
