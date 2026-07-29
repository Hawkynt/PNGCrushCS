using System;
using System.IO;

namespace FileFormat.Jpeg;

/// <summary>Stateless assembler for JPEG marker segments and JFIF APP0 header.</summary>
internal static class JpegMarkerWriter {

  public static void WriteMarker(MemoryStream stream, byte marker) {
    stream.WriteByte(0xFF);
    stream.WriteByte(marker);
  }

  public static void WriteSoi(MemoryStream stream) => WriteMarker(stream, JpegMarker.SOI);

  public static void WriteEoi(MemoryStream stream) => WriteMarker(stream, JpegMarker.EOI);

  public static void WriteApp0Jfif(MemoryStream stream) {
    WriteMarker(stream, JpegMarker.APP0);
    var data = new byte[] {
      0x00, 0x10,       // Length = 16
      0x4A, 0x46, 0x49, 0x46, 0x00, // "JFIF\0"
      0x01, 0x01,       // Version 1.1
      0x00,             // Units = no units
      0x00, 0x01,       // X density = 1
      0x00, 0x01,       // Y density = 1
      0x00, 0x00        // No thumbnail
    };
    stream.Write(data);
  }

  public static void WriteDqt(MemoryStream stream, int tableId, int[] values, bool is16Bit = false) {
    WriteMarker(stream, JpegMarker.DQT);
    var dataLen = is16Bit ? 64 * 2 + 1 : 64 + 1;
    _WriteUint16(stream, dataLen + 2);

    stream.WriteByte((byte)((is16Bit ? 0x10 : 0x00) | (tableId & 0x0F)));

    if (is16Bit)
      for (var i = 0; i < 64; ++i) {
        stream.WriteByte((byte)(values[i] >> 8));
        stream.WriteByte((byte)(values[i] & 0xFF));
      }
    else
      for (var i = 0; i < 64; ++i)
        stream.WriteByte((byte)values[i]);
  }

  public static void WriteSof(MemoryStream stream, byte sofMarker, JpegFrameHeader frame) {
    WriteMarker(stream, sofMarker);
    var length = 8 + frame.Components.Length * 3;
    _WriteUint16(stream, length);
    stream.WriteByte(frame.Precision);
    _WriteUint16(stream, frame.Height);
    _WriteUint16(stream, frame.Width);
    stream.WriteByte((byte)frame.Components.Length);

    foreach (var comp in frame.Components) {
      stream.WriteByte(comp.Id);
      stream.WriteByte((byte)((comp.HSamplingFactor << 4) | comp.VSamplingFactor));
      stream.WriteByte(comp.QuantTableId);
    }
  }

  public static void WriteDht(MemoryStream stream, int tableClass, int tableId, JpegHuffmanTable table) {
    WriteMarker(stream, JpegMarker.DHT);
    var totalValues = table.Values.Length;

    _WriteUint16(stream, 2 + 1 + 16 + totalValues);
    stream.WriteByte((byte)((tableClass << 4) | (tableId & 0x0F)));
    stream.Write(table.Bits, 0, 16);
    stream.Write(table.Values, 0, totalValues);
  }

  public static void WriteDri(MemoryStream stream, int restartInterval) {
    WriteMarker(stream, JpegMarker.DRI);
    _WriteUint16(stream, 4);
    _WriteUint16(stream, restartInterval);
  }

  public static void WriteSos(MemoryStream stream, JpegScanHeader scan) {
    WriteMarker(stream, JpegMarker.SOS);
    var length = 6 + scan.Components.Length * 2;
    _WriteUint16(stream, length);
    stream.WriteByte((byte)scan.Components.Length);

    foreach (var comp in scan.Components) {
      stream.WriteByte(comp.ComponentId);
      stream.WriteByte((byte)((comp.DcTableId << 4) | comp.AcTableId));
    }

    stream.WriteByte(scan.SpectralStart);
    stream.WriteByte(scan.SpectralEnd);
    stream.WriteByte((byte)((scan.SuccessiveApproxHigh << 4) | scan.SuccessiveApproxLow));
  }

  public static void WriteMarkerSegment(MemoryStream stream, JpegMarkerSegment segment) {
    WriteMarker(stream, segment.Marker);
    _WriteUint16(stream, segment.Data.Length + 2);
    stream.Write(segment.Data);
  }

  private static void _WriteUint16(MemoryStream stream, int value) {
    stream.WriteByte((byte)(value >> 8));
    stream.WriteByte((byte)(value & 0xFF));
  }
}
