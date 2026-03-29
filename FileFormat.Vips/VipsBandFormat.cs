namespace FileFormat.Vips;

/// <summary>VIPS band (sample) format describing the data type of each pixel component.</summary>
public enum VipsBandFormat {
  UChar = 0,
  Char = 1,
  UShort = 2,
  Short = 3,
  UInt = 4,
  Int = 5,
  Float = 6,
  Complex = 7,
  Double = 8,
  DpComplex = 9,
}
