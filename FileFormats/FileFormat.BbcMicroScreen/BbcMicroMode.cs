namespace FileFormat.BbcMicroScreen;

/// <summary>The BBC Micro display modes that hold a bitmap.</summary>
public enum BbcMicroMode {
  /// <summary>640x256 monochrome, shown with doubled scanlines.</summary>
  Mode0 = 0,

  /// <summary>320x256 in four colours.</summary>
  Mode1 = 1,

  /// <summary>160x256 in sixteen colours, shown with doubled columns.</summary>
  Mode2 = 2,

  /// <summary>320x256 monochrome.</summary>
  Mode4 = 4,

  /// <summary>160x256 in four colours, shown with doubled columns.</summary>
  Mode5 = 5,
}
