using FileFormat.Core;
using FileFormat.Riff;

namespace FileFormat.Avi;

/// <summary>The <c>strh</c> chunk describing one stream inside a <c>LIST strl</c> (56 bytes).</summary>
/// <remarks>
/// <see cref="Handler"/> is not what decides the codec. ffmpeg reports the same <c>rawvideo</c>
/// stream, and decodes it, whether this field holds four zero bytes or the letters <c>DIB </c>; what
/// it reports as the codec tag is the <c>biCompression</c> of the stream format instead. So this
/// reader decides on that field too, and names both in a refusal.
/// </remarks>
[GenerateSerializer]
public readonly partial record struct AviStreamHeader(
  [property: SeqField(Size = 4)] FourCC Type,
  [property: SeqField(Size = 4)] FourCC Handler,
  uint Flags,
  short Priority,
  short Language,
  uint InitialFrames,
  uint Scale,
  uint Rate,
  uint Start,
  uint Length,
  uint SuggestedBufferSize,
  int Quality,
  uint SampleSize,
  short FrameLeft,
  short FrameTop,
  short FrameRight,
  short FrameBottom
) {

  public const int StructSize = 56;

  /// <summary>
  /// The header without its trailing <c>rcFrame</c> rectangle, which older writers leave off.
  /// </summary>
  public const int StructSizeWithoutFrameRectangle = 48;

  /// <summary>The four letters marking a stream of pictures.</summary>
  public const string VIDEO_STREAM_TYPE = "vids";

  /// <summary>Whether this stream carries pictures rather than sound or text.</summary>
  public bool IsVideo => this.Type.ToString() == VIDEO_STREAM_TYPE;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<AviStreamHeader>();
}
