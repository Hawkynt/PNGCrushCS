using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Avif;

/// <summary>Assembles AVIF file bytes from an <see cref="AvifFile"/>.</summary>
public static class AvifWriter {

  public static byte[] ToBytes(AvifFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return _Assemble(file);
  }

  private static byte[] _Assemble(AvifFile file) {
    var parts = new List<byte[]>();

    parts.Add(_BuildFtypBox(file.Brand));
    parts.Add(_BuildMetaBox(file.Width, file.Height));

    var imageData = file.RawImageData.Length > 0 ? file.RawImageData : file.PixelData;
    parts.Add(IsoBmffBox.BuildBox(IsoBmffBox.Mdat, imageData));

    var totalSize = 0;
    foreach (var part in parts)
      totalSize += part.Length;

    var result = new byte[totalSize];
    var offset = 0;
    foreach (var part in parts) {
      part.AsSpan(0, part.Length).CopyTo(result.AsSpan(offset));
      offset += part.Length;
    }

    return result;
  }

  private static byte[] _BuildFtypBox(string brand) {
    // ftyp payload: major_brand(4) + minor_version(4) + compatible_brands(4+)
    var payload = new byte[12];
    _WriteFourCC(payload, 0, brand);
    // minor_version = 0
    _WriteFourCC(payload, 8, "avif");
    return IsoBmffBox.BuildBox(IsoBmffBox.Ftyp, payload);
  }

  private static byte[] _BuildMetaBox(int width, int height) {
    // meta is a full box: version(1) + flags(3) = 4 bytes before children
    var hdlrBox = _BuildHdlrBox();
    var pitmBox = _BuildPitmBox();
    var iinfBox = _BuildIinfBox();
    var iprpBox = _BuildIprpBox(width, height);
    var ilocBox = _BuildIlocBox();

    var childrenSize = hdlrBox.Length + pitmBox.Length + iinfBox.Length + iprpBox.Length + ilocBox.Length;
    var payload = new byte[4 + childrenSize]; // 4 bytes for full box header
    var offset = 4; // skip version/flags (zeros)

    hdlrBox.AsSpan(0, hdlrBox.Length).CopyTo(payload.AsSpan(offset));
    offset += hdlrBox.Length;

    pitmBox.AsSpan(0, pitmBox.Length).CopyTo(payload.AsSpan(offset));
    offset += pitmBox.Length;

    iinfBox.AsSpan(0, iinfBox.Length).CopyTo(payload.AsSpan(offset));
    offset += iinfBox.Length;

    iprpBox.AsSpan(0, iprpBox.Length).CopyTo(payload.AsSpan(offset));
    offset += iprpBox.Length;

    ilocBox.AsSpan(0, ilocBox.Length).CopyTo(payload.AsSpan(offset));

    return IsoBmffBox.BuildBox(IsoBmffBox.Meta, payload);
  }

  private static byte[] _BuildHdlrBox() {
    // hdlr full box: version(1) + flags(3) + pre_defined(4) + handler_type(4) + reserved(12) + name(1)
    var payload = new byte[4 + 4 + 4 + 12 + 1];
    // version/flags = 0 (first 4 bytes)
    // pre_defined = 0 (bytes 4-7)
    _WriteFourCC(payload, 8, "pict"); // handler_type
    // reserved = 0 (bytes 12-23)
    // name = null terminator (byte 24)
    return IsoBmffBox.BuildBox(IsoBmffBox.Hdlr, payload);
  }

  private static byte[] _BuildPitmBox() {
    // pitm full box: version(1) + flags(3) + item_ID(2)
    var payload = new byte[6];
    // version = 0, flags = 0
    BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(4), 1); // item_ID = 1
    return IsoBmffBox.BuildBox(IsoBmffBox.Pitm, payload);
  }

  private static byte[] _BuildIinfBox() {
    // iinf full box: version(1) + flags(3) + entry_count(2) + infe box(es)
    var infePayload = new byte[4 + 2 + 4 + 5]; // version/flags + item_ID + item_protection_index + item_type + null name
    // version = 2 for infe
    infePayload[0] = 2;
    BinaryPrimitives.WriteUInt16BigEndian(infePayload.AsSpan(4), 1); // item_ID = 1
    // item_protection_index = 0
    _WriteFourCC(infePayload, 8, "av01"); // item_type
    // name = "Image\0" but we use just null
    infePayload[12] = 0;

    var infeBox = IsoBmffBox.BuildBox(IsoBmffBox.Infe, infePayload);

    var payload = new byte[4 + 2 + infeBox.Length];
    // version = 0, flags = 0
    BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(4), 1); // entry_count = 1
    infeBox.AsSpan(0, infeBox.Length).CopyTo(payload.AsSpan(6));

    return IsoBmffBox.BuildBox(IsoBmffBox.Iinf, payload);
  }

  private static byte[] _BuildIprpBox(int width, int height) {
    var ipcoBox = _BuildIpcoBox(width, height);
    var ipmaBox = _BuildIpmaBox();

    var payload = new byte[ipcoBox.Length + ipmaBox.Length];
    ipcoBox.AsSpan(0, ipcoBox.Length).CopyTo(payload.AsSpan(0));
    ipmaBox.AsSpan(0, ipmaBox.Length).CopyTo(payload.AsSpan(ipcoBox.Length));

    return IsoBmffBox.BuildBox(IsoBmffBox.Iprp, payload);
  }

  private static byte[] _BuildIpcoBox(int width, int height) {
    var ispeBox = _BuildIspeBox(width, height);
    return IsoBmffBox.BuildBox(IsoBmffBox.Ipco, ispeBox);
  }

  private static byte[] _BuildIspeBox(int width, int height) {
    // ispe full box: version(1) + flags(3) + width(4) + height(4)
    var payload = new byte[12];
    // version = 0, flags = 0
    BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(4), (uint)width);
    BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(8), (uint)height);
    return IsoBmffBox.BuildBox(IsoBmffBox.Ispe, payload);
  }

  private static byte[] _BuildIpmaBox() {
    // ipma full box: version(1) + flags(3) + entry_count(4) + item_ID(2) + association_count(1) + association(1)
    var payload = new byte[4 + 4 + 2 + 1 + 1];
    // version = 0, flags = 0
    BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(4), 1); // entry_count = 1
    BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(8), 1); // item_ID = 1
    payload[10] = 1; // association_count = 1
    payload[11] = 0x81; // essential(1) + property_index(1) = 1 with essential bit set
    return IsoBmffBox.BuildBox(IsoBmffBox.Ipma, payload);
  }

  private static byte[] _BuildIlocBox() {
    // iloc full box: version(1) + flags(3) + offset_size/length_size(1) + base_offset_size/index_size(1) + item_count(2)
    // For simplicity, version 0 with no items pointing to external data
    var payload = new byte[4 + 1 + 1 + 2];
    // version = 0, flags = 0
    payload[4] = 0x44; // offset_size=4, length_size=4
    payload[5] = 0x00; // base_offset_size=0, index_size=0 (v0 has no index_size)
    // item_count = 0 (the mdat is self-contained in our simple format)
    return IsoBmffBox.BuildBox(IsoBmffBox.Iloc, payload);
  }

  private static void _WriteFourCC(byte[] buffer, int offset, string fourCC) {
    buffer[offset] = (byte)fourCC[0];
    buffer[offset + 1] = (byte)fourCC[1];
    buffer[offset + 2] = (byte)fourCC[2];
    buffer[offset + 3] = (byte)fourCC[3];
  }
}
