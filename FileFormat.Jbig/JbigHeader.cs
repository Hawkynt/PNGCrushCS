using FileFormat.Core;

namespace FileFormat.Jbig;

/// <summary>The 20-byte BIE (Bi-level Image Entity) header at the start of every JBIG file.</summary>
[GenerateSerializer]
public readonly partial record struct JbigHeader(
  [property: HeaderField(0, 1)] byte DL,
  [property: HeaderField(1, 1)] byte D,
  [property: HeaderField(2, 1)] byte P,
  [property: HeaderField(3, 1)] byte Reserved,
  [property: HeaderField(4, 4, Endianness = Endianness.Big)] int XD,
  [property: HeaderField(8, 4, Endianness = Endianness.Big)] int YD,
  [property: HeaderField(12, 4, Endianness = Endianness.Big)] int L0,
  [property: HeaderField(16, 1)] byte MX,
  [property: HeaderField(17, 1)] byte MY,
  [property: HeaderField(18, 1)] byte Options,
  [property: HeaderField(19, 1)] byte Order
) {

  public const int StructSize = 20;

  /// <summary>TPBON flag: typical prediction on.</summary>
  internal const byte OptionTPBON = 0x08;

  /// <summary>LRLTWO flag: two-line template.</summary>
  internal const byte OptionLRLTWO = 0x40;

  public static HeaderFieldDescriptor[] GetFieldMap()
    => HeaderFieldMapper.GetFieldMap<JbigHeader>();
}
