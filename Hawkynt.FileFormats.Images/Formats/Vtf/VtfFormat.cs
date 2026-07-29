namespace FileFormat.Vtf;

/// <summary>Pixel format for VTF textures.</summary>
public enum VtfFormat {
  Rgba8888 = 0,
  Abgr8888 = 1,
  Rgb888 = 2,
  Bgr888 = 3,
  Rgb565 = 4,
  I8 = 5,
  Ia88 = 6,
  A8 = 7,
  Rgb888Bluescreen = 9,
  Bgr888Bluescreen = 10,
  Argb8888 = 11,
  Bgra8888 = 12,
  Dxt1 = 13,
  Dxt3 = 14,
  Dxt5 = 15,
  Uv88 = 16,
  Rgba16161616F = 24,
  Rgba16161616 = 25,
  None = -1
}
