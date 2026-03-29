using FileFormat.Core;

namespace FileFormat.WebP;

/// <summary>The 5-byte VP8L bitstream header: 1-byte signature (0x2F) followed by a 4-byte packed bitfield.</summary>
[GenerateSerializer]
internal readonly partial record struct Vp8LHeader(
  [property: HeaderField(0, 1)] byte Signature,
  [property: HeaderField(1, 4)] uint BitField
) {

  public const int StructSize = 5;

  public int Width => (int)(this.BitField & 0x3FFF) + 1;
  public int Height => (int)((this.BitField >> 14) & 0x3FFF) + 1;
  public bool HasAlpha => ((this.BitField >> 28) & 1) != 0;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<Vp8LHeader>();
}
