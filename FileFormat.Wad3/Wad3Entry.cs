using FileFormat.Core;

namespace FileFormat.Wad3;

/// <summary>A 32-byte directory entry within a WAD3 file.</summary>
[GenerateSerializer]
public readonly partial record struct Wad3Entry(
  int FilePos,
  int DiskSize,
  int Size,
  byte Type,
  byte Compression,
  ushort Padding,
  [property: String, SeqField(Size = 16)] string Name
) {

 public const int StructSize = 32;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<Wad3Entry>();
}
