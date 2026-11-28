using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Iwc;

/// <summary>The scale factors of one decomposition level: horizontal, vertical and diagonal band steps per channel.</summary>
/// <param name="Horizontal">Step of the H band for Y, Cb, Cr.</param>
/// <param name="Vertical">Step of the V band for Y, Cb, Cr.</param>
/// <param name="Diagonal">Step of the D band for Y, Cb, Cr.</param>
internal readonly record struct IwcLevelScales(float[] Horizontal, float[] Vertical, float[] Diagonal);

/// <summary>The little-endian header of an IWC file, as the WaveL applet's <c>ReadFile</c> takes it.</summary>
/// <remarks>
/// <c>"IWC"</c>, a version byte, the picture size, the level count and the encoder's quality setting
/// as 16-bit integers, then the approximation band's step (three floats) and offset (three 16-bit
/// integers) for Y, Cb and Cr, then nine floats per level — the H, V and D steps of Y, of Cb and of
/// Cr — and finally the payload's byte count as a 32-bit integer.
/// </remarks>
internal sealed record IwcHeader(
  int Version,
  int Width,
  int Height,
  int Levels,
  int Quality,
  float[] ApproximationScales,
  short[] ApproximationMinima,
  IwcLevelScales[] LevelScales,
  int PayloadLength) {

  public const int FixedSize = 30;
  public const int KnownVersion = 1;

  /// <summary>Largest picture this reader allocates for (the applet keeps a 16-bit sample per pixel and channel several times over).</summary>
  public const long MaxPixels = 1L << 26;

  /// <summary>Header length for <paramref name="levels"/> levels, payload count included.</summary>
  public static int SizeFor(int levels) => FixedSize + levels * 36 + 4;

  public static bool HasSignature(ReadOnlySpan<byte> data) => data.Length >= 4 && data[0] == 'I' && data[1] == 'W' && data[2] == 'C';

  /// <summary>Whether <paramref name="width"/> × <paramref name="height"/> can be decomposed <paramref name="levels"/> times.</summary>
  /// <remarks>
  /// Every level halves (rounding down) the previous level's approximation, and the applet's lifting
  /// step reads two samples of every row and column it synthesises, so each level must start from at
  /// least 2 × 2.
  /// </remarks>
  public static bool IsDecomposable(int width, int height, int levels) {
    if (levels < 1)
      return false;

    for (var level = 0; level < levels; ++level, width /= 2, height /= 2)
      if (width < 2 || height < 2)
        return false;

    return true;
  }

  public static IwcHeader Parse(ReadOnlySpan<byte> data) {
    if (!HasSignature(data))
      throw new InvalidDataException("Not an IWC file: it does not start with \"IWC\".");

    if (data.Length < FixedSize)
      throw new InvalidDataException($"An IWC header is at least {FixedSize + 4} bytes; this data has {data.Length}.");

    var version = data[3];
    if (version != KnownVersion)
      throw new NotSupportedException($"IWC version {version} is not known; only version {KnownVersion}, which the WaveL applet v1.1 reads, is.");

    int width = BinaryPrimitives.ReadInt16LittleEndian(data[4..]);
    int height = BinaryPrimitives.ReadInt16LittleEndian(data[6..]);
    int levels = BinaryPrimitives.ReadInt16LittleEndian(data[8..]);
    int quality = BinaryPrimitives.ReadInt16LittleEndian(data[10..]);

    if (width < 1 || height < 1)
      throw new InvalidDataException($"IWC picture size {width}x{height} is not positive.");

    if ((long)width * height > MaxPixels)
      throw new InvalidDataException($"IWC picture {width}x{height} exceeds the {MaxPixels} pixels this reader allocates for.");

    if (!IsDecomposable(width, height, levels))
      throw new InvalidDataException(
        $"IWC picture {width}x{height} cannot be decomposed {levels} times: every level needs at least 2x2 samples, which the applet's lifting step reads.");

    if (data.Length < SizeFor(levels))
      throw new InvalidDataException($"IWC header for {levels} levels is {SizeFor(levels)} bytes; this data has {data.Length}.");

    var approximationScales = new float[3];
    var approximationMinima = new short[3];
    for (var channel = 0; channel < 3; ++channel) {
      approximationScales[channel] = BinaryPrimitives.ReadSingleLittleEndian(data[(12 + channel * 4)..]);
      approximationMinima[channel] = BinaryPrimitives.ReadInt16LittleEndian(data[(24 + channel * 2)..]);
    }

    var levelScales = new IwcLevelScales[levels];
    var offset = FixedSize;
    for (var level = 0; level < levels; ++level) {
      var horizontal = new float[3];
      var vertical = new float[3];
      var diagonal = new float[3];
      for (var channel = 0; channel < 3; ++channel) {
        horizontal[channel] = BinaryPrimitives.ReadSingleLittleEndian(data[offset..]);
        vertical[channel] = BinaryPrimitives.ReadSingleLittleEndian(data[(offset + 4)..]);
        diagonal[channel] = BinaryPrimitives.ReadSingleLittleEndian(data[(offset + 8)..]);
        offset += 12;
      }

      levelScales[level] = new(horizontal, vertical, diagonal);
    }

    var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(data[offset..]);
    if (payloadLength < 0)
      throw new InvalidDataException($"IWC payload length {payloadLength} is negative.");

    return new(version, width, height, levels, quality, approximationScales, approximationMinima, levelScales, payloadLength);
  }

  public byte[] ToBytes(ReadOnlySpan<byte> payload) {
    var result = new byte[SizeFor(this.Levels) + payload.Length];
    var span = result.AsSpan();
    span[0] = (byte)'I';
    span[1] = (byte)'W';
    span[2] = (byte)'C';
    span[3] = (byte)this.Version;
    BinaryPrimitives.WriteInt16LittleEndian(span[4..], (short)this.Width);
    BinaryPrimitives.WriteInt16LittleEndian(span[6..], (short)this.Height);
    BinaryPrimitives.WriteInt16LittleEndian(span[8..], (short)this.Levels);
    BinaryPrimitives.WriteInt16LittleEndian(span[10..], (short)this.Quality);
    for (var channel = 0; channel < 3; ++channel) {
      BinaryPrimitives.WriteSingleLittleEndian(span[(12 + channel * 4)..], this.ApproximationScales[channel]);
      BinaryPrimitives.WriteInt16LittleEndian(span[(24 + channel * 2)..], this.ApproximationMinima[channel]);
    }

    var offset = FixedSize;
    foreach (var scales in this.LevelScales)
      for (var channel = 0; channel < 3; ++channel) {
        BinaryPrimitives.WriteSingleLittleEndian(span[offset..], scales.Horizontal[channel]);
        BinaryPrimitives.WriteSingleLittleEndian(span[(offset + 4)..], scales.Vertical[channel]);
        BinaryPrimitives.WriteSingleLittleEndian(span[(offset + 8)..], scales.Diagonal[channel]);
        offset += 12;
      }

    BinaryPrimitives.WriteInt32LittleEndian(span[offset..], payload.Length);
    payload.CopyTo(span[(offset + 4)..]);
    return result;
  }
}
