namespace FileFormat.WebP;

/// <summary>What an animation frame leaves behind on the canvas once its turn is over.</summary>
public enum WebPFrameDisposalMethod : byte {

  /// <summary>Leave the canvas as it is. The next frame draws on top of what this one left.</summary>
  None = 0,

  /// <summary>Clear this frame's rectangle before the next frame is drawn.</summary>
  /// <remarks>
  /// The container specification words this as "dispose to background color", which reads as though
  /// the colour the ANIM chunk states were painted there. No decoder does that: libwebp clears the
  /// rectangle to transparent black, and ffmpeg and ImageMagick follow it. The stated background
  /// colour is a hint for whatever the animation is shown against, never part of the picture.
  /// </remarks>
  Background = 1,
}
