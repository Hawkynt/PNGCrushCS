namespace FileFormat.WebP;

/// <summary>How an animation frame's pixels meet the ones already on the canvas.</summary>
public enum WebPFrameBlendMethod : byte {

  /// <summary>Alpha-blend this frame over the canvas the previous frames left.</summary>
  /// <remarks>Encoded as a zero bit — the flag in the ANMF header reads "do not blend".</remarks>
  AlphaBlend = 0,

  /// <summary>Replace the canvas inside this frame's rectangle outright, alpha included.</summary>
  None = 1,
}
