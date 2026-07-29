namespace FileFormat.Fits;

/// <summary>FITS BITPIX values specifying the data type of each pixel.</summary>
public enum FitsBitpix {
  UInt8 = 8,
  Int16 = 16,
  Int32 = 32,
  Float32 = -32,
  Float64 = -64
}
