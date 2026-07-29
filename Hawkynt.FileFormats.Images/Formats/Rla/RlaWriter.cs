using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Rla;

/// <summary>Assembles RLA (Wavefront Run-Length) file bytes from pixel data.</summary>
public static class RlaWriter {

  public static byte[] ToBytes(RlaFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var width = file.Width;
    var height = file.Height;
    var totalChannels = file.NumChannels + file.NumMatte;
    var bytesPerChannel = file.NumBits <= 8 ? 1 : 2;
    var scanlineChannelSize = width * bytesPerChannel;

    var header = new RlaHeader(
      WindowLeft: 0,
      WindowRight: (short)(width - 1),
      WindowBottom: 0,
      WindowTop: (short)(height - 1),
      ActiveWindowLeft: 0,
      ActiveWindowRight: (short)(width - 1),
      ActiveWindowBottom: 0,
      ActiveWindowTop: (short)(height - 1),
      FrameNumber: (short)file.FrameNumber,
      StorageType: (short)file.StorageType,
      NumChannels: (short)file.NumChannels,
      NumMatte: (short)file.NumMatte,
      NumAux: 0,
      Revision: -2,
      NumBits: (short)file.NumBits,
      MatteType: 0,
      MatteBits: file.NumMatte > 0 ? (short)file.NumBits : (short)0,
      AuxType: 0,
      AuxBits: 0,
      FieldRendered: 0,
      JobNumber: 0,
      Next: 0,
      Gamma: string.Empty,
      RedChroma: string.Empty,
      GreenChroma: string.Empty,
      BlueChroma: string.Empty,
      WhitePoint: string.Empty,
      FileName: string.Empty,
      Description: file.Description ?? string.Empty,
      ProgramName: file.ProgramName ?? string.Empty,
      MachineName: string.Empty,
      User: string.Empty,
      Date: string.Empty,
      Aspect: string.Empty,
      AspectRatio: string.Empty,
      ColorChannel: string.Empty,
      Time: string.Empty,
      Filter: string.Empty,
      AuxData: string.Empty
    );

    // Compress all scanline channels
    var compressedChunks = new byte[height * totalChannels][];
    for (var row = 0; row < height; ++row)
      for (var ch = 0; ch < totalChannels; ++ch) {
        var srcOffset = (row * totalChannels + ch) * scanlineChannelSize;
        var scanline = new byte[scanlineChannelSize];
        var available = Math.Min(scanlineChannelSize, file.PixelData.Length - srcOffset);
        if (available > 0)
          file.PixelData.AsSpan(srcOffset, available).CopyTo(scanline.AsSpan(0));

        compressedChunks[row * totalChannels + ch] = RlaRleCompressor.Compress(scanline);
      }

    // Calculate layout: header + offset table + scanline data
    var offsetTableSize = height * 4;
    var dataStart = RlaHeader.StructSize + offsetTableSize;

    // Calculate scanline data positions
    var scanlineOffsets = new int[height];
    var currentOffset = dataStart;
    for (var row = 0; row < height; ++row) {
      scanlineOffsets[row] = currentOffset;
      for (var ch = 0; ch < totalChannels; ++ch)
        currentOffset += 2 + compressedChunks[row * totalChannels + ch].Length; // 2 bytes length prefix + data
    }

    // Assemble output
    var totalSize = currentOffset;
    var result = new byte[totalSize];

    // Write header
    header.WriteTo(result.AsSpan());

    // Write offset table
    for (var i = 0; i < height; ++i)
      BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(RlaHeader.StructSize + i * 4), scanlineOffsets[i]);

    // Write scanline data
    for (var row = 0; row < height; ++row) {
      var pos = scanlineOffsets[row];
      for (var ch = 0; ch < totalChannels; ++ch) {
        var chunk = compressedChunks[row * totalChannels + ch];
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(pos), (ushort)chunk.Length);
        pos += 2;
        chunk.AsSpan(0, chunk.Length).CopyTo(result.AsSpan(pos));
        pos += chunk.Length;
      }
    }

    return result;
  }
}
