using System;
using System.IO;
using System.Text;

namespace FileFormat.Nitf;

/// <summary>Reads NITF (National Imagery Transmission Format) files from bytes, streams, or file paths.</summary>
public static class NitfReader {

  private const string _MAGIC = "NITF";
  private const string _VERSION = "02.10";
  private const int _MIN_FILE_SIZE = 9; // FHDR(4) + FVER(5) minimum

  // File header field offsets and lengths (MIL-STD-2500C)
  private const int _FHDR_OFFSET = 0;
  private const int _FHDR_LENGTH = 4;
  private const int _FVER_OFFSET = 4;
  private const int _FVER_LENGTH = 5;
  private const int _CLEVEL_OFFSET = 9;
  private const int _CLEVEL_LENGTH = 2;
  private const int _STYPE_OFFSET = 11;
  private const int _STYPE_LENGTH = 4;
  private const int _OSTAID_OFFSET = 15;
  private const int _OSTAID_LENGTH = 10;
  private const int _FDT_OFFSET = 25;
  private const int _FDT_LENGTH = 14;
  private const int _FTITLE_OFFSET = 39;
  private const int _FTITLE_LENGTH = 80;
  private const int _FSCLAS_OFFSET = 119;
  private const int _FSCLAS_LENGTH = 1;

  // Security fields after FSCLAS (simplified: we skip over them)
  // FSCLSY(2) + FSCODE(11) + FSCTLH(2) + FSREL(20) + FSDCTP(2) + FSDCDT(8) + FSDCXM(4) +
  // FSDG(1) + FSDGDT(8) + FSCLTX(43) + FSCATP(1) + FSCAUT(40) + FSCRSN(1) + FSSRDT(8) + FSCTLN(15)
  private const int _SECURITY_FIELDS_LENGTH = 2 + 11 + 2 + 20 + 2 + 8 + 4 + 1 + 8 + 43 + 1 + 40 + 1 + 8 + 15; // = 166

  private const int _FSCOP_OFFSET = _FSCLAS_OFFSET + _FSCLAS_LENGTH + _SECURITY_FIELDS_LENGTH; // 286
  private const int _FSCOP_LENGTH = 5;
  private const int _FSCPYS_LENGTH = 5;
  private const int _ENCRYP_LENGTH = 1;
  private const int _FBKGC_LENGTH = 3;
  private const int _ONAME_LENGTH = 24;
  private const int _OPHONE_LENGTH = 18;

  private static int _FlOffset =>
    _FSCOP_OFFSET + _FSCOP_LENGTH + _FSCPYS_LENGTH + _ENCRYP_LENGTH + _FBKGC_LENGTH + _ONAME_LENGTH + _OPHONE_LENGTH;

  private const int _FL_LENGTH = 12;
  private const int _HL_LENGTH = 6;
  private const int _NUMI_LENGTH = 3;
  private const int _LISH_LENGTH = 6;
  private const int _LI_LENGTH = 10;

  // Image subheader field offsets (relative to subheader start)
  private const int _IM_LENGTH = 2;
  private const int _IID1_LENGTH = 10;
  private const int _IDATIM_LENGTH = 14;
  private const int _TGTID_LENGTH = 17;
  private const int _IID2_LENGTH = 80;
  private const int _ISCLAS_LENGTH = 1;
  private const int _ISECURITY_FIELDS_LENGTH = 2 + 11 + 2 + 20 + 2 + 8 + 4 + 1 + 8 + 43 + 1 + 40 + 1 + 8 + 15; // = 166
  private const int _ENCRYP_IMG_LENGTH = 1;
  private const int _ISORCE_LENGTH = 42;
  private const int _NROWS_LENGTH = 8;
  private const int _NCOLS_LENGTH = 8;
  private const int _PVTYPE_LENGTH = 3;
  private const int _IREP_LENGTH = 8;
  private const int _ICAT_LENGTH = 8;
  private const int _ABPP_LENGTH = 2;
  private const int _PJUST_LENGTH = 1;
  private const int _ICORDS_LENGTH = 1;
  private const int _NICOM_LENGTH = 1;
  private const int _IC_LENGTH = 2;

  public static NitfFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("NITF file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static NitfFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static NitfFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < _MIN_FILE_SIZE)
      throw new InvalidDataException($"Data too small for a valid NITF file (minimum {_MIN_FILE_SIZE} bytes, got {data.Length}).");

    var bytes = data.ToArray();

    var magic = Encoding.ASCII.GetString(data.Slice(_FHDR_OFFSET, _FHDR_LENGTH));
    if (magic != _MAGIC)
      throw new InvalidDataException($"Invalid NITF magic: expected '{_MAGIC}', got '{magic}'.");

    var version = Encoding.ASCII.GetString(data.Slice(_FVER_OFFSET, _FVER_LENGTH));
    if (version != _VERSION)
      throw new InvalidDataException($"Unsupported NITF version: expected '{_VERSION}', got '{version}'.");

    var title = _ReadField(bytes, _FTITLE_OFFSET, _FTITLE_LENGTH).TrimEnd();
    var classification = _ReadField(bytes, _FSCLAS_OFFSET, _FSCLAS_LENGTH)[0];

    // Read HL (header length)
    var flOffset = _FlOffset;
    var hlOffset = flOffset + _FL_LENGTH;
    var hl = _ReadInt(bytes, hlOffset, _HL_LENGTH);

    // Read NUMI (number of image segments)
    var numiOffset = hlOffset + _HL_LENGTH;
    var numi = _ReadInt(bytes, numiOffset, _NUMI_LENGTH);
    if (numi < 1)
      throw new InvalidDataException("NITF file contains no image segments.");

    // Read first image segment lengths
    var imageInfoOffset = numiOffset + _NUMI_LENGTH;
    var lish = _ReadInt(bytes, imageInfoOffset, _LISH_LENGTH);
    var li = _ReadLong(bytes, imageInfoOffset + _LISH_LENGTH, _LI_LENGTH);

    // The image subheader starts at offset HL
    var subheaderOffset = hl;
    return _ParseImageSegment(bytes, subheaderOffset, (int)li, title, classification);
  }

  public static NitfFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  private static NitfFile _ParseImageSegment(byte[] data, int subheaderOffset, int dataLength, string title, char classification) {
    var offset = subheaderOffset;

    // IM (2 chars) - must be "IM"
    var im = _ReadField(data, offset, _IM_LENGTH);
    if (im != "IM")
      throw new InvalidDataException($"Invalid image subheader marker: expected 'IM', got '{im}'.");
    offset += _IM_LENGTH;

    // Skip IID1, IDATIM, TGTID, IID2
    offset += _IID1_LENGTH + _IDATIM_LENGTH + _TGTID_LENGTH + _IID2_LENGTH;

    // Skip ISCLAS + security fields + ENCRYP
    offset += _ISCLAS_LENGTH + _ISECURITY_FIELDS_LENGTH + _ENCRYP_IMG_LENGTH;

    // Skip ISORCE
    offset += _ISORCE_LENGTH;

    // Read NROWS
    var nrows = _ReadInt(data, offset, _NROWS_LENGTH);
    offset += _NROWS_LENGTH;

    // Read NCOLS
    var ncols = _ReadInt(data, offset, _NCOLS_LENGTH);
    offset += _NCOLS_LENGTH;

    // Read PVTYPE
    var pvtype = _ReadField(data, offset, _PVTYPE_LENGTH).TrimEnd();
    offset += _PVTYPE_LENGTH;

    // Read IREP
    var irep = _ReadField(data, offset, _IREP_LENGTH).TrimEnd();
    offset += _IREP_LENGTH;

    // Read ICAT
    offset += _ICAT_LENGTH;

    // Read ABPP
    var abpp = _ReadInt(data, offset, _ABPP_LENGTH);
    offset += _ABPP_LENGTH;

    // Skip PJUST
    offset += _PJUST_LENGTH;

    // Read ICORDS
    var icords = _ReadField(data, offset, _ICORDS_LENGTH);
    offset += _ICORDS_LENGTH;

    // IGEOLO - 60 chars if ICORDS != ' ' (space)
    if (icords[0] != ' ' && icords[0] != '\0')
      offset += 60;

    // Read NICOM (number of image comments)
    var nicom = _ReadInt(data, offset, _NICOM_LENGTH);
    offset += _NICOM_LENGTH;

    // Skip image comments (80 chars each)
    offset += nicom * 80;

    // Read IC (image compression)
    var ic = _ReadField(data, offset, _IC_LENGTH).TrimEnd();
    offset += _IC_LENGTH;

    // COMRAT - only present if compressed
    if (ic != "NC" && ic != "NM")
      offset += 4;

    // NBANDS
    var nbands = _ReadInt(data, offset, 1);
    offset += 1;

    // XBANDS - only if NBANDS == 0
    if (nbands == 0) {
      nbands = _ReadInt(data, offset, 5);
      offset += 5;
    }

    // Per-band fields: IREPBAND(2) + ISUBCAT(6) + IFC(1) + IMFLT(3) + NLUTS(1)
    for (var b = 0; b < nbands; ++b) {
      offset += 2 + 6 + 1 + 3; // IREPBAND + ISUBCAT + IFC + IMFLT
      var nluts = _ReadInt(data, offset, 1);
      offset += 1;
      if (nluts > 0) {
        var nelut = _ReadInt(data, offset, 5);
        offset += 5;
        offset += nluts * nelut;
      }
    }

    // ISYNC
    offset += 1;

    // IMODE
    var imode = _ReadField(data, offset, 1);
    offset += 1;

    // NBPR, NBPC, NPPBH, NPPBV, NBPP
    offset += 4 + 4 + 4 + 4;
    var nbpp = _ReadInt(data, offset, 2);
    offset += 2;

    // IDLVL, IALVL, ILOC, IMAG
    offset += 3 + 3 + 10 + 4;

    // UDIDL
    var udidl = _ReadInt(data, offset, 5);
    offset += 5;
    if (udidl > 0)
      offset += udidl - 3; // UDOFL(3) already counted as part of the header

    // IXSHDL
    var ixshdl = _ReadInt(data, offset, 5);
    offset += 5;
    if (ixshdl > 0)
      offset += ixshdl - 3;

    // Pixel data starts after the subheader
    var pixelDataOffset = offset;

    var mode = irep switch {
      "MONO" => NitfImageMode.Grayscale,
      "RGB" => NitfImageMode.Rgb,
      _ => irep.StartsWith("RGB") ? NitfImageMode.Rgb : NitfImageMode.Grayscale,
    };

    var bytesPerPixelPerBand = (nbpp + 7) / 8;
    var expectedPixelBytes = nrows * ncols * nbands * bytesPerPixelPerBand;
    var available = data.Length - pixelDataOffset;
    var copyLen = Math.Min(expectedPixelBytes, Math.Max(0, available));

    var pixelData = new byte[expectedPixelBytes];
    if (copyLen > 0)
      data.AsSpan(pixelDataOffset, copyLen).CopyTo(pixelData.AsSpan(0));

    return new NitfFile {
      Width = ncols,
      Height = nrows,
      Mode = mode,
      Title = title,
      Classification = classification,
      PixelData = pixelData,
    };
  }

  private static string _ReadField(byte[] data, int offset, int length) {
    if (offset + length > data.Length)
      throw new InvalidDataException($"Unexpected end of data at offset {offset} (need {length} bytes, have {data.Length - offset}).");

    return Encoding.ASCII.GetString(data, offset, length);
  }

  private static int _ReadInt(byte[] data, int offset, int length) {
    var field = _ReadField(data, offset, length).Trim();
    if (field.Length == 0)
      return 0;

    if (!int.TryParse(field, out var value))
      throw new InvalidDataException($"Expected integer at offset {offset}, got '{field}'.");

    return value;
  }

  private static long _ReadLong(byte[] data, int offset, int length) {
    var field = _ReadField(data, offset, length).Trim();
    if (field.Length == 0)
      return 0;

    if (!long.TryParse(field, out var value))
      throw new InvalidDataException($"Expected long integer at offset {offset}, got '{field}'.");

    return value;
  }
}
