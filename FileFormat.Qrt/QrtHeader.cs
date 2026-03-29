using FileFormat.Core;

namespace FileFormat.Qrt;

/// <summary>The 10-byte header of a QRT ray tracer image: Width(ushort LE), Height(ushort LE), Reserved(6 bytes).</summary>
[GenerateSerializer]
[HeaderFiller("Reserved", 4, 6)]
public readonly partial record struct QrtHeader(
  [property: HeaderField(0, 2)] ushort Width,
  [property: HeaderField(2, 2)] ushort Height
) {

  public const int StructSize = 10;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<QrtHeader>();
}
