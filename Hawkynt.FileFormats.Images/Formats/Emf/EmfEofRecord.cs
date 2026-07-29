using FileFormat.Core;

namespace FileFormat.Emf;

/// <summary>The 20-byte EMR_EOF record that terminates an EMF file.</summary>
[GenerateSerializer]
internal readonly partial record struct EmfEofRecord(
  uint RecordType,
  uint RecordSize,
  uint NumPalEntries,
  uint OffPalEntries,
  uint SizeLast
) {

 public const int StructSize = 20;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<EmfEofRecord>();
}
