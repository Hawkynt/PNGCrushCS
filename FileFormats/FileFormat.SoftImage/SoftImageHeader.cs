using FileFormat.Core;

namespace FileFormat.SoftImage;

/// <summary>The 92-byte fixed header of a Softimage PIC file (big-endian): Magic (4), Version (4), Comment (80), Width (2), Height (2).</summary>
[GenerateSerializer, Endian(Endianness.Big)]
internal readonly partial record struct SoftImageHeader(
  [property: Field(0, 4)] uint Magic,
  [property: Field(4, 4)] float Version,
  [property: Field(8, 80), String] string Comment,
  [property: Field(88, 2)] ushort Width,
  [property: Field(90, 2)] ushort Height
) {
  public const int StructSize = 92;
}
