using System;

namespace FileFormat.ChinonEs1000;

/// <summary>Writes the fixed-size ES-1000 COMET container around a synthesized CCD field.</summary>
public static class ChinonEs1000Writer {

  public static byte[] ToBytes(ChinonEs1000File file) {
    var expected = ChinonEs1000File.CcdColumns * ChinonEs1000File.CcdLines;
    if (file.CcdData == null || file.CcdData.Length != expected)
      throw new ArgumentException($"A Chinon ES-1000 frame needs exactly {expected} CCD bytes.", nameof(file));

    var output = new byte[ChinonEs1000File.FileSize];
    ChinonEs1000File.Magic.CopyTo(output);
    file.CcdData.CopyTo(output, ChinonEs1000File.FileHeaderSize + ChinonEs1000File.CameraHeaderSize);
    return output;
  }
}
