using System;
using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Text;

namespace FileFormat.Dicom;

/// <summary>Assembles DICOM file bytes from image data (Explicit VR Little Endian).</summary>
public static class DicomWriter {

  /// <summary>Transfer syntax UID for Explicit VR Little Endian.</summary>
  private const string _TRANSFER_SYNTAX_UID = "1.2.840.10008.1.2.1";

  /// <summary>SOP Class UID for Secondary Capture.</summary>
  private const string _SOP_CLASS_UID = "1.2.840.10008.5.1.4.1.1.7";

  /// <summary>Implementation Class UID (fictional, for writing).</summary>
  private const string _IMPL_CLASS_UID = "1.2.826.0.1.3680043.8.1234.1";

  public static byte[] ToBytes(DicomFile file) {
    ArgumentNullException.ThrowIfNull(file);

    using var ms = new MemoryStream();

    // 1. 128-byte preamble (zeros)
    ms.Write(new byte[128]);

    // 2. DICM magic
    ms.Write(Encoding.ASCII.GetBytes("DICM"));

    // 3. Meta information group (0002,xxxx)
    var metaBytes = _BuildMetaGroup();
    // (0002,0000) UL - group length
    _WriteTag(ms, 0x0002, 0x0000, "UL", _UInt32Bytes((uint)metaBytes.Length));
    ms.Write(metaBytes);

    // 4. Image tags
    // (0028,0002) SamplesPerPixel
    _WriteTag(ms, 0x0028, 0x0002, "US", _UInt16Bytes((ushort)file.SamplesPerPixel));

    // (0028,0004) PhotometricInterpretation
    _WriteTag(ms, 0x0028, 0x0004, "CS", _PadString(_PhotometricToString(file.PhotometricInterpretation)));

    // (0028,0010) Rows
    _WriteTag(ms, 0x0028, 0x0010, "US", _UInt16Bytes((ushort)file.Height));

    // (0028,0011) Columns
    _WriteTag(ms, 0x0028, 0x0011, "US", _UInt16Bytes((ushort)file.Width));

    // (0028,0100) BitsAllocated
    _WriteTag(ms, 0x0028, 0x0100, "US", _UInt16Bytes((ushort)file.BitsAllocated));

    // (0028,0101) BitsStored
    _WriteTag(ms, 0x0028, 0x0101, "US", _UInt16Bytes((ushort)file.BitsStored));

    // (0028,0102) HighBit
    _WriteTag(ms, 0x0028, 0x0102, "US", _UInt16Bytes((ushort)(file.BitsStored - 1)));

    // (0028,0103) PixelRepresentation (0 = unsigned)
    _WriteTag(ms, 0x0028, 0x0103, "US", _UInt16Bytes(0));

    // (0028,1050) WindowCenter
    if (file.WindowCenter != 0.0 || file.WindowWidth != 0.0) {
      _WriteTag(ms, 0x0028, 0x1050, "DS", _PadString(file.WindowCenter.ToString(CultureInfo.InvariantCulture)));
      _WriteTag(ms, 0x0028, 0x1051, "DS", _PadString(file.WindowWidth.ToString(CultureInfo.InvariantCulture)));
    }

    // (7FE0,0010) PixelData
    _WriteTag(ms, 0x7FE0, 0x0010, "OW", file.PixelData);

    return ms.ToArray();
  }

  private static byte[] _BuildMetaGroup() {
    using var ms = new MemoryStream();

    // (0002,0001) OB - File Meta Information Version
    _WriteTag(ms, 0x0002, 0x0001, "OB", [0x00, 0x01]);

    // (0002,0002) UI - Media Storage SOP Class UID
    _WriteTag(ms, 0x0002, 0x0002, "UI", _PadUid(_SOP_CLASS_UID));

    // (0002,0003) UI - Media Storage SOP Instance UID
    _WriteTag(ms, 0x0002, 0x0003, "UI", _PadUid("1.2.3.4.5.6.7.8.9"));

    // (0002,0010) UI - Transfer Syntax UID
    _WriteTag(ms, 0x0002, 0x0010, "UI", _PadUid(_TRANSFER_SYNTAX_UID));

    // (0002,0012) UI - Implementation Class UID
    _WriteTag(ms, 0x0002, 0x0012, "UI", _PadUid(_IMPL_CLASS_UID));

    return ms.ToArray();
  }

  private static readonly string[] _LongVRs = ["OB", "OD", "OF", "OL", "OW", "SQ", "UC", "UN", "UR", "UT"];

  private static void _WriteTag(MemoryStream ms, ushort group, ushort element, string vr, byte[] value) {
    // Tag: group (2 LE) + element (2 LE)
    Span<byte> tagBuf = stackalloc byte[4];
    BinaryPrimitives.WriteUInt16LittleEndian(tagBuf, group);
    BinaryPrimitives.WriteUInt16LittleEndian(tagBuf[2..], element);
    ms.Write(tagBuf);

    // VR (2 ASCII bytes)
    ms.Write(Encoding.ASCII.GetBytes(vr));

    var isLongVr = Array.IndexOf(_LongVRs, vr) >= 0;
    if (isLongVr) {
      // 2-byte reserved + 4-byte length
      _WriteUInt16LE(ms, 0);
      _WriteUInt32LE(ms, (uint)value.Length);
    } else {
      // 2-byte length
      _WriteUInt16LE(ms, (ushort)value.Length);
    }

    ms.Write(value);
  }

  private static byte[] _UInt16Bytes(ushort value) {
    var buf = new byte[2];
    BinaryPrimitives.WriteUInt16LittleEndian(buf, value);
    return buf;
  }

  private static byte[] _UInt32Bytes(uint value) {
    var buf = new byte[4];
    BinaryPrimitives.WriteUInt32LittleEndian(buf, value);
    return buf;
  }

  private static byte[] _PadString(string value) {
    var bytes = Encoding.ASCII.GetBytes(value);
    if (bytes.Length % 2 != 0) {
      var padded = new byte[bytes.Length + 1];
      bytes.AsSpan(0, bytes.Length).CopyTo(padded);
      padded[^1] = (byte)' ';
      return padded;
    }

    return bytes;
  }

  private static byte[] _PadUid(string uid) {
    var bytes = Encoding.ASCII.GetBytes(uid);
    if (bytes.Length % 2 != 0) {
      var padded = new byte[bytes.Length + 1];
      bytes.AsSpan(0, bytes.Length).CopyTo(padded);
      padded[^1] = 0; // UIDs are padded with null
      return padded;
    }

    return bytes;
  }

  private static string _PhotometricToString(DicomPhotometricInterpretation value) => value switch {
    DicomPhotometricInterpretation.Monochrome1 => "MONOCHROME1",
    DicomPhotometricInterpretation.Monochrome2 => "MONOCHROME2",
    DicomPhotometricInterpretation.Rgb => "RGB",
    DicomPhotometricInterpretation.PaletteColor => "PALETTE COLOR",
    _ => "MONOCHROME2"
  };

  private static void _WriteUInt16LE(MemoryStream ms, ushort value) {
    Span<byte> buf = stackalloc byte[2];
    BinaryPrimitives.WriteUInt16LittleEndian(buf, value);
    ms.Write(buf);
  }

  private static void _WriteUInt32LE(MemoryStream ms, uint value) {
    Span<byte> buf = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32LittleEndian(buf, value);
    ms.Write(buf);
  }
}
