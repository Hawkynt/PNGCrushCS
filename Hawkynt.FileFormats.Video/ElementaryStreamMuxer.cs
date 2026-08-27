using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace Hawkynt.FileFormats.Video;

/// <summary>
/// The common writer state for formats whose file syntax is exactly the coded packets in sequence.
/// </summary>
/// <remarks>
/// H.264 and H.265 Annex B streams, Motion JPEG streams and MPEG elementary video streams have no
/// container header, index or metadata block to synthesise. Their muxing operation is therefore the
/// deliberately boring one: validate that the declared stream is one this format can carry, then
/// write every coded packet byte for byte in packet order.
/// </remarks>
internal sealed class ElementaryStreamMuxer {

  private readonly MemoryStream _output = new();
  private readonly string _formatName;
  private bool _finished;

  internal ElementaryStreamMuxer(
    IReadOnlyList<MediaStreamInfo> streams,
    VideoMetadata metadata,
    string formatName,
    Func<MediaStreamInfo, bool> acceptsStream) {
    ArgumentNullException.ThrowIfNull(streams);
    ArgumentNullException.ThrowIfNull(metadata);
    ArgumentNullException.ThrowIfNull(formatName);
    ArgumentNullException.ThrowIfNull(acceptsStream);

    this._formatName = formatName;

    if (streams.Count != 1)
      throw new NotSupportedException($"{formatName} carries exactly one stream; {streams.Count} were supplied.");

    var stream = streams[0];
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Index != 0)
      throw new ArgumentException($"{formatName}'s only stream must have index 0, not {stream.Index}.", nameof(streams));

    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException($"{formatName} carries video only; stream 0 is {stream.Kind}.");

    if (!acceptsStream(stream))
      throw new NotSupportedException(
        $"{formatName} cannot carry stream 0 as '{stream.Codec}'"
        + (stream.CodecId == null ? "." : $" / '{stream.CodecId}'."));

    // Raw elementary streams have nowhere to put title, artist, dates, cover art or annotations.
    // The writer contract explicitly permits a container to drop metadata it cannot represent.
  }

  internal void WritePacket(CodedPacket packet) {
    if (this._finished)
      throw new InvalidOperationException($"{this._formatName} has already been finished.");

    if (packet.StreamIndex != 0)
      throw new ArgumentOutOfRangeException(nameof(packet), packet.StreamIndex,
        $"{this._formatName} has only stream 0.");

    this._output.Write(packet.Data.Span);
  }

  internal byte[] Finish() {
    if (this._finished)
      throw new InvalidOperationException($"{this._formatName} has already been finished.");

    this._finished = true;
    return this._output.ToArray();
  }
}
