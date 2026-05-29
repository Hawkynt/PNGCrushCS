namespace FileFormat.JpegXl;

/// <summary>
/// Public spec-conformant metadata extracted from a real JPEG XL file
/// (ISO/IEC 18181-1 §3.6.2 SizeHeader + §3.6.3 ImageMetadata + §3.6.5 FrameHeader).
///
/// <para>Returned by <see cref="JpegXlReader.TryReadSpecMetadata"/>.
/// Pixel decode of arbitrary real JPEG XL files is not yet implemented; this type
/// gives consumers access to the structural information that <em>is</em> spec-conformant
/// in our parser.</para>
/// </summary>
public readonly record struct JpegXlSpecMetadata(
  /// <summary>Image canvas width in pixels.</summary>
  int Width,
  /// <summary>Image canvas height in pixels.</summary>
  int Height,
  /// <summary>Bits per sample (or mantissa bits if <see cref="IsFloatSample"/> is true).</summary>
  int BitsPerSample,
  /// <summary>True for floating-point samples; false for unsigned-integer.</summary>
  bool IsFloatSample,
  /// <summary>Number of extra channels (alpha, depth, spot color, etc.).</summary>
  int NumExtraChannels,
  /// <summary>True if pixel data is XYB-encoded (perceptual-color JXL mode).</summary>
  bool IsXybEncoded,
  /// <summary>True if the first frame is a modular sub-codec frame; false if VarDCT.</summary>
  bool IsModularFrame,
  /// <summary>True if frame uses progressive (multi-pass) coding.</summary>
  bool IsProgressiveFrame
);
