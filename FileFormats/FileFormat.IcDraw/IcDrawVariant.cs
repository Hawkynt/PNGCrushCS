namespace FileFormat.IcDraw;

/// <summary>Which of the two ICDRAW file kinds a file is.</summary>
public enum IcDrawVariant {

  /// <summary>A single icon (.ibi, "ICBI"): one image followed by a 1-bit mask.</summary>
  SingleIcon,

  /// <summary>A group of icons (.ib3, "ICB3"): three images back to back, no mask.</summary>
  IconGroup,
}
