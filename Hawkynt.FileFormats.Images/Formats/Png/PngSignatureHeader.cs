using FileFormat.Core;

namespace FileFormat.Png;

/// <summary>The 8-byte PNG signature at the start of every PNG file.</summary>
[GenerateSerializer]
public readonly partial record struct PngSignatureHeader(
  byte Byte0,
  byte Byte1,
  byte Byte2,
  byte Byte3,
  byte Byte4,
  byte Byte5,
  byte Byte6,
  byte Byte7
) {

 public const int StructSize = 8;

 /// <summary>The valid PNG signature: 137 80 78 71 13 10 26 10.</summary>
 public static PngSignatureHeader Expected => new(137, 80, 78, 71, 13, 10, 26, 10);

 /// <summary>Whether this signature matches the expected PNG signature.</summary>
 public bool IsValid => this == Expected;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<PngSignatureHeader>();
}
