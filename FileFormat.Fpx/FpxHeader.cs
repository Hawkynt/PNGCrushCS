using FileFormat.Core;

namespace FileFormat.Fpx;

/// <summary>The 16-byte header of an FPX file: Magic(4 bytes "FPX\0"), Version(uint16 LE), Reserved(uint16 LE), Width(uint32 LE), Height(uint32 LE).</summary>
[GenerateSerializer]
[Filler(0, 4)]
[Filler(6, 2)]
public readonly partial record struct FpxHeader(
 [property: FieldOffset(4)] ushort Version,
 [property: FieldOffset(8)] uint Width,
 uint Height
) {

 public const int StructSize = 16;

 /// <summary>The 4-byte magic signature: "FPX\0".</summary>
 public static readonly byte[] Magic = [(byte)'F', (byte)'P', (byte)'X', 0x00];

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<FpxHeader>();
}
