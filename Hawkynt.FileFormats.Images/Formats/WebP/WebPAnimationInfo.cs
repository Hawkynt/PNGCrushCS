namespace FileFormat.WebP;

/// <summary>What the ANIM chunk states about an animation as a whole.</summary>
public sealed record WebPAnimationInfo {

  /// <summary>The background colour the file names, packed as the container stores it: [B, G, R, A]
  /// in ascending byte order, which read as one little-endian 32-bit word puts alpha in the top byte.</summary>
  /// <remarks>
  /// Carried because the file states it, not because it is drawn. Every decoder measured here —
  /// libwebp's own, ffmpeg's and ImageMagick's — clears disposed rectangles to transparent black and
  /// never paints this colour onto the canvas. It describes what the animation is meant to be shown
  /// against, which is the application's business rather than the picture's.
  /// </remarks>
  public required uint BackgroundColorBgra { get; init; }

  /// <summary>How many times the animation plays. Zero means without end.</summary>
  public required int LoopCount { get; init; }
}
