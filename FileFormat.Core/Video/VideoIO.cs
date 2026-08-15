using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Core;

/// <summary>
/// Generic entry points for video I/O, and the one place demuxing and decoding are joined up. The
/// counterpart of <see cref="FormatIO"/> for containers.
/// </summary>
/// <remarks>
/// Every method here dispatches through static interface members on the type parameters, so the
/// joining costs nothing at run time and there is no reflection anywhere in it. The join lives here
/// rather than inside either interface precisely so that neither side has to know the other exists.
/// </remarks>
public static class VideoIO {

  // --- Demux ---

  public static TContainer Read<TContainer>(ReadOnlySpan<byte> data) where TContainer : IVideoContainerReader<TContainer>
    => TContainer.FromSpan(data);

  public static TContainer Read<TContainer>(byte[] data) where TContainer : IVideoContainerReader<TContainer> {
    ArgumentNullException.ThrowIfNull(data);

    return TContainer.FromBytes(data);
  }

  public static TContainer Read<TContainer>(FileInfo file) where TContainer : IVideoContainerReader<TContainer> {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Video file not found.", file.FullName);

    return TContainer.FromFile(file);
  }

  public static TContainer Read<TContainer>(Stream stream) where TContainer : IVideoContainerReader<TContainer> {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return TContainer.FromSpan(data);
    }

    using var buffer = new MemoryStream();
    stream.CopyTo(buffer);
    return TContainer.FromSpan(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
  }

  /// <summary>The first stream of the container that carries pictures, or <c>null</c> when it has none.</summary>
  public static MediaStreamInfo? FirstVideoStream<TContainer>(TContainer container) where TContainer : IVideoContainerReader<TContainer> {
    foreach (var stream in TContainer.Streams(container))
      if (stream.Kind == MediaStreamKind.Video)
        return stream;

    return null;
  }

  // --- Demux + decode ---

  /// <summary>
  /// Walks the pictures of one stream, decoding each packet as it is reached and no sooner.
  /// </summary>
  /// <remarks>
  /// The decoder is built inside the walk rather than outside it, for two reasons. Nothing is
  /// decoded and no decoder state is allocated until a caller asks for the first frame — a caller
  /// that wants one frame of an hour pays for one frame. And enumerating a second time starts a
  /// second decoder from the beginning, where a shared one would carry the first walk's state into
  /// the second and hand back frames predicted from the wrong reference.
  /// </remarks>
  public static IEnumerable<DecodedFrame> DecodeStream<TContainer, TDecoder>(TContainer container, MediaStreamInfo stream)
    where TContainer : IVideoContainerReader<TContainer>
    where TDecoder : IVideoCodecDecoder<TDecoder> {
    ArgumentNullException.ThrowIfNull(stream);

    return Decode<TDecoder>(TContainer.ReadPackets(container, stream.Index), stream);
  }

  /// <summary>Walks the pictures a sequence of packets decodes to, one packet at a time.</summary>
  public static IEnumerable<DecodedFrame> Decode<TDecoder>(IEnumerable<CodedPacket> packets, MediaStreamInfo stream)
    where TDecoder : IVideoCodecDecoder<TDecoder>
    => Decode(packets, stream, static info => TDecoder.Create(info));

  /// <summary>
  /// Walks the pictures a sequence of packets decodes to, with the codec chosen by the caller.
  /// </summary>
  /// <remarks>
  /// The overload a registry uses: which codec a stream needs is known only once the stream has been
  /// read, so the decoder arrives as a factory rather than as a type parameter. The factory itself
  /// still comes from generated, statically dispatched code — there is no reflection on this path
  /// either.
  /// </remarks>
  public static IEnumerable<DecodedFrame> Decode(
    IEnumerable<CodedPacket> packets, MediaStreamInfo stream, Func<MediaStreamInfo, IVideoFrameDecoder> decoderFactory) {
    ArgumentNullException.ThrowIfNull(packets);
    ArgumentNullException.ThrowIfNull(stream);
    ArgumentNullException.ThrowIfNull(decoderFactory);

    return _Decode(packets, stream, decoderFactory);

    static IEnumerable<DecodedFrame> _Decode(
      IEnumerable<CodedPacket> packets, MediaStreamInfo stream, Func<MediaStreamInfo, IVideoFrameDecoder> decoderFactory) {
      var decoder = decoderFactory(stream);
      var lastTimestamp = (long?)null;

      foreach (var packet in packets) {
        if (!decoder.TryDecode(packet, out var picture))
          continue;

        lastTimestamp = packet.PresentationTimestamp;
        yield return new(picture, stream.Index, packet.PresentationTimestamp, packet.IsKeyFrame);
      }

      // Whatever the codec was still holding when the packets ran out. Those frames have no packet
      // of their own to take a timestamp from, so they carry the last one that was seen.
      foreach (var picture in decoder.Flush())
        yield return new(picture, stream.Index, lastTimestamp);
    }
  }
}
