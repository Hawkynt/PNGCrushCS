using FileFormat.Core;

namespace FileFormat.SpookySpritesFalcon;

/// <summary>The 4-byte header of a Spooky Sprites Falcon file: Width (ushort BE), Height (ushort BE).</summary>
[GenerateSerializer, Endian(Endianness.Big)]
public readonly partial record struct SpookySpritesFalconHeader( ushort Width, ushort Height
) {

 public const int StructSize = 4;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<SpookySpritesFalconHeader>();
}
