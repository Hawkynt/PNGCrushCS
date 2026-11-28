using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Wic;

/// <summary>The 32-byte little-endian header in front of every JWIC coder stream.</summary>
/// <param name="Generation">Which translator generation the magic number names.</param>
/// <param name="Width">Picture width (<c>iwidth</c>).</param>
/// <param name="Height">Picture height (<c>iheight</c>).</param>
/// <param name="Dpi">Resolution, 0 when unspecified.</param>
/// <param name="ChromaBudgetScale">The encoder's chroma budget coefficient (<c>ccbsc</c>); the translators write 25.</param>
/// <param name="ColorModel">0 grey, 2 colour.</param>
/// <param name="Transform">The wavelet used.</param>
/// <param name="MatrixWidth">Width of the padded coefficient matrix.</param>
/// <param name="MatrixHeight">Height of the padded coefficient matrix.</param>
/// <param name="LumaLevels">Pyramid depth of the grey or luma plane.</param>
/// <param name="ChromaLevels">Pyramid depth of the two chroma planes.</param>
/// <param name="BitsPerChannel">The four informational bits-per-channel bytes.</param>
/// <param name="TopPlanes">Highest bit plane of each coded plane.</param>
internal readonly record struct WicHeader(
  WicGeneration Generation,
  int Width,
  int Height,
  int Dpi,
  uint ChromaBudgetScale,
  int ColorModel,
  WicTransform Transform,
  int MatrixWidth,
  int MatrixHeight,
  int LumaLevels,
  int ChromaLevels,
  uint BitsPerChannel,
  uint TopPlanes) {

  public const int Size = 32;
  public const uint MagicVersion100 = 0x30FB7A1B;
  public const uint MagicVersion101 = 0xBEBADEFA;
  public const int VersionNumber100 = 1;
  public const int VersionNumber101 = 0x0101;
  public const int Grey = 0;
  public const int DeepGrey = 1;
  public const int Colour = 2;

  /// <summary>
  /// Largest coefficient matrix this reader allocates for: 4096 × 4096, which holds everything the
  /// 1.01 translator's 4096-sample line buffers could transform. A stream of a few bytes can
  /// legitimately describe a matrix this large (an all-black picture codes nothing), so the bound
  /// cannot come from the file length.
  /// </summary>
  public const long MaxMatrixSamples = 1L << 24;

  public bool IsColour => this.ColorModel == Colour;

  /// <summary>The generation the first bytes announce, or null when they announce neither.</summary>
  public static WicGeneration? Identify(ReadOnlySpan<byte> data) {
    if (data.Length < 6)
      return null;

    var magic = BinaryPrimitives.ReadUInt32LittleEndian(data);
    var version = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
    return magic switch {
      MagicVersion101 when version == VersionNumber101 => WicGeneration.Version101,
      MagicVersion100 when version == VersionNumber100 => WicGeneration.Version100,
      _ => null,
    };
  }

  /// <summary>Parses and validates the header, refusing anything the decoder could not honour.</summary>
  public static WicHeader Parse(ReadOnlySpan<byte> data) {
    if (data.Length < Size)
      throw new InvalidDataException($"A JWIC header is {Size} bytes; this data has {data.Length}.");

    var generation = Identify(data)
      ?? throw new InvalidDataException("Not a JWIC file: neither the 1.0 magic 1B 7A FB 30 (version 1) nor the 1.01 magic FA DE BA BE (version 0x0101).");

    var header = new WicHeader(
      generation,
      BinaryPrimitives.ReadUInt16LittleEndian(data[6..]),
      BinaryPrimitives.ReadUInt16LittleEndian(data[8..]),
      BinaryPrimitives.ReadUInt16LittleEndian(data[10..]),
      BinaryPrimitives.ReadUInt32LittleEndian(data[12..]),
      data[16],
      (WicTransform)data[17],
      BinaryPrimitives.ReadUInt16LittleEndian(data[18..]),
      BinaryPrimitives.ReadUInt16LittleEndian(data[20..]),
      data[22],
      data[23],
      BinaryPrimitives.ReadUInt32LittleEndian(data[24..]),
      BinaryPrimitives.ReadUInt32LittleEndian(data[28..]));

    header._Validate();
    return header;
  }

  /// <summary>Highest bit plane of plane <paramref name="index"/> (0 grey/luma, 1 and 2 chroma).</summary>
  public int TopPlane(int index) => (int)(this.TopPlanes >> (index * 8)) & 0xFF;

  public void WriteTo(Span<byte> destination) {
    BinaryPrimitives.WriteUInt32LittleEndian(destination, this.Generation == WicGeneration.Version101 ? MagicVersion101 : MagicVersion100);
    BinaryPrimitives.WriteUInt16LittleEndian(destination[4..], (ushort)(this.Generation == WicGeneration.Version101 ? VersionNumber101 : VersionNumber100));
    BinaryPrimitives.WriteUInt16LittleEndian(destination[6..], (ushort)this.Width);
    BinaryPrimitives.WriteUInt16LittleEndian(destination[8..], (ushort)this.Height);
    BinaryPrimitives.WriteUInt16LittleEndian(destination[10..], (ushort)this.Dpi);
    BinaryPrimitives.WriteUInt32LittleEndian(destination[12..], this.ChromaBudgetScale);
    destination[16] = (byte)this.ColorModel;
    destination[17] = (byte)this.Transform;
    BinaryPrimitives.WriteUInt16LittleEndian(destination[18..], (ushort)this.MatrixWidth);
    BinaryPrimitives.WriteUInt16LittleEndian(destination[20..], (ushort)this.MatrixHeight);
    destination[22] = (byte)this.LumaLevels;
    destination[23] = (byte)this.ChromaLevels;
    BinaryPrimitives.WriteUInt32LittleEndian(destination[24..], this.BitsPerChannel);
    BinaryPrimitives.WriteUInt32LittleEndian(destination[28..], this.TopPlanes);
  }

  private void _Validate() {
    if (this.ColorModel == DeepGrey)
      throw new NotSupportedException(
        "JWIC colour model 1 (grey deeper than 8 bits, SPARC byte order) is declared by the format but no "
        + "JWIC encoder or decoder ever implemented it, so its sample layout is undefined.");

    if (this.ColorModel is not (Grey or Colour))
      throw new InvalidDataException($"JWIC colour model {this.ColorModel} is not defined (0 grey, 2 colour).");

    if (this.Transform is not (WicTransform.Daubechies4 or WicTransform.Biorthogonal97 or WicTransform.Haar))
      throw new InvalidDataException($"JWIC transform {(int)this.Transform} is not defined (0 Daubechies, 1 B97, 2 Haar).");

    if (this.Width == 0 || this.Height == 0)
      throw new InvalidDataException("JWIC picture dimensions must be non-zero.");

    if (this.MatrixWidth < this.Width || this.MatrixHeight < this.Height)
      throw new InvalidDataException(
        $"JWIC coefficient matrix {this.MatrixWidth}x{this.MatrixHeight} is smaller than the picture {this.Width}x{this.Height}.");

    if ((long)this.MatrixWidth * this.MatrixHeight > MaxMatrixSamples)
      throw new InvalidDataException(
        $"JWIC coefficient matrix {this.MatrixWidth}x{this.MatrixHeight} exceeds the {MaxMatrixSamples} samples this reader allocates for.");

    if (this.IsColour && (this.MatrixWidth & 1) != 0)
      throw new InvalidDataException("A colour JWIC matrix must have an even width: its chroma planes are half as wide.");

    _ValidateLevels(this.LumaLevels, this.MatrixWidth, this.MatrixHeight, "luma");
    if (this.IsColour)
      _ValidateLevels(this.ChromaLevels, this.MatrixWidth / 2, this.MatrixHeight, "chroma");

    for (var plane = 0; plane < (this.IsColour ? 3 : 1); ++plane)
      if (this.TopPlane(plane) > 31)
        throw new InvalidDataException($"JWIC plane {plane} claims bit plane {this.TopPlane(plane)}; coefficients have 31 magnitude bits.");
  }

  /// <summary>
  /// The decoders scan with the stored level count but invert with one re-derived from the plane
  /// size, so a file where the two disagree decodes to noise; a plane too small to decompose at all
  /// makes every JWIC decoder index outside its pyramid.
  /// </summary>
  private static void _ValidateLevels(int levels, int width, int height, string plane) {
    var expected = WicWavelet.LevelsFor(width, height);
    if (expected == 0)
      throw new InvalidDataException(
        $"JWIC {plane} plane {width}x{height} is too small to decompose (each side must be even and at least 8), which no JWIC decoder can invert.");

    if (levels != expected)
      throw new InvalidDataException(
        $"JWIC {plane} plane {width}x{height} decomposes into {expected} levels, but the header says {levels}.");
  }
}
