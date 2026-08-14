namespace FileFormat.WebP;

/// <summary>One frame of an animated WebP: a rectangle, when and how it is shown, and its pixels.</summary>
/// <remarks>
/// A frame is not a picture of its own. It is a rectangle placed somewhere on a canvas of the size
/// the VP8X chunk states, and it may be smaller than that canvas in both directions — animations
/// routinely encode only the part that changed. Reading one on its own and calling the result the
/// frame gives a picture of the right colours at the wrong size in the wrong place.
/// </remarks>
public sealed record WebPFrame {

  /// <summary>Left edge of this frame's rectangle on the canvas, in pixels.</summary>
  /// <remarks>Always even: the ANMF header stores it halved.</remarks>
  public required int X { get; init; }

  /// <summary>Top edge of this frame's rectangle on the canvas, in pixels.</summary>
  /// <remarks>Always even: the ANMF header stores it halved.</remarks>
  public required int Y { get; init; }

  /// <summary>Width of this frame's rectangle, which may be less than the canvas width.</summary>
  public required int Width { get; init; }

  /// <summary>Height of this frame's rectangle, which may be less than the canvas height.</summary>
  public required int Height { get; init; }

  /// <summary>How long this frame is shown, in milliseconds.</summary>
  public int DurationMilliseconds { get; init; }

  public WebPFrameDisposalMethod DisposalMethod { get; init; }
  public WebPFrameBlendMethod BlendMethod { get; init; }

  /// <summary>The frame's VP8 or VP8L chunk payload.</summary>
  public required byte[] ImageData { get; init; }

  /// <summary>Whether <see cref="ImageData"/> is a VP8L (lossless) rather than a VP8 (lossy) payload.</summary>
  public required bool IsLossless { get; init; }

  /// <summary>The frame's ALPH chunk payload, for lossy frames that carry alpha. <c>null</c> otherwise.</summary>
  public byte[]? AlphaChunk { get; init; }

  /// <summary>Whether this frame carries alpha at all — an ALPH chunk beside a lossy payload, or a
  /// lossless payload whose header says so.</summary>
  public required bool HasAlpha { get; init; }
}
