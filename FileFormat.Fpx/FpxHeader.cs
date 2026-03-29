using FileFormat.Core;

namespace FileFormat.Fpx;

/// <summary>The 16-byte header of an FPX file: Magic(4 bytes "FPX\0"), Version(uint16 LE), Reserved(uint16 LE), Width(uint32 LE), Height(uint32 LE).</summary>
[GenerateSerializer]
[HeaderFiller("Magic", 0, 4)]
[HeaderFiller("Reserved", 6, 2)]
public readonly partial record struct FpxHeader(
  [property: HeaderField(4, 2)] ushort Version,
  [property: HeaderField(8, 4)] uint Width,
  [property: HeaderField(12, 4)] uint Height
) {

  public const int StructSize = 16;

  /// <summary>The 4-byte magic signature: "FPX\0".</summary>
  public static readonly byte[] Magic = [(byte)'F', (byte)'P', (byte)'X', 0x00];

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<FpxHeader>();
}
