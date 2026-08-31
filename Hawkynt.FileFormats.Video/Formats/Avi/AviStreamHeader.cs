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
  /// <summary>Gets the type.</summary>
  public FourCC Type { get; init; } = Type;
  /// <summary>Gets the handler.</summary>
  public FourCC Handler { get; init; } = Handler;
  /// <summary>Gets the flags.</summary>
  public uint Flags { get; init; } = Flags;
  /// <summary>Gets the priority.</summary>
  public short Priority { get; init; } = Priority;
  /// <summary>Gets the language.</summary>
  public short Language { get; init; } = Language;
  /// <summary>Gets the initial Frames.</summary>
  public uint InitialFrames { get; init; } = InitialFrames;
  /// <summary>Gets the scale.</summary>
  public uint Scale { get; init; } = Scale;
  /// <summary>Gets the rate.</summary>
  public uint Rate { get; init; } = Rate;
  /// <summary>Gets the start.</summary>
  public uint Start { get; init; } = Start;
  /// <summary>Gets the length.</summary>
  public uint Length { get; init; } = Length;
  /// <summary>Gets the suggested Buffer Size.</summary>
  public uint SuggestedBufferSize { get; init; } = SuggestedBufferSize;
  /// <summary>Gets the quality.</summary>
  public int Quality { get; init; } = Quality;
  /// <summary>Gets the sample Size.</summary>
  public uint SampleSize { get; init; } = SampleSize;
  /// <summary>Gets the frame Left.</summary>
  public short FrameLeft { get; init; } = FrameLeft;
  /// <summary>Gets the frame Top.</summary>
  public short FrameTop { get; init; } = FrameTop;
  /// <summary>Gets the frame Right.</summary>
  public short FrameRight { get; init; } = FrameRight;
  /// <summary>Gets the frame Bottom.</summary>
  public short FrameBottom { get; init; } = FrameBottom;

  /// <summary>The serialized structure size, in bytes.</summary>
  public const int StructSize = 56;

  /// <summary>
  /// The header without its trailing <c>rcFrame</c> rectangle, which older writers leave off.
  /// </summary>
  public const int StructSizeWithoutFrameRectangle = 48;

  /// <summary>The four letters marking a stream of pictures.</summary>
  public const string VIDEO_STREAM_TYPE = "vids";

  /// <summary>Whether this stream carries pictures rather than sound or text.</summary>
  public bool IsVideo => this.Type.ToString() == VIDEO_STREAM_TYPE;

  /// <summary>Gets descriptors for the serialized fields.</summary>
  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<AviStreamHeader>();
}
