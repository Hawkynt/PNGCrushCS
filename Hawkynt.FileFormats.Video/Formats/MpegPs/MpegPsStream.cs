using FileFormat.Core;

namespace FileFormat.MpegPs;

/// <summary>
/// One elementary stream of a program stream: which stream id carries it, and what a caller is told
/// about it.
/// </summary>
/// <remarks>
/// Internal because the stream id and the private substream id are this reader's own bookkeeping —
/// what a caller wants is the <see cref="MediaStreamInfo"/>, and that is what
/// <see cref="MpegProgramStreamContainer.Streams"/> hands out.
/// </remarks>
/// <param name="StreamId">The stream id the packets of this stream are introduced by.</param>
/// <param name="SubstreamId">For a stream inside private stream 1, the byte its payloads begin with
/// that says which of the several streams sharing <c>0xBD</c> this is; <c>null</c> otherwise.</param>
/// <param name="PrivateHeaderLength">How many bytes of the payload belong to the private stream's own
/// header rather than to the elementary stream, or <c>-1</c> for a substream whose header width this
/// reader does not know.</param>
/// <param name="Info">What the stream is told to the caller as.</param>
internal readonly record struct MpegPsStream(
  byte StreamId,
  byte? SubstreamId,
  int PrivateHeaderLength,
  MediaStreamInfo Info);
