namespace FileFormat.Pvr;

/// <summary>PowerVR pixel format identifiers (64-bit format field).</summary>
public enum PvrPixelFormat : ulong {
  PVRTC_2BPP_RGB = 0,
  PVRTC_2BPP_RGBA = 1,
  PVRTC_4BPP_RGB = 2,
  PVRTC_4BPP_RGBA = 3,
  ETC1 = 6,
  ETC2_RGB = 22,
  ETC2_RGBA = 23,
  ASTC_4x4 = 27
}
