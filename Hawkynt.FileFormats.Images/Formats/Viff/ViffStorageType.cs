namespace FileFormat.Viff;

/// <summary>VIFF pixel data storage types (VFF_TYP_*).</summary>
/// <remarks>
/// The numbering is Khoros', and it is not dense: for the integer types the constant is the width in
/// bytes, which leaves 3, 7 and 8 unused. They had been renumbered 0..6 as if they ran consecutively,
/// which put <see cref="Int"/> on 3 and <see cref="Float"/> on 4 — the value a real four-byte-integer
/// file carries. Only <see cref="Byte"/> is exercised against a third party here, since ImageMagick
/// writes nothing else.
/// </remarks>
public enum ViffStorageType : uint {
  Bit = 0,
  Byte = 1,
  Short = 2,
  Int = 4,
  Float = 5,
  Complex = 6,
  Double = 9,
  DoubleComplex = 10
}
