using System;
using System.IO;

namespace FileFormat.Msp;

/// <summary>MSP v2 RLE compression. RunType==0 means run encoding, RunType!=0 means literal bytes.</summary>
internal static class MspRleCompressor {

  public static byte[] Compress(byte[] scanline) {
    ArgumentNullException.ThrowIfNull(scanline);
    if (scanline.Length == 0)
      return [];

    using var ms = new MemoryStream();
    var i = 0;

    while (i < scanline.Length) {
      var value = scanline[i];
      var runStart = i;
      while (i < scanline.Length && scanline[i] == value && i - runStart < byte.MaxValue)
        ++i;

      var runLength = i - runStart;
      if (runLength >= 3) {
        ms.WriteByte(0);
        ms.WriteByte((byte)runLength);
        ms.WriteByte(value);
        continue;
      }

      i = runStart;
      var literalStart = i;
      while (i < scanline.Length && i - literalStart < byte.MaxValue) {
        if (i + 2 < scanline.Length && scanline[i] == scanline[i + 1] && scanline[i] == scanline[i + 2])
          break;
        ++i;
      }

      var literalCount = i - literalStart;
      ms.WriteByte((byte)literalCount);
      ms.Write(scanline, literalStart, literalCount);
    }

    return ms.ToArray();
  }

  public static byte[] Decompress(byte[] data, int bytesPerRow) {
    ArgumentNullException.ThrowIfNull(data);
    if (bytesPerRow < 0)
      throw new ArgumentOutOfRangeException(nameof(bytesPerRow));
    if (bytesPerRow == 0)
      return data.Length == 0 ? [] : throw new InvalidDataException("MSP scanline has data for a zero-byte row.");

    var output = new byte[bytesPerRow];
    var inIdx = 0;
    var outIdx = 0;

    while (inIdx < data.Length) {
      var runType = data[inIdx++];
      if (runType == 0) {
        if (inIdx + 2 > data.Length)
          throw new InvalidDataException("Truncated MSP repeated-value packet.");

        var runCount = data[inIdx++];
        var runValue = data[inIdx++];
        if (runCount == 0)
          throw new InvalidDataException("MSP repeated-value packets may not have a zero count.");
        if (outIdx + runCount > bytesPerRow)
          throw new InvalidDataException("MSP repeated-value packet overruns the decoded scanline.");

        output.AsSpan(outIdx, runCount).Fill(runValue);
        outIdx += runCount;
        continue;
      }

      var literalCount = runType;
      if (inIdx + literalCount > data.Length)
        throw new InvalidDataException("Truncated MSP literal packet.");
      if (outIdx + literalCount > bytesPerRow)
        throw new InvalidDataException("MSP literal packet overruns the decoded scanline.");

      data.AsSpan(inIdx, literalCount).CopyTo(output.AsSpan(outIdx, literalCount));
      inIdx += literalCount;
      outIdx += literalCount;
    }

    if (outIdx != bytesPerRow)
      throw new InvalidDataException($"MSP scanline decoded to {outIdx} bytes; expected {bytesPerRow}.");

    return output;
  }
}
