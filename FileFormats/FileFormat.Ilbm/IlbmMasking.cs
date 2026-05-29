namespace FileFormat.Ilbm;

/// <summary>Masking modes for ILBM images.</summary>
public enum IlbmMasking : byte {
  None = 0,
  HasMask = 1,
  HasTransparentColor = 2,
  Lasso = 3
}
