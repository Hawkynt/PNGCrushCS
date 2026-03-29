using FileFormat.Core;

namespace FileFormat.Apng;

/// <summary>The 8-byte acTL (Animation Control) chunk data in an APNG file.</summary>
[GenerateSerializer]
public readonly partial record struct ApngActl(
  [property: HeaderField(0, 4, Endianness = Endianness.Big)] int NumFrames,
  [property: HeaderField(4, 4, Endianness = Endianness.Big)] int NumPlays
) {

  public const int StructSize = 8;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<ApngActl>();
}
