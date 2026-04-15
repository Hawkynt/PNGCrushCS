using FileFormat.Core;

namespace FileFormat.Mng;

/// <summary>MHDR chunk data (28 bytes) for MNG files.</summary>
[GenerateSerializer, Endian(Endianness.Big)]
public readonly partial record struct MngHeader(
  uint Width,
  uint Height,
  uint TicksPerSecond,
  uint NominalLayerCount,
  uint NominalFrameCount,
  uint NominalPlayTime,
  uint Profile
) {
 public const int StructSize = 28;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<MngHeader>();
}
