using FileFormat.Core;

namespace FileFormat.Jbig;

/// <summary>The 20-byte BIE (Bi-level Image Entity) header at the start of every JBIG file.</summary>
[GenerateSerializer, Endian(Endianness.Big)]
public readonly partial record struct JbigHeader(
  byte DL,
  byte D,
  byte P,
  byte Reserved,
  int XD,
  int YD,
  int L0,
  byte MX,
  byte MY,
  byte Options,
  byte Order
) {

 public const int StructSize = 20;

 /// <summary>TPBON flag: typical prediction on.</summary>
 internal const byte OptionTPBON = 0x08;

 /// <summary>LRLTWO flag: two-line template.</summary>
 internal const byte OptionLRLTWO = 0x40;

 public static HeaderFieldDescriptor[] GetFieldMap()
 => HeaderFieldMapper.GetFieldMap<JbigHeader>();
}
