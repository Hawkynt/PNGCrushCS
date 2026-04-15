using FileFormat.Core;

namespace FileFormat.AliasPix;

/// <summary>The 10-byte header at the start of every Alias/Wavefront PIX file: Width, Height, XOffset, YOffset, BitsPerPixel (all ushort BE).</summary>
[GenerateSerializer, Endian(Endianness.Big)]
public readonly partial record struct AliasPixHeader(
  ushort Width,
  ushort Height,
  ushort XOffset,
  ushort YOffset,
  ushort BitsPerPixel
) {

 public const int StructSize = 10;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<AliasPixHeader>();
}
