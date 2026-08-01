using FileFormat.Core;

namespace FileFormat.SpookySpritesFalcon;

/// <summary>The 12-byte header of a Spooky Sprites Falcon file.</summary>
/// <remarks>
/// Four letters naming the format, then the size, then four bytes nothing reads. It used to be
/// written here as the size alone, with no name in front of it — so nothing could tell one of these
/// from any other pair of words.
/// </remarks>
[GenerateSerializer, Endian(Endianness.Big)]
[Filler(0, 4)]
[Filler(8, 4)]
public readonly partial record struct SpookySpritesFalconHeader(
  [property: Field(4, 2)] ushort Width,
  [property: Field(6, 2)] ushort Height
) {

  public const int StructSize = 12;

  /// <summary>The four letters every file starts with.</summary>
  public const string Signature = "tre1";

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<SpookySpritesFalconHeader>();
}
