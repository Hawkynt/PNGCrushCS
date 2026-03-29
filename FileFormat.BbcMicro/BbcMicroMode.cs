namespace FileFormat.BbcMicro;

/// <summary>BBC Micro screen modes.</summary>
public enum BbcMicroMode {
  /// <summary>640x256, 2 colors, 1bpp, 80 character columns.</summary>
  Mode0 = 0,
  /// <summary>320x256, 4 colors, 2bpp, 40 character columns.</summary>
  Mode1 = 1,
  /// <summary>160x256, 8+8 flashing colors, 4bpp, 20 character columns.</summary>
  Mode2 = 2,
  /// <summary>320x256, 2 colors, 1bpp, 40 character columns.</summary>
  Mode4 = 4,
  /// <summary>160x256, 4 colors, 2bpp, 20 character columns.</summary>
  Mode5 = 5
}
