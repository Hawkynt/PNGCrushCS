namespace FileFormat.Gif;

/// <summary>The two standardised GIF specification versions.</summary>
public enum GifVersion {
  /// <summary>GIF87a — original spec; no graphic control extensions, no application extensions, no
  /// transparency, no animation. Writers should only emit this when the file uses none of those features.</summary>
  Gif87a,
  /// <summary>GIF89a — adds graphic control extension (transparency, disposal, delay), comment / plain-text /
  /// application extensions. The default for anything modern.</summary>
  Gif89a,
}
