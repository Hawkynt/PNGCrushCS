namespace FileFormat.Viff;

/// <summary>What the bands of a VIFF picture mean.</summary>
/// <remarks>
/// These are the values the format defines, which is not the short list they were once written as
/// here — plain RGB is fifteen and not two, and two is a chromaticity space nothing here produces.
/// A file naming the wrong one is turned away by anything that checks, which is how it was found.
/// </remarks>
public enum ViffColorSpaceModel : uint {
  None = 0,
  NtscRgb = 1,
  NtscCieXy = 2,
  NtscXyz = 3,
  CieXyY = 4,
  Xyz = 5,
  YCbCr = 6,
  Hsv = 7,
  Hls = 8,
  Ihs = 9,
  Cmy = 10,
  UvW = 11,
  UcvcW = 12,
  Lab = 13,
  Luv = 14,

  /// <summary>Plain red, green and blue, which is what a picture written here holds.</summary>
  GenericRgb = 15,
}
