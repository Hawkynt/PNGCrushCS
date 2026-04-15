using FileFormat.Core;

namespace FileFormat.Wad2;

/// <summary>A 32-byte directory entry within a WAD2 file.</summary>
[GenerateSerializer]
public readonly partial record struct Wad2Entry(
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
 => HeaderFieldMapper.GetFieldMap<Wad2Entry>();
}
