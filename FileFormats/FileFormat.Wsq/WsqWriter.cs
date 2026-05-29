using System;
using System.IO;

namespace FileFormat.Wsq;

/// <summary>Assembles WSQ file bytes from pixel data.</summary>
public static class WsqWriter {

  public static byte[] ToBytes(WsqFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file.PixelData, file.Width, file.Height, file.Ppi, file.CompressionRatio);
  }

  internal static byte[] Assemble(byte[] pixelData, int width, int height, int ppi, double quality) {
    using var ms = new MemoryStream();

    // SOI
    _WriteMarker(ms, WsqMarker.SOI);

    // Forward DWT
    var coeffs = WsqWavelet.Forward2D(pixelData, width, height);

    // Compute shift (mean) and remove it
    var shift = 0.0;
    for (var i = 0; i < coeffs.Length; ++i)
      shift += coeffs[i];
    shift /= coeffs.Length;
    for (var i = 0; i < coeffs.Length; ++i)
      coeffs[i] -= shift;

    // Compute quantization params
    var quantParams = WsqQuantizer.ComputeParams(coeffs, width, height, quality);

    // Quantize
    var indices = WsqQuantizer.Quantize(coeffs, width, height, quantParams);

    // Build Huffman table
    var huffTable = WsqHuffman.BuildFromIndices(indices);
    huffTable.BuildDerived();

    // Write SOF
    _WriteSOF(ms, width, height, 1.0f, (float)shift, ppi);

    // Write SOB
    _WriteSOB(ms, quantParams);

    // Write DTT (transform table — CDF 9/7 hardcoded, write minimal)
    _WriteDTT(ms);

    // Write DQT
    _WriteDQT(ms, quantParams);

    // Write DHT
    _WriteDHT(ms, huffTable);

    // Encode scan data
    var scanData = WsqHuffman.Encode(indices, huffTable);

    // Write SOS
    _WriteSOS(ms, scanData);

    // EOI
    _WriteMarker(ms, WsqMarker.EOI);

    return ms.ToArray();
  }

  private static void _WriteMarker(MemoryStream ms, ushort marker) {
    ms.WriteByte((byte)(marker >> 8));
    ms.WriteByte((byte)(marker & 0xFF));
  }

  private static void _WriteSOF(MemoryStream ms, int width, int height, float scale, float shift, int ppi) {
    _WriteMarker(ms, WsqMarker.SOF);

    // Length: 2 (length field) + 1 (black) + 1 (white) + 2 (height) + 2 (width) + 4 (scale) + 4 (shift) + 2 (ppi) = 18
    var len = (ushort)18;
    _WriteUInt16BE(ms, len);

    ms.WriteByte(0);   // black reference
    ms.WriteByte(255); // white reference

    _WriteUInt16BE(ms, (ushort)height);
    _WriteUInt16BE(ms, (ushort)width);

    _WriteFloat32BE(ms, scale);
    _WriteFloat32BE(ms, shift);
    _WriteUInt16BE(ms, (ushort)ppi);
  }

  private static void _WriteSOB(MemoryStream ms, WsqQuantizer.QuantParams[] subbandParams) {
    _WriteMarker(ms, WsqMarker.SOB);

    // Length: 2 (length) + 1 (count) + count * 8 (binWidth + zeroBin)
    var count = subbandParams.Length;
    var len = (ushort)(2 + 1 + count * 8);
    _WriteUInt16BE(ms, len);

    ms.WriteByte((byte)count);
    for (var i = 0; i < count; ++i) {
      _WriteFloat32BE(ms, (float)subbandParams[i].BinWidth);
      _WriteFloat32BE(ms, (float)subbandParams[i].ZeroBinCenter);
    }
  }

  private static void _WriteDTT(MemoryStream ms) {
    _WriteMarker(ms, WsqMarker.DTT);
    // Minimal DTT: just a length field indicating hardcoded CDF 9/7
    var len = (ushort)3;
    _WriteUInt16BE(ms, len);
    ms.WriteByte(0x97); // CDF 9/7 marker byte
  }

  private static void _WriteDQT(MemoryStream ms, WsqQuantizer.QuantParams[] subbandParams) {
    _WriteMarker(ms, WsqMarker.DQT);
    // Length: 2 (length) + count * 4 (bin widths as float32)
    var count = subbandParams.Length;
    var len = (ushort)(2 + count * 4);
    _WriteUInt16BE(ms, len);
    for (var i = 0; i < count; ++i)
      _WriteFloat32BE(ms, (float)subbandParams[i].BinWidth);
  }

  private static void _WriteDHT(MemoryStream ms, WsqHuffman.HuffmanTable table) {
    _WriteMarker(ms, WsqMarker.DHT);
    // Length: 2 (length) + 1 (class/id) + 16 (code lengths) + values.Length
    var len = (ushort)(2 + 1 + 16 + table.Values.Length);
    _WriteUInt16BE(ms, len);
    ms.WriteByte(0x00); // Class 0, ID 0

    ms.Write(table.CodeLengths, 0, 16);
    ms.Write(table.Values, 0, table.Values.Length);
  }

  private static void _WriteSOS(MemoryStream ms, byte[] scanData) {
    _WriteMarker(ms, WsqMarker.SOS);
    // Scan header: 2 (length) + 1 (component count) + 1 (table selection) + 4 (scan data length BE)
    var headerLen = (ushort)8;
    _WriteUInt16BE(ms, headerLen);
    ms.WriteByte(1);    // 1 component
    ms.WriteByte(0x00); // Table selection

    // Write scan data length as 4-byte big-endian so decoder knows exact extent
    _WriteUInt32BE(ms, (uint)scanData.Length);

    // Write raw scan data
    ms.Write(scanData, 0, scanData.Length);
  }

  private static void _WriteUInt16BE(MemoryStream ms, ushort value) {
    ms.WriteByte((byte)(value >> 8));
    ms.WriteByte((byte)(value & 0xFF));
  }

  private static void _WriteUInt32BE(MemoryStream ms, uint value) {
    ms.WriteByte((byte)(value >> 24));
    ms.WriteByte((byte)(value >> 16));
    ms.WriteByte((byte)(value >> 8));
    ms.WriteByte((byte)(value & 0xFF));
  }

  private static void _WriteFloat32BE(MemoryStream ms, float value) {
    var bytes = BitConverter.GetBytes(value);
    if (BitConverter.IsLittleEndian)
      Array.Reverse(bytes);
    ms.Write(bytes, 0, 4);
  }
}
