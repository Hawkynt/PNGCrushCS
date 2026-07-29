namespace FileFormat.Png;

/// <summary>PNG filter types as defined in the PNG specification</summary>
public enum PngFilterType {
  None = 0,
  Sub = 1,
  Up = 2,
  Average = 3,
  Paeth = 4
}
