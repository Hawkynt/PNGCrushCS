using FileFormat.Core;

namespace FileFormat.WebP;

/// <summary>The 10-byte VP8 keyframe header: 3-byte frame tag, 3-byte signature (0x9D 0x01 0x2A), width and height.</summary>
[GenerateSerializer]
[Filler(0, 3, "FrameTag")]
[Filler(3, 3, "Signature")]
internal readonly partial record struct Vp8FrameHeader( byte FrameTag0, byte FrameTag1, byte FrameTag2, byte Signature0, byte Signature1, byte Signature2, ushort WidthAndScale, ushort HeightAndScale
) {

 public const int StructSize = 10;

 public bool IsKeyframe => (this.FrameTag0 & 1) == 0;
 public bool HasValidSignature => this.Signature0 == 0x9D && this.Signature1 == 0x01 && this.Signature2 == 0x2A;
 public int Width => this.WidthAndScale & 0x3FFF;
 public int Height => this.HeightAndScale & 0x3FFF;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<Vp8FrameHeader>();
}
