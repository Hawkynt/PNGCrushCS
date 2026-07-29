namespace FileFormat.Tiff;

/// <summary>TIFF specification constants: tag IDs, type IDs, compression IDs, photometric values.</summary>
internal static class TiffConstants {

  // Tag IDs
  public const ushort TagImageWidth = 256;
  public const ushort TagImageLength = 257;
  public const ushort TagBitsPerSample = 258;
  public const ushort TagCompression = 259;
  public const ushort TagPhotometric = 262;
  public const ushort TagStripOffsets = 273;
  public const ushort TagOrientation = 274;
  public const ushort TagSamplesPerPixel = 277;
  public const ushort TagRowsPerStrip = 278;
  public const ushort TagStripByteCounts = 279;
  public const ushort TagPlanarConfig = 284;
  public const ushort TagPredictor = 317;
  public const ushort TagColorMap = 320;
  public const ushort TagTileWidth = 322;
  public const ushort TagTileLength = 323;
  public const ushort TagTileOffsets = 324;
  public const ushort TagTileByteCounts = 325;

  // IFD entry type IDs
  public const ushort TypeByte = 1;
  public const ushort TypeAscii = 2;
  public const ushort TypeShort = 3;
  public const ushort TypeLong = 4;
  public const ushort TypeRational = 5;

  // Type sizes in bytes
  public static int TypeSize(ushort type) => type switch {
    TypeByte => 1,
    TypeAscii => 1,
    TypeShort => 2,
    TypeLong => 4,
    TypeRational => 8,
    _ => 1,
  };

  // Compression type constants (in-file values)
  public const ushort CompressionNone = 1;
  public const ushort CompressionPackBits = 32773;
  public const ushort CompressionLzw = 5;
  public const ushort CompressionDeflate = 8;

  // Photometric interpretation
  public const ushort PhotometricMinIsWhite = 0;
  public const ushort PhotometricMinIsBlack = 1;
  public const ushort PhotometricRgb = 2;
  public const ushort PhotometricPalette = 3;

  // Orientation
  public const ushort OrientationTopLeft = 1;

  // Planar configuration
  public const ushort PlanarConfigContig = 1;

  // Predictor
  public const ushort PredictorNone = 1;
  public const ushort PredictorHorizontal = 2;

  // Byte order marks
  public const ushort ByteOrderLE = 0x4949; // "II"
  public const ushort ByteOrderBE = 0x4D4D; // "MM"
  public const ushort MagicNumber = 42;
}
