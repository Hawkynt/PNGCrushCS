using FileFormat.Core;

namespace FileFormat.Bsave;

/// <summary>The 7-byte header at the start of every BSAVE file. All multi-byte fields are little-endian.</summary>
[GenerateSerializer]
public readonly partial record struct BsaveHeader(
  [property: HeaderField(0, 1)] byte Magic,
  [property: HeaderField(1, 2)] ushort Segment,
  [property: HeaderField(3, 2)] ushort Offset,
  [property: HeaderField(5, 2)] ushort Length
) {

  public const int StructSize = 7;
  public const byte MagicValue = 0xFD;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<BsaveHeader>();
}
