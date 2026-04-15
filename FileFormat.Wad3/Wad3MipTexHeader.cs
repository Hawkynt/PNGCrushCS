using FileFormat.Core;

namespace FileFormat.Wad3;

/// <summary>The 40-byte MipTex sub-header within a WAD3 lump: name(16) + width(4) + height(4) + 4 mip offsets(16).</summary>
[GenerateSerializer]
public readonly partial record struct Wad3MipTexHeader(
  [property: String, SeqField(Size = 16)] string Name,
  uint Width,
  uint Height,
  uint MipOffset0,
  uint MipOffset1,
  uint MipOffset2,
  uint MipOffset3
) {

 public const int StructSize = 40;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<Wad3MipTexHeader>();
}
