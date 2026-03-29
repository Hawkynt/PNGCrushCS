using FileFormat.Core;

namespace FileFormat.DrawIt;

/// <summary>The 4-byte header at the start of every DrawIt file: Width (LE 16-bit) + Height (LE 16-bit).</summary>
[GenerateSerializer]
internal readonly partial record struct DrawItHeader(
  [property: HeaderField(0, 2, Endianness = Endianness.Little)] ushort Width,
  [property: HeaderField(2, 2, Endianness = Endianness.Little)] ushort Height
) {

  public const int StructSize = 4;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<DrawItHeader>();
}
