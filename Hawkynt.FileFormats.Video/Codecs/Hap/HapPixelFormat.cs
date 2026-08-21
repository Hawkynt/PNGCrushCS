namespace FileFormat.Codecs.Hap;

/// <summary>The texture format a Hap top-level section's data is coded in, read off its type byte.</summary>
internal enum HapPixelFormat {
  Dxt1Rgb,
  Dxt5Rgba,
  Dxt5ScaledYCoCg,
  Bc7Rgba,
  Rgtc1Alpha,
  Bc6UnsignedFloat,
  Bc6SignedFloat,
}
