using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Jpeg2000;

/// <summary>
/// Conforming baseline JP2 authoring profile used by the public RawImage writer.
/// </summary>
/// <remarks>
/// The general data model can describe codestreams with decomposition levels, but the encoder does
/// not need to implement every optional JPEG 2000 coding mode to be conforming. Authoring is kept to
/// an intentionally small lossless profile: 8-bit unsigned samples, one layer, reversible coding,
/// zero DWT decomposition levels and no multiple-component transform. That leaves one LL subband per
/// component and makes every emitted packet independently checkable against T.800.
/// </remarks>
public static class Jpeg2000BaselineWriter {

  private const ushort _SOC = 0xFF4F;
  private const ushort _QCD = 0xFF5C;
  private const ushort _SOT = 0xFF90;

  public static byte[] ToBytes(Jpeg2000File file) {
    if (file.Width <= 0 || file.Height <= 0)
      throw new ArgumentOutOfRangeException(nameof(file), "JPEG 2000 dimensions must be positive.");
    if (file.ComponentCount is not 1 and not 3)
      throw new NotSupportedException("The JPEG 2000 RawImage writer supports Gray8 and RGB24.");
    if (file.BitsPerComponent != 8)
      throw new NotSupportedException("The JPEG 2000 RawImage writer authors 8-bit components.");

    var expected = checked(file.Width * file.Height * file.ComponentCount);
    if (file.PixelData == null || file.PixelData.Length != expected)
      throw new InvalidDataException($"JPEG 2000 pixel buffer has {file.PixelData?.Length ?? 0} bytes; expected {expected}.");

    // Decomposition depth is an encoder decision, not image content. Zero levels is a perfectly
    // valid reversible codestream and avoids emitting unsupported detail-subband context modes.
    var baseline = file with { DecompositionLevels = 0 };
    var bytes = Jpeg2000Writer.ToBytesEbcot(baseline);
    _RepairReversibleQcd(bytes);
    return bytes;
  }

  /// <summary>
  /// The older EBCOT assembler used epsilon=R+1 with zero guard bits. That happens to leave Mb at
  /// eight but violates E.10 for an 8-bit LL reversible subband. The normative pair is epsilon=8,
  /// G=1, which still gives Mb=8 through E.2 and therefore does not change any Tier-1 data.
  /// </summary>
  private static void _RepairReversibleQcd(Span<byte> file) {
    var codestream = _FindSoc(file);
    var pos = codestream + 2;

    while (pos + 2 <= file.Length) {
      var marker = BinaryPrimitives.ReadUInt16BigEndian(file[pos..]);
      if (marker == _SOT)
        break;

      if ((marker & 0xFF00) != 0xFF00)
        throw new InvalidDataException("JPEG 2000 main header lost marker alignment before QCD.");

      pos += 2;
      if (pos + 2 > file.Length)
        throw new InvalidDataException("JPEG 2000 main-header marker has no length.");

      var length = BinaryPrimitives.ReadUInt16BigEndian(file[pos..]);
      if (length < 2 || pos + length > file.Length)
        throw new InvalidDataException("JPEG 2000 main-header marker length is invalid.");

      if (marker == _QCD) {
        if (length != 4)
          throw new InvalidDataException($"Zero-decomposition reversible QCD must be four bytes, got {length}.");

        file[pos + 2] = 0x20;     // G=1, style=0 (no quantization)
        file[pos + 3] = 8 << 3;   // epsilon_LL = R_I = 8, three low bits reserved zero
        return;
      }

      pos += length;
    }

    throw new InvalidDataException("JPEG 2000 codestream contains no QCD marker before SOT.");
  }

  private static int _FindSoc(ReadOnlySpan<byte> file) {
    for (var i = 0; i + 1 < file.Length; ++i)
      if (BinaryPrimitives.ReadUInt16BigEndian(file[i..]) == _SOC)
        return i;

    throw new InvalidDataException("JP2 output contains no JPEG 2000 SOC marker.");
  }
}
