using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using FileFormat.Codecs.H265;
using FileFormat.Core;

namespace FileFormat.Heif;

/// <summary>Writes a single-image HEIF/HEIC file containing an HEVC Main-Still-Picture item.</summary>
public static class HeifWriter {

  public static byte[] ToBytes(HeifFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.Width <= 0 || file.Height <= 0)
      throw new ArgumentOutOfRangeException(nameof(file), "HEIF requires positive image dimensions.");
    if (file.PixelData == null || file.PixelData.Length < checked(file.Width * file.Height * 3))
      throw new ArgumentException("HEIF writing requires a complete RGB24 PixelData raster.", nameof(file));

    var source = new RawImage {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData,
    };
    var encoded = H265PcmStillCodec.Encode(source);

    var brand = _HeifBrand(file.Brand);
    var ftyp = _BuildFtypBox(brand);

    // iloc construction method zero uses an absolute file offset. The field is always four bytes,
    // so building meta once with a dummy offset tells us its final size; rebuilding with the real
    // mdat payload offset cannot change that size.
    var meta = _BuildMetaBox(
      file.Width, file.Height,
      encoded.HevcDisplayWidth, encoded.HevcDisplayHeight,
      encoded.DecoderConfiguration, encoded.Sample.Length, 0);
    var mdatPayloadOffset = checked((uint)(ftyp.Length + meta.Length + 8));
    meta = _BuildMetaBox(
      file.Width, file.Height,
      encoded.HevcDisplayWidth, encoded.HevcDisplayHeight,
      encoded.DecoderConfiguration, encoded.Sample.Length, mdatPayloadOffset);
    var mdat = IsoBmffBox.WriteBox(IsoBmffBox.Mdat, encoded.Sample);

    var result = new byte[checked(ftyp.Length + meta.Length + mdat.Length)];
    var at = 0;
    ftyp.CopyTo(result, at);
    at += ftyp.Length;
    meta.CopyTo(result, at);
    at += meta.Length;
    mdat.CopyTo(result, at);
    return result;
  }

  private static string _HeifBrand(string? requested)
    => requested is "heic" or "heix" or "hevc" or "heim" or "heis" or "hevm" or "hevs" or "mif1"
      ? requested
      : "heic";

  private static byte[] _BuildFtypBox(string brand) {
    // HEIF image sequence constraints are not used here: this is one primary image item.
    string[] compatible = brand == "mif1" ? ["mif1", "heic"] : [brand, "mif1"];
    var data = new byte[8 + compatible.Length * 4];
    Encoding.ASCII.GetBytes(brand, 0, 4, data, 0);
    var at = 8; // minor_version is zero
    foreach (var item in compatible) {
      Encoding.ASCII.GetBytes(item, 0, 4, data, at);
      at += 4;
    }
    return IsoBmffBox.WriteBox(IsoBmffBox.Ftyp, data);
  }

  private static byte[] _BuildMetaBox(
    int logicalWidth,
    int logicalHeight,
    int hevcWidth,
    int hevcHeight,
    byte[] hvcc,
    int sampleLength,
    uint mdatPayloadOffset
  ) {
    var children = new[] {
      _BuildHdlrBox(),
      _BuildPitmBox(),
      _BuildIinfBox(),
      _BuildIlocBox(sampleLength, mdatPayloadOffset),
      _BuildIprpBox(logicalWidth, logicalHeight, hevcWidth, hevcHeight, hvcc),
    };

    var size = 0;
    foreach (var child in children)
      size = checked(size + child.Length);
    var data = new byte[size];
    var at = 0;
    foreach (var child in children) {
      child.CopyTo(data, at);
      at += child.Length;
    }
    return IsoBmffBox.WriteFullBox(IsoBmffBox.Meta, 0, 0, data);
  }

  private static byte[] _BuildHdlrBox() {
    var name = Encoding.ASCII.GetBytes("HEVC still image\0");
    var data = new byte[20 + name.Length];
    Encoding.ASCII.GetBytes("pict", 0, 4, data, 4);
    name.CopyTo(data, 20);
    return IsoBmffBox.WriteFullBox(IsoBmffBox.Hdlr, 0, 0, data);
  }

  private static byte[] _BuildPitmBox() {
    var data = new byte[2];
    BinaryPrimitives.WriteUInt16BigEndian(data, 1);
    return IsoBmffBox.WriteFullBox(IsoBmffBox.Pitm, 0, 0, data);
  }

  private static byte[] _BuildIinfBox() {
    // ItemInfoEntry version 2 is the first form that carries a four-character item_type.
    var infeData = new byte[2 + 2 + 4 + 1];
    BinaryPrimitives.WriteUInt16BigEndian(infeData.AsSpan(0, 2), 1); // item_ID
    BinaryPrimitives.WriteUInt16BigEndian(infeData.AsSpan(2, 2), 0); // protection index
    Encoding.ASCII.GetBytes("hvc1", 0, 4, infeData, 4);
    infeData[8] = 0; // empty item_name
    var infe = IsoBmffBox.WriteFullBox("infe", 2, 0, infeData);

    var iinfData = new byte[2 + infe.Length];
    BinaryPrimitives.WriteUInt16BigEndian(iinfData.AsSpan(0, 2), 1);
    infe.CopyTo(iinfData, 2);
    return IsoBmffBox.WriteFullBox("iinf", 0, 0, iinfData);
  }

  private static byte[] _BuildIlocBox(int sampleLength, uint baseOffset) {
    // version 0, four-byte base/extent offsets and lengths, one extent in the file's mdat payload.
    var data = new byte[2 + 2 + 2 + 2 + 4 + 2 + 4 + 4];
    data[0] = 0x44; // offset_size=4, length_size=4
    data[1] = 0x40; // base_offset_size=4
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(2, 2), 1); // item_count
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(4, 2), 1); // item_ID
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(6, 2), 0); // data_reference_index
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8, 4), baseOffset);
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(12, 2), 1); // extent_count
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(14, 4), 0); // extent_offset
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(18, 4), checked((uint)sampleLength));
    return IsoBmffBox.WriteFullBox(IsoBmffBox.Iloc, 0, 0, data);
  }

  private static byte[] _BuildIprpBox(
    int logicalWidth,
    int logicalHeight,
    int hevcWidth,
    int hevcHeight,
    byte[] hvcc
  ) {
    var properties = new List<byte[]> {
      IsoBmffBox.WriteBox(IsoBmffBox.HvcC, hvcc),
      _BuildIspeBox(hevcWidth, hevcHeight),
      _BuildPixiBox(),
    };

    var hasCleanAperture = logicalWidth != hevcWidth || logicalHeight != hevcHeight;
    if (hasCleanAperture)
      properties.Add(_BuildClapBox(logicalWidth, logicalHeight, hevcWidth, hevcHeight));

    var propertyBytes = 0;
    foreach (var property in properties)
      propertyBytes = checked(propertyBytes + property.Length);
    var ipcoData = new byte[propertyBytes];
    var at = 0;
    foreach (var property in properties) {
      property.CopyTo(ipcoData, at);
      at += property.Length;
    }
    var ipco = IsoBmffBox.WriteBox(IsoBmffBox.Ipco, ipcoData);

    var associationCount = properties.Count;
    var ipmaData = new byte[4 + 2 + 1 + associationCount];
    BinaryPrimitives.WriteUInt32BigEndian(ipmaData.AsSpan(0, 4), 1);
    BinaryPrimitives.WriteUInt16BigEndian(ipmaData.AsSpan(4, 2), 1);
    ipmaData[6] = checked((byte)associationCount);
    for (var i = 0; i < associationCount; ++i)
      ipmaData[7 + i] = checked((byte)(0x80 | (i + 1)));
    var ipma = IsoBmffBox.WriteFullBox(IsoBmffBox.Ipma, 0, 0, ipmaData);

    var result = new byte[ipco.Length + ipma.Length];
    ipco.CopyTo(result, 0);
    ipma.CopyTo(result, ipco.Length);
    return IsoBmffBox.WriteBox(IsoBmffBox.Iprp, result);
  }

  private static byte[] _BuildIspeBox(int width, int height) {
    var data = new byte[8];
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(0, 4), checked((uint)width));
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4, 4), checked((uint)height));
    return IsoBmffBox.WriteFullBox(IsoBmffBox.Ispe, 0, 0, data);
  }

  private static byte[] _BuildPixiBox() {
    byte[] data = [3, 8, 8, 8];
    return IsoBmffBox.WriteFullBox("pixi", 0, 0, data);
  }

  private static byte[] _BuildClapBox(int width, int height, int codedWidth, int codedHeight) {
    var data = new byte[32];
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(0, 4), checked((uint)width));
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4, 4), 1);
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8, 4), checked((uint)height));
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(12, 4), 1);

    // Crop only the duplicated right/bottom edge. The clean-aperture offset is measured from the
    // coded centre, hence -1/2 when exactly one sample is removed from the positive side.
    var horizontalOddCrop = codedWidth - width;
    var verticalOddCrop = codedHeight - height;
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(16, 4), horizontalOddCrop == 1 ? -1 : 0);
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(20, 4), horizontalOddCrop == 1 ? 2u : 1u);
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(24, 4), verticalOddCrop == 1 ? -1 : 0);
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(28, 4), verticalOddCrop == 1 ? 2u : 1u);
    return IsoBmffBox.WriteBox(IsoBmffBox.Clap, data);
  }
}
