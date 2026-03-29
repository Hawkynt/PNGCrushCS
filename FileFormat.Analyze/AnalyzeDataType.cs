namespace FileFormat.Analyze;

/// <summary>Data type codes for Analyze 7.5 header (offset 70).</summary>
public enum AnalyzeDataType : short {
  UInt8 = 2,
  Int16 = 4,
  Int32 = 8,
  Float32 = 16,
  Rgb24 = 128,
}
