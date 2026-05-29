using FileFormat.Core;

namespace FileFormat.AtariFalconXga;

/// <summary>The 4-byte header of an Atari Falcon XGA file: Width (ushort BE), Height (ushort BE).</summary>
[GenerateSerializer, Endian(Endianness.Big)]
public readonly partial record struct AtariFalconXgaHeader( ushort Width, ushort Height
) {

 public const int StructSize = 4;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<AtariFalconXgaHeader>();
}
