namespace FileFormat.Wic;

/// <summary>The wavelet a JWIC file was decomposed with.</summary>
public enum WicTransform : byte {

  /// <summary>Daubechies, four taps, circular boundaries.</summary>
  Daubechies4 = 0,

  /// <summary>Biorthogonal 9/7, circular boundaries; the translators' default.</summary>
  Biorthogonal97 = 1,

  /// <summary>Haar averages and differences; offered by the 1.01 translator only.</summary>
  Haar = 2,
}
