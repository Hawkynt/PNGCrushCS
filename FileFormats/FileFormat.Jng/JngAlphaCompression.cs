namespace FileFormat.Jng;

/// <summary>Alpha channel compression method for JNG files.</summary>
public enum JngAlphaCompression : byte {
  PngDeflate = 0,
  Jpeg = 8
}
