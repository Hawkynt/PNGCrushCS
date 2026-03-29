using System;
using System.IO;
using System.Text;

namespace FileFormat.Nitf;

/// <summary>Assembles NITF (National Imagery Transmission Format) file bytes from pixel data.</summary>
public static class NitfWriter {

  public static byte[] ToBytes(NitfFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file.Width, file.Height, file.Mode, file.PixelData, file.Title, file.Classification);
  }

  internal static byte[] Assemble(int width, int height, NitfImageMode mode, byte[] pixelData, string title, char classification) {
    var nbands = mode == NitfImageMode.Rgb ? 3 : 1;
    var irep = mode == NitfImageMode.Rgb ? "RGB" : "MONO";
    var irepBands = mode == NitfImageMode.Rgb ? new[] { "R", "G", "B" } : new[] { "M" };
    var expectedPixelBytes = width * height * nbands;

    // Build image subheader
    var imageSubheader = _BuildImageSubheader(width, height, nbands, irep, irepBands);
    var imageSubheaderLength = imageSubheader.Length;

    // Build file header (needs to know subheader + data lengths)
    var fileHeader = _BuildFileHeader(imageSubheaderLength, expectedPixelBytes, title, classification);
    var fileHeaderLength = fileHeader.Length;

    // Update HL field in file header to be the actual header length
    _WriteField(fileHeader, _HL_OFFSET_IN_HEADER(fileHeader), 6, fileHeaderLength.ToString());

    // Total file length
    var totalLength = fileHeaderLength + imageSubheaderLength + expectedPixelBytes;
    _WriteField(fileHeader, _FL_OFFSET_IN_HEADER(fileHeader), 12, totalLength.ToString());

    // Assemble output
    var result = new byte[totalLength];
    fileHeader.AsSpan(0, fileHeaderLength).CopyTo(result.AsSpan(0));
    imageSubheader.AsSpan(0, imageSubheaderLength).CopyTo(result.AsSpan(fileHeaderLength));

    var copyLen = Math.Min(expectedPixelBytes, pixelData.Length);
    if (copyLen > 0)
      pixelData.AsSpan(0, copyLen).CopyTo(result.AsSpan(fileHeaderLength + imageSubheaderLength));

    return result;
  }

  private static byte[] _BuildFileHeader(int imageSubheaderLength, int imageDataLength, string title, char classification) {
    using var ms = new MemoryStream();
    using var w = new StreamWriter(ms, Encoding.ASCII) { AutoFlush = true };

    // FHDR (4)
    _Write(w, "NITF", 4);
    // FVER (5)
    _Write(w, "02.10", 5);
    // CLEVEL (2)
    _WriteNum(w, 3, 2);
    // STYPE (4)
    _Write(w, "BF01", 4);
    // OSTAID (10)
    _Write(w, "", 10);
    // FDT (14)
    _Write(w, "20200101000000", 14);
    // FTITLE (80)
    _Write(w, title ?? "", 80);
    // FSCLAS (1)
    _Write(w, classification.ToString(), 1);

    // Security fields: FSCLSY(2) + FSCODE(11) + FSCTLH(2) + FSREL(20) + FSDCTP(2) + FSDCDT(8) +
    // FSDCXM(4) + FSDG(1) + FSDGDT(8) + FSCLTX(43) + FSCATP(1) + FSCAUT(40) + FSCRSN(1) +
    // FSSRDT(8) + FSCTLN(15) = 166
    _Write(w, "", 2);   // FSCLSY
    _Write(w, "", 11);  // FSCODE
    _Write(w, "", 2);   // FSCTLH
    _Write(w, "", 20);  // FSREL
    _Write(w, "", 2);   // FSDCTP
    _Write(w, "", 8);   // FSDCDT
    _Write(w, "", 4);   // FSDCXM
    _Write(w, "", 1);   // FSDG
    _Write(w, "", 8);   // FSDGDT
    _Write(w, "", 43);  // FSCLTX
    _Write(w, "", 1);   // FSCATP
    _Write(w, "", 40);  // FSCAUT
    _Write(w, "", 1);   // FSCRSN
    _Write(w, "", 8);   // FSSRDT
    _Write(w, "", 15);  // FSCTLN

    // FSCOP (5)
    _WriteNum(w, 0, 5);
    // FSCPYS (5)
    _WriteNum(w, 0, 5);
    // ENCRYP (1)
    _WriteNum(w, 0, 1);
    // FBKGC (3 bytes binary - but we write ASCII zeros for simplicity)
    w.Flush();
    ms.Write(new byte[3], 0, 3);
    // ONAME (24)
    _Write(w, "", 24);
    // OPHONE (18)
    _Write(w, "", 18);

    // FL (12) - placeholder, will be filled in later
    var flPosition = (int)ms.Position;
    _WriteNum(w, 0, 12);

    // HL (6) - placeholder, will be filled in later
    var hlPosition = (int)ms.Position;
    _WriteNum(w, 0, 6);

    // NUMI (3) - always 1 image segment
    _WriteNum(w, 1, 3);

    // LISH (6) - image subheader length
    _WriteNum(w, imageSubheaderLength, 6);
    // LI (10) - image data length
    _WriteNum(w, imageDataLength, 10);

    // NUMS (3) - number of graphic segments
    _WriteNum(w, 0, 3);

    // NUMX (3) - reserved
    _WriteNum(w, 0, 3);

    // NUMT (3) - number of text segments
    _WriteNum(w, 0, 3);

    // NUMDES (3) - number of data extension segments
    _WriteNum(w, 0, 3);

    // NUMRES (3) - number of reserved extension segments
    _WriteNum(w, 0, 3);

    // UDHDL (5) - user-defined header data length
    _WriteNum(w, 0, 5);

    // XHDL (5) - extended header data length
    _WriteNum(w, 0, 5);

    w.Flush();
    var headerBytes = ms.ToArray();

    // Store FL and HL positions for later fixup
    _flOffset = flPosition;
    _hlOffset = hlPosition;

    return headerBytes;
  }

  [ThreadStatic] private static int _flOffset;
  [ThreadStatic] private static int _hlOffset;

  private static int _FL_OFFSET_IN_HEADER(byte[] header) => _flOffset;
  private static int _HL_OFFSET_IN_HEADER(byte[] header) => _hlOffset;

  private static byte[] _BuildImageSubheader(int width, int height, int nbands, string irep, string[] irepBands) {
    using var ms = new MemoryStream();
    using var w = new StreamWriter(ms, Encoding.ASCII) { AutoFlush = true };

    // IM (2)
    _Write(w, "IM", 2);
    // IID1 (10)
    _Write(w, "", 10);
    // IDATIM (14)
    _Write(w, "20200101000000", 14);
    // TGTID (17)
    _Write(w, "", 17);
    // IID2 (80)
    _Write(w, "", 80);

    // ISCLAS (1)
    _Write(w, "U", 1);

    // Image security fields (same layout as file security, 166 bytes)
    _Write(w, "", 2);   // ISCLSY
    _Write(w, "", 11);  // ISCODE
    _Write(w, "", 2);   // ISCTLH
    _Write(w, "", 20);  // ISREL
    _Write(w, "", 2);   // ISDCTP
    _Write(w, "", 8);   // ISDCDT
    _Write(w, "", 4);   // ISDCXM
    _Write(w, "", 1);   // ISDG
    _Write(w, "", 8);   // ISDGDT
    _Write(w, "", 43);  // ISCLTX
    _Write(w, "", 1);   // ISCATP
    _Write(w, "", 40);  // ISCAUT
    _Write(w, "", 1);   // ISCRSN
    _Write(w, "", 8);   // ISSRDT
    _Write(w, "", 15);  // ISCTLN

    // ENCRYP (1)
    _WriteNum(w, 0, 1);
    // ISORCE (42)
    _Write(w, "", 42);

    // NROWS (8)
    _WriteNum(w, height, 8);
    // NCOLS (8)
    _WriteNum(w, width, 8);

    // PVTYPE (3) - always INT for 8-bit
    _Write(w, "INT", 3);
    // IREP (8)
    _Write(w, irep, 8);
    // ICAT (8)
    _Write(w, "VIS", 8);
    // ABPP (2) - actual bits per pixel per band
    _WriteNum(w, 8, 2);
    // PJUST (1) - pixel justification
    _Write(w, "R", 1);

    // ICORDS (1) - no coordinate system
    _Write(w, " ", 1);

    // NICOM (1) - no image comments
    _WriteNum(w, 0, 1);

    // IC (2) - no compression
    _Write(w, "NC", 2);

    // NBANDS (1)
    _WriteNum(w, nbands, 1);

    // Per-band fields
    for (var b = 0; b < nbands; ++b) {
      // IREPBAND (2)
      _Write(w, irepBands[b], 2);
      // ISUBCAT (6)
      _Write(w, "", 6);
      // IFC (1) - no filter
      _Write(w, "N", 1);
      // IMFLT (3) - no filter
      _Write(w, "", 3);
      // NLUTS (1)
      _WriteNum(w, 0, 1);
    }

    // ISYNC (1)
    _WriteNum(w, 0, 1);

    // IMODE (1) - band sequential for multi-band, block for single band
    _Write(w, nbands > 1 ? "S" : "B", 1);

    // NBPR (4) - number of blocks per row
    _WriteNum(w, 1, 4);
    // NBPC (4) - number of blocks per column
    _WriteNum(w, 1, 4);
    // NPPBH (4) - pixels per block horizontal
    _WriteNum(w, width, 4);
    // NPPBV (4) - pixels per block vertical
    _WriteNum(w, height, 4);
    // NBPP (2) - bits per pixel per band
    _WriteNum(w, 8, 2);

    // IDLVL (3)
    _WriteNum(w, 1, 3);
    // IALVL (3)
    _WriteNum(w, 0, 3);
    // ILOC (10) - row(5) + col(5)
    _WriteNum(w, 0, 5);
    _WriteNum(w, 0, 5);
    // IMAG (4)
    _Write(w, "1.0 ", 4);

    // UDIDL (5)
    _WriteNum(w, 0, 5);
    // IXSHDL (5)
    _WriteNum(w, 0, 5);

    w.Flush();
    return ms.ToArray();
  }

  /// <summary>Writes a right-padded ASCII string field.</summary>
  private static void _Write(StreamWriter w, string value, int fieldLength) {
    var truncated = value.Length > fieldLength ? value[..fieldLength] : value;
    w.Write(truncated.PadRight(fieldLength));
  }

  /// <summary>Writes a left-zero-padded numeric field.</summary>
  private static void _WriteNum(StreamWriter w, long value, int fieldLength) {
    var s = value.ToString();
    if (s.Length > fieldLength)
      s = s[..fieldLength];
    w.Write(s.PadLeft(fieldLength, '0'));
  }

  /// <summary>Overwrites a numeric field in-place within an existing byte array.</summary>
  private static void _WriteField(byte[] buffer, int offset, int length, string value) {
    var padded = value.PadLeft(length, '0');
    if (padded.Length > length)
      padded = padded[..length];
    Encoding.ASCII.GetBytes(padded, 0, length, buffer, offset);
  }
}
