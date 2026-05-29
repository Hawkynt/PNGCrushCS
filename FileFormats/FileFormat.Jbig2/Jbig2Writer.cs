using System;
using System.IO;

namespace FileFormat.Jbig2;

/// <summary>Writes JBIG2 files as standalone format with MMR-coded generic regions.</summary>
public static class Jbig2Writer {

  public static byte[] ToBytes(Jbig2File file) {
    ArgumentNullException.ThrowIfNull(file);

    using var ms = new MemoryStream();

    // File header: magic (8 bytes)
    ms.Write(Jbig2Reader.Magic, 0, Jbig2Reader.Magic.Length);

    // Flags: sequential organization (bit 0 = 1), known page count (bit 1 = 0)
    ms.WriteByte(0x01);

    // Page count: 1 (4 bytes BE)
    _WriteInt32BE(ms, 1);

    // Segment 0: Page Information
    _WritePageInfoSegment(ms, file.Width, file.Height, 0);

    // Segment 1: Immediate Lossless Generic Region (MMR)
    var mmrData = MmrCodec.Encode(file.PixelData, file.Width, file.Height);
    _WriteGenericRegionSegment(ms, file.Width, file.Height, mmrData, 1);

    // Segment 2: End of Page
    _WriteEndOfPageSegment(ms, 2);

    // Segment 3: End of File
    _WriteEndOfFileSegment(ms, 3);

    return ms.ToArray();
  }

  private static void _WritePageInfoSegment(MemoryStream ms, int width, int height, int segmentNumber) {
    // Segment header
    _WriteInt32BE(ms, segmentNumber); // segment number
    ms.WriteByte((byte)Jbig2SegmentType.PageInformation); // flags: type 48, no deferred, 1-byte page assoc
    ms.WriteByte(0x00); // referred-to count = 0
    ms.WriteByte(0x01); // page association = 1

    // Data length: 19 bytes for page info
    _WriteInt32BE(ms, 19);

    // Page info data
    _WriteInt32BE(ms, width);  // page width
    _WriteInt32BE(ms, height); // page height
    _WriteInt32BE(ms, 0);      // X resolution (0 = unspecified)
    _WriteInt32BE(ms, 0);      // Y resolution (0 = unspecified)

    // Page flags (1 byte):
    // bit 0: default pixel value (0 = white)
    // bits 1-2: combination operator (0 = OR)
    // bit 3: requires auxiliary buffer (0 = no)
    // bit 4: override default combination operator (0 = no)
    // bits 5-7: reserved
    ms.WriteByte(0x00);

    // Page striping information (2 bytes): maximum stripe height (0 = no striping, full page)
    ms.WriteByte(0x00);
    ms.WriteByte(0x00);
  }

  private static void _WriteGenericRegionSegment(MemoryStream ms, int width, int height, byte[] mmrData, int segmentNumber) {
    // Segment header
    _WriteInt32BE(ms, segmentNumber); // segment number
    ms.WriteByte((byte)Jbig2SegmentType.ImmediateLosslessGenericRegion); // type 39
    ms.WriteByte(0x00); // referred-to count = 0
    ms.WriteByte(0x01); // page association = 1

    // Data length: 18 bytes region header + MMR data
    var dataLength = 18 + mmrData.Length;
    _WriteInt32BE(ms, dataLength);

    // Region segment information field (17 bytes)
    _WriteInt32BE(ms, width);  // region width
    _WriteInt32BE(ms, height); // region height
    _WriteInt32BE(ms, 0);      // region X location
    _WriteInt32BE(ms, 0);      // region Y location
    ms.WriteByte(0x00);        // combination operator: OR

    // Generic region flags (1 byte):
    // bit 0: 1 = MMR coding
    // bits 1-2: template (0)
    // bit 3: typical prediction (0)
    ms.WriteByte(0x01); // MMR = true

    // MMR compressed data
    ms.Write(mmrData, 0, mmrData.Length);
  }

  private static void _WriteEndOfPageSegment(MemoryStream ms, int segmentNumber) {
    _WriteInt32BE(ms, segmentNumber); // segment number
    ms.WriteByte((byte)Jbig2SegmentType.EndOfPage); // type 49
    ms.WriteByte(0x00); // referred-to count = 0
    ms.WriteByte(0x01); // page association = 1
    _WriteInt32BE(ms, 0); // data length = 0
  }

  private static void _WriteEndOfFileSegment(MemoryStream ms, int segmentNumber) {
    _WriteInt32BE(ms, segmentNumber); // segment number
    ms.WriteByte((byte)Jbig2SegmentType.EndOfFile); // type 51
    ms.WriteByte(0x00); // referred-to count = 0
    ms.WriteByte(0x00); // page association = 0 (file-level)
    _WriteInt32BE(ms, 0); // data length = 0
  }

  private static void _WriteInt32BE(MemoryStream ms, int value) {
    ms.WriteByte((byte)((value >> 24) & 0xFF));
    ms.WriteByte((byte)((value >> 16) & 0xFF));
    ms.WriteByte((byte)((value >> 8) & 0xFF));
    ms.WriteByte((byte)(value & 0xFF));
  }
}
