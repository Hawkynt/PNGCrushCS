namespace FileFormat.Core;

/// <summary>One decoded picture and when it is due.</summary>
/// <remarks>
/// The picture is a <see cref="RawImage"/> — the same type every image format in this library reads
/// and writes — so a frame can be saved, resampled, quantised or compared with exactly the code that
/// already does those things to a photograph. A video-only picture type would have needed all of it
/// written a second time.
/// </remarks>
/// <param name="Image">The picture.</param>
/// <param name="StreamIndex">Which stream of the container it came out of.</param>
/// <param name="PresentationTimestamp">When it is due, in that stream's time base, or <c>null</c>
/// where the container stated nothing.</param>
/// <param name="IsKeyFrame">Whether the packet it came from could be decoded on its own.</param>
public readonly record struct DecodedFrame(
  RawImage Image,
  int StreamIndex,
  long? PresentationTimestamp = null,
  bool IsKeyFrame = false);
