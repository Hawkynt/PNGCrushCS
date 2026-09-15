using System;

namespace FileFormat.Gif;

/// <summary>Shrinks a frame's image descriptor to the smallest rectangle that still carries
/// information, moving the rest of the canvas into the frame's position offset.</summary>
/// <remarks>
/// The saving is real and often large: for an animation whose frames differ only in a small region,
/// every pixel outside that region is either the background or already on the canvas from the
/// previous frame, and the LZW stream does not have to carry it at all. Which index counts as
/// "carries no information" depends on the caller's compositing plan — the background index when the
/// previous frame is left in place, the transparent index when the frame is a difference — so it is
/// a parameter rather than a policy baked in here.
/// </remarks>
public static class GifFrameWindow {

  /// <summary>Crops <paramref name="pixels"/> to the bounding box of everything that is not
  /// <paramref name="skipIndex"/>, returning the cropped pixels together with the adjusted position
  /// and size. Returns the input unchanged when nothing can be trimmed; returns a 1x1 frame holding
  /// <paramref name="skipIndex"/> when every pixel is skippable, because GIF has no zero-sized frame.</summary>
  /// <param name="pixels">Row-major indexed pixels, <c>size.Width * size.Height</c> long.</param>
  /// <param name="size">The frame's current size.</param>
  /// <param name="position">The frame's current position on the logical screen.</param>
  /// <param name="skipIndex">The palette index that carries no information for this frame.</param>
  public static (byte[] Pixels, Dimensions Size, Offset Position) Trim(
    byte[] pixels,
    Dimensions size,
    Offset position,
    byte skipIndex) {
    ArgumentNullException.ThrowIfNull(pixels);
    int width = size.Width;
    int height = size.Height;
    if (width == 0 || height == 0 || pixels.Length < width * height)
      return (pixels, size, position);

    var top = height;
    var bottom = -1;
    var left = width;
    var right = -1;

    for (var y = 0; y < height; ++y) {
      var row = y * width;
      for (var x = 0; x < width; ++x) {
        if (pixels[row + x] == skipIndex)
          continue;
        if (y < top) top = y;
        if (y > bottom) bottom = y;
        if (x < left) left = x;
        if (x > right) right = x;
      }
    }

    // Nothing but skippable pixels: GIF cannot express a 0x0 frame, so emit the smallest legal one.
    if (bottom < top)
      return ([skipIndex], new Dimensions(1, 1), position);

    var newWidth = right - left + 1;
    var newHeight = bottom - top + 1;
    if (newWidth == width && newHeight == height)
      return (pixels, size, position);

    var trimmed = new byte[newWidth * newHeight];
    for (var y = 0; y < newHeight; ++y)
      Array.Copy(pixels, (top + y) * width + left, trimmed, y * newWidth, newWidth);

    return (
      trimmed,
      new Dimensions(newWidth, newHeight),
      new Offset(position.X + left, position.Y + top)
    );
  }
}
