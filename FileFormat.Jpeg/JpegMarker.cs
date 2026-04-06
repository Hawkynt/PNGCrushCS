namespace FileFormat.Jpeg;

/// <summary>JPEG marker byte constants (ITU-T T.81).</summary>
internal static class JpegMarker {
  public const byte Prefix = 0xFF;

  // Start/End
  public const byte SOI = 0xD8;
  public const byte EOI = 0xD9;

  // Frame markers
  public const byte SOF0 = 0xC0; // Baseline DCT
  public const byte SOF1 = 0xC1; // Extended sequential DCT
  public const byte SOF2 = 0xC2; // Progressive DCT

  // Huffman table
  public const byte DHT = 0xC4;

  // Quantization table
  public const byte DQT = 0xDB;

  // Restart interval
  public const byte DRI = 0xDD;

  // Start of scan
  public const byte SOS = 0xDA;

  // Restart markers
  public const byte RST0 = 0xD0;
  public const byte RST7 = 0xD7;

  // Application markers
  public const byte APP0 = 0xE0;
  public const byte APP1 = 0xE1;
  public const byte APP15 = 0xEF;

  // Comment
  public const byte COM = 0xFE;

  public static bool IsRst(byte marker) => marker >= RST0 && marker <= RST7;
  public static bool IsApp(byte marker) => marker >= APP0 && marker <= APP15;
  public static bool IsSof(byte marker) => marker >= SOF0 && marker <= 0xCF && marker != DHT && marker != 0xC8;
}
