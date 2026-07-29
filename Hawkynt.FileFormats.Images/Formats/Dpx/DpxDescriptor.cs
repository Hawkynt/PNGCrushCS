namespace FileFormat.Dpx;

public enum DpxDescriptor {
  UserDefined = 0,
  Red = 1,
  Green = 2,
  Blue = 3,
  Alpha = 4,
  Luma = 6,
  ColorDifferenceCbCr = 7,
  Depth = 8,
  Composite = 9,
  Rgb = 50,
  Rgba = 51,
  Abgr = 52
}
