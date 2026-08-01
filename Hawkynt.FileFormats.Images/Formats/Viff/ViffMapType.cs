namespace FileFormat.Viff;

/// <summary>The element type of a VIFF colour map (VFF_MAPTYP_*).</summary>
/// <remarks>Numbered like <see cref="ViffStorageType"/>: the integer types carry their width in bytes.</remarks>
public enum ViffMapType : uint {
  None = 0,
  Byte = 1,
  Short = 2,
  Int = 4,
  Float = 5,
  Complex = 6,
  Double = 7
}
