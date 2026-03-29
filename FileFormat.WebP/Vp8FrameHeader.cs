using FileFormat.Core;

namespace FileFormat.WebP;

/// <summary>The 10-byte VP8 keyframe header: 3-byte frame tag, 3-byte signature (0x9D 0x01 0x2A), width and height.</summary>
[GenerateSerializer]
[HeaderFiller("FrameTag", 0, 3)]
[HeaderFiller("Signature", 3, 3)]
internal readonly partial record struct Vp8FrameHeader(
  [property: HeaderField(0, 1)] byte FrameTag0,
  [property: HeaderField(1, 1)] byte FrameTag1,
  [property: HeaderField(2, 1)] byte FrameTag2,
  [property: HeaderField(3, 1)] byte Signature0,
  [property: HeaderField(4, 1)] byte Signature1,
  [property: HeaderField(5, 1)] byte Signature2,
  [property: HeaderField(6, 2)] ushort WidthAndScale,
  [property: HeaderField(8, 2)] ushort HeightAndScale
) {

  public const int StructSize = 10;

  public bool IsKeyframe => (this.FrameTag0 & 1) == 0;
  public bool HasValidSignature => this.Signature0 == 0x9D && this.Signature1 == 0x01 && this.Signature2 == 0x2A;
  public int Width => this.WidthAndScale & 0x3FFF;
  public int Height => this.HeightAndScale & 0x3FFF;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<Vp8FrameHeader>();
}
