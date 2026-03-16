using FileFormat.Core;

namespace FileFormat.Png;

/// <summary>The 8-byte PNG signature at the start of every PNG file.</summary>
[GenerateSerializer]
public readonly partial record struct PngSignatureHeader(
  [property: HeaderField(0, 1)] byte Byte0,
  [property: HeaderField(1, 1)] byte Byte1,
  [property: HeaderField(2, 1)] byte Byte2,
  [property: HeaderField(3, 1)] byte Byte3,
  [property: HeaderField(4, 1)] byte Byte4,
  [property: HeaderField(5, 1)] byte Byte5,
  [property: HeaderField(6, 1)] byte Byte6,
  [property: HeaderField(7, 1)] byte Byte7
) {

  public const int StructSize = 8;

  /// <summary>The valid PNG signature: 137 80 78 71 13 10 26 10.</summary>
  public static PngSignatureHeader Expected => new(137, 80, 78, 71, 13, 10, 26, 10);

  /// <summary>Whether this signature matches the expected PNG signature.</summary>
  public bool IsValid => this == Expected;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<PngSignatureHeader>();
}
