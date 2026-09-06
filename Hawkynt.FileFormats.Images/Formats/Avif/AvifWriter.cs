using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using FileFormat.Avif.Codec;

namespace FileFormat.Avif;

/// <summary>Assembles AVIF file bytes from an <see cref="AvifFile"/>.</summary>
/// <remarks>
/// The picture is coded as a lossless AV1 key frame and wrapped in the item structure AVIF
/// requires: a primary <c>av01</c> item whose bytes live in <c>mdat</c>, described by <c>av1C</c>,
/// <c>ispe</c>, <c>pixi</c> and <c>colr</c>. Everything about that structure is load-bearing — a
/// file missing <c>av1C</c> is not AVIF, whatever its extension says, and no other reader will open
/// it.
/// </remarks>
public static class AvifWriter {

  private const uint _PRIMARY_ITEM_ID = 1;

  public static byte[] ToBytes(AvifFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.Width <= 0 || file.Height <= 0)
      throw new ArgumentException("AVIF: the picture must have a positive size.", nameof(file));

    if (file.HasAlpha)
      throw new NotSupportedException(
        "AVIF: writing an alpha channel is not supported. AVIF carries alpha as a second coded item "
        + "with its own auxiliary type, and this writer emits the colour item only.");

    var pixels = file.PixelData ?? [];
    var expected = file.Width * file.Height * 3;
    if (pixels.Length < expected)
      throw new ArgumentException(
        $"AVIF: the pixel buffer holds {pixels.Length} bytes, which is short of the {expected} a "
        + $"{file.Width}x{file.Height} picture needs.", nameof(file));

    var coded = Av1StillPictureEncoder.Encode(pixels, file.Width, file.Height);

    var ftyp = _BuildFtypBox();
    var meta = _BuildMetaBox(file.Width, file.Height, coded.Length, out var extentOffsetPosition);
    var mdat = IsoBmffBox.BuildBox(IsoBmffBox.Mdat, coded);

    var result = new byte[ftyp.Length + meta.Length + mdat.Length];
    ftyp.CopyTo(result, 0);
    meta.CopyTo(result, ftyp.Length);
    mdat.CopyTo(result, ftyp.Length + meta.Length);

    // The item location is an absolute file offset, so it can only be filled in once the boxes in
    // front of the media data have their final size.
    var payloadOffset = ftyp.Length + meta.Length + IsoBmffBox.HeaderSize;
    BinaryPrimitives.WriteUInt32BigEndian(
      result.AsSpan(ftyp.Length + extentOffsetPosition), (uint)payloadOffset);

    return result;
  }

  private static byte[] _BuildFtypBox() {
    // major_brand "avif", minor_version 0, then the brands a reader may rely on. "mif1" and "miaf"
    // say the file follows the image-item structure; "av01" that the codec is AV1.
    var payload = new byte[4 + 4 + 4 * 4];
    _WriteFourCc(payload, 0, "avif");
    _WriteFourCc(payload, 8, "avif");
    _WriteFourCc(payload, 12, "mif1");
    _WriteFourCc(payload, 16, "miaf");
    _WriteFourCc(payload, 20, "av01");
    return IsoBmffBox.BuildBox(IsoBmffBox.Ftyp, payload);
  }

  private static byte[] _BuildMetaBox(int width, int height, int codedLength, out int extentOffsetPosition) {
    var hdlr = _BuildHdlrBox();
    var pitm = _BuildPitmBox();
    var iloc = _BuildIlocBox(codedLength, out var offsetWithinIloc);
    var iinf = _BuildIinfBox();
    var iprp = _BuildIprpBox(width, height);

    var payload = new List<byte> { 0, 0, 0, 0 }; // meta is a full box: version and flags
    var ilocStart = 4 + hdlr.Length + pitm.Length;
    payload.AddRange(hdlr);
    payload.AddRange(pitm);
    payload.AddRange(iloc);
    payload.AddRange(iinf);
    payload.AddRange(iprp);

    extentOffsetPosition = IsoBmffBox.HeaderSize + ilocStart + offsetWithinIloc;
    return IsoBmffBox.BuildBox(IsoBmffBox.Meta, payload.ToArray());
  }

  private static byte[] _BuildHdlrBox() {
    var payload = new byte[4 + 4 + 4 + 12 + 1];
    _WriteFourCc(payload, 8, "pict");
    return IsoBmffBox.BuildBox(IsoBmffBox.Hdlr, payload);
  }

  private static byte[] _BuildPitmBox() {
    var payload = new byte[6];
    BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(4), (ushort)_PRIMARY_ITEM_ID);
    return IsoBmffBox.BuildBox(IsoBmffBox.Pitm, payload);
  }

  private static byte[] _BuildIinfBox() {
    // infe version 2: item_ID(2), item_protection_index(2), item_type(4), item_name (empty string).
    var infePayload = new byte[4 + 2 + 2 + 4 + 1];
    infePayload[0] = 2;
    BinaryPrimitives.WriteUInt16BigEndian(infePayload.AsSpan(4), (ushort)_PRIMARY_ITEM_ID);
    _WriteFourCc(infePayload, 8, "av01");
    var infe = IsoBmffBox.BuildBox(IsoBmffBox.Infe, infePayload);

    var payload = new byte[4 + 2 + infe.Length];
    BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(4), 1); // entry_count
    infe.CopyTo(payload, 6);
    return IsoBmffBox.BuildBox(IsoBmffBox.Iinf, payload);
  }

  private static byte[] _BuildIlocBox(int codedLength, out int extentOffsetPosition) {
    // Version 0, offset_size 4, length_size 4, base_offset_size 0: one item, one extent.
    var payload = new byte[4 + 1 + 1 + 2 + 2 + 2 + 2 + 4 + 4];
    payload[4] = 0x44; // offset_size = 4, length_size = 4
    payload[5] = 0x00; // base_offset_size = 0
    BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(6), 1); // item_count
    BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(8), (ushort)_PRIMARY_ITEM_ID);
    BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(10), 0); // data_reference_index
    BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(12), 1); // extent_count
    // extent_offset is filled in once the surrounding boxes are sized.
    BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(18), (uint)codedLength);

    extentOffsetPosition = IsoBmffBox.HeaderSize + 14;
    return IsoBmffBox.BuildBox(IsoBmffBox.Iloc, payload);
  }

  private static byte[] _BuildIprpBox(int width, int height) {
    var ispe = _BuildIspeBox(width, height);
    var pixi = _BuildPixiBox();
    var av1C = _BuildAv1CBox();
    var colr = _BuildColrBox();

    var ipcoPayload = new List<byte>();
    ipcoPayload.AddRange(ispe);
    ipcoPayload.AddRange(pixi);
    ipcoPayload.AddRange(av1C);
    ipcoPayload.AddRange(colr);
    var ipco = IsoBmffBox.BuildBox(IsoBmffBox.Ipco, ipcoPayload.ToArray());

    // ipma: one entry, four associations. av1C and ispe are essential properties.
    var ipmaPayload = new byte[4 + 4 + 2 + 1 + 4];
    BinaryPrimitives.WriteUInt32BigEndian(ipmaPayload.AsSpan(4), 1); // entry_count
    BinaryPrimitives.WriteUInt16BigEndian(ipmaPayload.AsSpan(8), (ushort)_PRIMARY_ITEM_ID);
    ipmaPayload[10] = 4; // association_count
    ipmaPayload[11] = 0x81; // ispe, essential
    ipmaPayload[12] = 0x02; // pixi
    ipmaPayload[13] = 0x83; // av1C, essential
    ipmaPayload[14] = 0x04; // colr
    var ipma = IsoBmffBox.BuildBox(IsoBmffBox.Ipma, ipmaPayload);

    var payload = new byte[ipco.Length + ipma.Length];
    ipco.CopyTo(payload, 0);
    ipma.CopyTo(payload, ipco.Length);
    return IsoBmffBox.BuildBox(IsoBmffBox.Iprp, payload);
  }

  private static byte[] _BuildIspeBox(int width, int height) {
    var payload = new byte[12];
    BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(4), (uint)width);
    BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(8), (uint)height);
    return IsoBmffBox.BuildBox(IsoBmffBox.Ispe, payload);
  }

  private static byte[] _BuildPixiBox() {
    var payload = new byte[4 + 1 + 3];
    payload[4] = 3; // num_channels
    payload[5] = 8;
    payload[6] = 8;
    payload[7] = 8;
    return IsoBmffBox.BuildBox(IsoBmffBox.Pixi, payload);
  }

  private static byte[] _BuildAv1CBox() {
    // AV1CodecConfigurationRecord: it repeats the sequence header's profile and colour layout so a
    // reader can size buffers without decoding, and must agree with the bitstream exactly.
    var payload = new byte[4];
    payload[0] = 0x81; // marker = 1, version = 1
    payload[1] = (1 << 5) | 31; // seq_profile = 1, seq_level_idx_0 = 31
    payload[2] = 0x00; // tier 0, 8-bit, colour, 4:4:4, chroma_sample_position unknown
    payload[3] = 0x00; // no initial presentation delay
    return IsoBmffBox.BuildBox(IsoBmffBox.Av1C, payload);
  }

  private static byte[] _BuildColrBox() {
    var payload = new byte[4 + 2 + 2 + 2 + 1];
    _WriteFourCc(payload, 0, "nclx");
    BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(4), (ushort)Av1ColorPrimaries.Bt709);
    BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(6), (ushort)Av1TransferCharacteristics.Srgb);
    BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(8), (ushort)Av1MatrixCoefficients.Identity);
    payload[10] = 0x80; // full_range_flag
    return IsoBmffBox.BuildBox(IsoBmffBox.Colr, payload);
  }

  private static void _WriteFourCc(byte[] buffer, int offset, string fourCc) {
    for (var i = 0; i < 4; ++i)
      buffer[offset + i] = (byte)fourCc[i];
  }
}
