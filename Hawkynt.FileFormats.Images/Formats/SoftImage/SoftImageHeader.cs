using FileFormat.Core;

namespace FileFormat.SoftImage;

/// <summary>
/// The 104-byte fixed header of a Softimage PIC file, big-endian throughout: magic, version, an
/// eighty-byte comment, the four letters <c>PICT</c>, the size, the pixel aspect ratio, and how the
/// picture is interlaced.
/// </summary>
/// <remarks>
/// Those four letters were not accounted for, so the size was read from where they sit and a 75 by
/// 75 picture came back as 20553 by 17236 — which is <c>PI</c> and <c>CT</c> read as numbers.
/// </remarks>
[GenerateSerializer, Endian(Endianness.Big)]
internal readonly partial record struct SoftImageHeader(
  [property: Field(0, 4)] uint Magic,
  [property: Field(4, 4)] float Version,
  [property: Field(8, 80), String] string Comment,
  [property: Field(88, 4)] uint Id,
  [property: Field(92, 2)] ushort Width,
  [property: Field(94, 2)] ushort Height,
  [property: Field(96, 4)] float Ratio,
  [property: Field(100, 2)] ushort Fields,
  [property: Field(102, 2)] ushort Padding
) {
  public const int StructSize = 104;

  /// <summary>The four letters that stand between the comment and the size.</summary>
  public const uint PictId = 0x50494354;
}
