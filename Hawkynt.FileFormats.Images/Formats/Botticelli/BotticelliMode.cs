namespace FileFormat.Botticelli;

/// <summary>The three pictures a .p4i file can hold, told apart by length and a marker.</summary>
public enum BotticelliMode {

  /// <summary>320x200 at one bit per pixel, two colours per cell.</summary>
  Hires,

  /// <summary>160 double-width pixels at two bits per pixel, four colours per cell.</summary>
  Multicolor,

  /// <summary>The 128x64 startup logo, four fixed colours.</summary>
  Logo,
}
