using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace FileFormat.Heif;

/// <summary>Assembles HEIF/HEIC file bytes from an in-memory representation.</summary>
public static class HeifWriter {

  public static byte[] ToBytes(HeifFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var imageData = file.RawImageData.Length > 0 ? file.RawImageData : file.PixelData;
    var brand = string.IsNullOrEmpty(file.Brand) ? "heic" : file.Brand;

    var ftypBox = _BuildFtypBox(brand);
    var metaBox = _BuildMetaBox(file.Width, file.Height, imageData.Length);
    var mdatBox = IsoBmffBox.WriteBox(IsoBmffBox.Mdat, imageData);

    // Assemble all boxes sequentially
    var totalSize = ftypBox.Length + metaBox.Length + mdatBox.Length;
    var result = new byte[totalSize];
    var offset = 0;

    ftypBox.AsSpan(0, ftypBox.Length).CopyTo(result.AsSpan(offset));
    offset += ftypBox.Length;

    metaBox.AsSpan(0, metaBox.Length).CopyTo(result.AsSpan(offset));
    offset += metaBox.Length;

    mdatBox.AsSpan(0, mdatBox.Length).CopyTo(result.AsSpan(offset));

    return result;
  }

  private static byte[] _BuildFtypBox(string brand) {
    // ftyp: major_brand (4) + minor_version (4) + compatible_brands (4 each)
    var compatibleBrands = new[] { brand, "mif1" };
    var dataLen = 4 + 4 + compatibleBrands.Length * 4;
    var data = new byte[dataLen];

    Encoding.ASCII.GetBytes(brand, 0, 4, data, 0);
    // minor_version = 0 (already zeroed)

    var offset = 8;
    foreach (var cb in compatibleBrands) {
      Encoding.ASCII.GetBytes(cb, 0, 4, data, offset);
      offset += 4;
    }

    return IsoBmffBox.WriteBox(IsoBmffBox.Ftyp, data);
  }

  private static byte[] _BuildMetaBox(int width, int height, int imageDataLength) {
    // meta is a FullBox: content = sub-boxes
    var hdlr = _BuildHdlrBox();
    var pitm = _BuildPitmBox();
    var iprp = _BuildIprpBox(width, height);
    var iloc = _BuildIlocBox(imageDataLength);

    var innerLength = hdlr.Length + pitm.Length + iprp.Length + iloc.Length;
    var innerData = new byte[innerLength];
    var offset = 0;

    hdlr.AsSpan(0, hdlr.Length).CopyTo(innerData.AsSpan(offset));
    offset += hdlr.Length;
    pitm.AsSpan(0, pitm.Length).CopyTo(innerData.AsSpan(offset));
    offset += pitm.Length;
    iprp.AsSpan(0, iprp.Length).CopyTo(innerData.AsSpan(offset));
    offset += iprp.Length;
    iloc.AsSpan(0, iloc.Length).CopyTo(innerData.AsSpan(offset));

    return IsoBmffBox.WriteFullBox(IsoBmffBox.Meta, 0, 0, innerData);
  }

  private static byte[] _BuildHdlrBox() {
    // hdlr FullBox: version(1)+flags(3) already handled by WriteFullBox
    // pre_defined (4) + handler_type (4) + reserved (12) + name (null-terminated)
    var handlerType = "pict";
    var name = "HEIF Image Handler\0";
    var nameBytes = Encoding.ASCII.GetBytes(name);
    var dataLen = 4 + 4 + 12 + nameBytes.Length;
    var data = new byte[dataLen];
    // pre_defined = 0 (zeroed)
    Encoding.ASCII.GetBytes(handlerType, 0, 4, data, 4);
    // reserved = 0 (zeroed)
    nameBytes.AsSpan(0, nameBytes.Length).CopyTo(data.AsSpan(20));

    return IsoBmffBox.WriteFullBox(IsoBmffBox.Hdlr, 0, 0, data);
  }

  private static byte[] _BuildPitmBox() {
    // pitm FullBox version 0: item_ID (2 bytes)
    var data = new byte[2];
    BinaryPrimitives.WriteUInt16BigEndian(data, 1); // primary item ID = 1
    return IsoBmffBox.WriteFullBox(IsoBmffBox.Pitm, 0, 0, data);
  }

  private static byte[] _BuildIprpBox(int width, int height) {
    // iprp is a plain box containing ipco + ipma
    var ipco = _BuildIpcoBox(width, height);
    var ipma = _BuildIpmaBox();

    var data = new byte[ipco.Length + ipma.Length];
    ipco.AsSpan(0, ipco.Length).CopyTo(data.AsSpan(0));
    ipma.AsSpan(0, ipma.Length).CopyTo(data.AsSpan(ipco.Length));

    return IsoBmffBox.WriteBox(IsoBmffBox.Iprp, data);
  }

  private static byte[] _BuildIpcoBox(int width, int height) {
    // ipco is a plain box containing property boxes (e.g. ispe)
    var ispe = _BuildIspeBox(width, height);
    return IsoBmffBox.WriteBox(IsoBmffBox.Ipco, ispe);
  }

  private static byte[] _BuildIspeBox(int width, int height) {
    // ispe FullBox: version(1)+flags(3) + width(4) + height(4)
    var data = new byte[8];
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(0), (uint)width);
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4), (uint)height);
    return IsoBmffBox.WriteFullBox(IsoBmffBox.Ispe, 0, 0, data);
  }

  private static byte[] _BuildIpmaBox() {
    // ipma FullBox version 0, flags 0:
    // entry_count (4) + entries
    // entry: item_ID (2) + association_count (1) + associations
    // association (flags bit 0 = 0 -> 1 byte): essential(1 bit) + property_index(7 bits)
    var data = new byte[4 + 2 + 1 + 1];
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(0), 1); // 1 entry
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(4), 1); // item_ID = 1
    data[6] = 1; // association_count = 1
    data[7] = 0x80 | 1; // essential=1, property_index=1 (ispe is property 1)

    return IsoBmffBox.WriteFullBox(IsoBmffBox.Ipma, 0, 0, data);
  }

  private static byte[] _BuildIlocBox(int imageDataLength) {
    // iloc FullBox version 0:
    // offset_size/length_size (1 byte): each nibble, we use 4 bytes for both
    // base_offset_size/reserved (1 byte): 0
    // item_count (2 bytes)
    // per item: item_ID (2) + data_reference_index (2) + base_offset (0) + extent_count (2)
    //   per extent: extent_offset (4) + extent_length (4)

    // We set extent_offset to 0 -- a relative offset within mdat data area.
    // The offset is relative to the start of mdat data (after mdat header).
    var data = new byte[2 + 2 + 2 + 2 + 0 + 2 + 4 + 4];
    data[0] = 0x44; // offset_size=4, length_size=4
    data[1] = 0x00; // base_offset_size=0, reserved=0
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(2), 1); // item_count = 1
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(4), 1); // item_ID = 1
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(6), 0); // data_reference_index = 0
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(8), 1); // extent_count = 1
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(10), 0); // extent_offset = 0
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(14), (uint)imageDataLength); // extent_length

    return IsoBmffBox.WriteFullBox(IsoBmffBox.Iloc, 0, 0, data);
  }
}
