using FileFormat.Core;

namespace FileFormat.Wmf;

/// <summary>The 6-byte META_EOF record that terminates a WMF file.</summary>
[GenerateSerializer]
internal readonly partial record struct WmfEofRecord(
  uint SizeInWords,
  ushort Function
) {

 public const int StructSize = 6;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<WmfEofRecord>();
}
