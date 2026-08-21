using System;

namespace FileFormat.Asf;

/// <summary>
/// One payload as it lies inside a data packet: a whole media object, or a piece of one.
/// </summary>
/// <remarks>
/// A payload is not a packet in the sense the rest of this library uses the word. ASF calls a coded
/// frame a "media object" and is free to split one across several payloads and several data packets,
/// and equally free to put several whole objects in one packet; what a decoder is owed is the media
/// object, so the payloads are reassembled into one before anything leaves the container.
/// </remarks>
/// <param name="StreamNumber">The ASF stream number this belongs to, 1 to 127 — not a stream index.</param>
/// <param name="MediaObjectNumber">Which media object of that stream this is a piece of, counted modulo
/// the width of the field, so it repeats and cannot be used as an identity on its own.</param>
/// <param name="Offset">How far into the media object this piece begins.</param>
/// <param name="MediaObjectSize">How long the whole media object is, where the payload states it.</param>
/// <param name="PresentationTime">When the media object is due, in milliseconds, with the file's
/// preroll already taken off.</param>
/// <param name="IsKeyFrame">Whether the payload's stream-number byte said decoding may begin here.</param>
/// <param name="Data">This piece's bytes, as a window onto the file rather than a copy.</param>
internal readonly record struct AsfPayload(
  int StreamNumber,
  int MediaObjectNumber,
  int Offset,
  int MediaObjectSize,
  long PresentationTime,
  bool IsKeyFrame,
  ReadOnlyMemory<byte> Data);
