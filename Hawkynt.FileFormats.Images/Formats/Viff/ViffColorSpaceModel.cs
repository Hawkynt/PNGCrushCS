namespace FileFormat.Viff;

/// <summary>VIFF color space models (VFF_CM_*).</summary>
/// <remarks>
/// Only the first six were listed, under invented names, so the writer labelled an RGB image 2 —
/// which in Khoros' table is NTSC CMY. The plain-RGB value is the last one,
/// <see cref="GenericRgb"/>, and that is what ImageMagick both writes and expects.
/// </remarks>
public enum ViffColorSpaceModel : uint {
  None = 0,
  NtscRgb = 1,
  NtscCmy = 2,
  NtscYiq = 3,
  Hsv = 4,
  Hls = 5,
  Ihs = 6,
  CieRgb = 7,
  CieXyz = 8,
  CieUvw = 9,
  CieUcsUvw = 10,
  CieUcsSow = 11,
  CieUcsLab = 12,
  CieUcsLuv = 13,
  Generic = 14,
  GenericRgb = 15
}
