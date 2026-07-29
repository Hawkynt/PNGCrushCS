namespace FileFormat.Xwd;

/// <summary>X11 visual class types.</summary>
public enum XwdVisualClass : uint {
  StaticGray = 0,
  GrayScale = 1,
  StaticColor = 2,
  PseudoColor = 3,
  TrueColor = 4,
  DirectColor = 5
}
