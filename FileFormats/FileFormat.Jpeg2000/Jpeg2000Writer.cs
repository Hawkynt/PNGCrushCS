using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Jpeg2000.Codec;

namespace FileFormat.Jpeg2000;

/// <summary>Assembles JPEG 2000 (JP2) file bytes from pixel data.</summary>
public static class Jpeg2000Writer {

  /// <summary>J2K SOC marker: Start of Codestream.</summary>
  private const ushort _SOC = 0xFF4F;

  /// <summary>J2K SIZ marker: Image and Tile Size.</summary>
  private const ushort _SIZ = 0xFF51;

  /// <summary>J2K COD marker: Coding Style Default.</summary>
  private const ushort _COD = 0xFF52;

  /// <summary>J2K QCD marker: Quantization Default.</summary>
  private const ushort _QCD = 0xFF5C;

  /// <summary>J2K SOT marker: Start of Tile-Part.</summary>
  private const ushort _SOT = 0xFF90;

  /// <summary>J2K SOD marker: Start of Data.</summary>
  private const ushort _SOD = 0xFF93;

  /// <summary>J2K EOC marker: End of Codestream.</summary>
  private const ushort _EOC = 0xFFD9;

  public static byte[] ToBytes(Jpeg2000File file) {
    ArgumentNullException.ThrowIfNull(file);
    var codestream = _BuildCodestream(file);
    return _BuildJp2Container(file, codestream);
  }

  internal static byte[] ToCodestreamBytes(Jpeg2000File file) {
    ArgumentNullException.ThrowIfNull(file);
    return _BuildCodestream(file);
  }

  /// <summary>Encode using the EBCOT pipeline (Tier-1/Tier-2 arithmetic coding). May produce spec-compliant JPEG 2000 bitstreams.</summary>
  internal static byte[] ToBytesEbcot(Jpeg2000File file) {
    ArgumentNullException.ThrowIfNull(file);
    var codestream = _BuildCodestreamEbcot(file);
    return _BuildJp2Container(file, codestream);
  }

  private static byte[] _BuildJp2Container(Jpeg2000File file, byte[] codestream) {
    using var ms = new MemoryStream();

    // JP2 Signature box (12 bytes)
    ms.Write(Jp2Box.JP2_SIGNATURE_BYTES);

    // File Type box (ftyp)
    var ftypData = _BuildFileTypeBox();
    Jp2Box.WriteBox(ms, Jp2Box.TYPE_FILE_TYPE, ftypData);

    // JP2 Header superbox (jp2h) containing ihdr and colr
    var jp2hData = _BuildJp2HeaderBox(file);
    Jp2Box.WriteBox(ms, Jp2Box.TYPE_JP2_HEADER, jp2hData);

    // Contiguous Codestream box (jp2c)
    Jp2Box.WriteBox(ms, Jp2Box.TYPE_CODESTREAM, codestream);

    return ms.ToArray();
  }

  private static byte[] _BuildFileTypeBox() {
    // ftyp: BR(4) + MinV(4) + CL0(4) = 12 bytes
    var data = new byte[12];
    data[0] = (byte)'j';
    data[1] = (byte)'p';
    data[2] = (byte)'2';
    data[3] = (byte)' ';
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4), 0);
    data[8] = (byte)'j';
    data[9] = (byte)'p';
    data[10] = (byte)'2';
    data[11] = (byte)' ';
    return data;
  }

  private static byte[] _BuildJp2HeaderBox(Jpeg2000File file) {
    using var ms = new MemoryStream();

    // Image Header box (ihdr): 14 bytes payload
    var ihdrData = new byte[14];
    BinaryPrimitives.WriteUInt32BigEndian(ihdrData.AsSpan(0), (uint)file.Height);
    BinaryPrimitives.WriteUInt32BigEndian(ihdrData.AsSpan(4), (uint)file.Width);
    BinaryPrimitives.WriteUInt16BigEndian(ihdrData.AsSpan(8), (ushort)file.ComponentCount);
    ihdrData[10] = (byte)(file.BitsPerComponent - 1); // Ssiz: unsigned, bpc - 1
    ihdrData[11] = 7; // C: compression type = JPEG 2000
    ihdrData[12] = 0; // UnkC: colourspace unknown = 0 (known)
    ihdrData[13] = 0; // IPR: no intellectual property
    Jp2Box.WriteBox(ms, Jp2Box.TYPE_IMAGE_HEADER, ihdrData);

    // Colour Specification box (colr): method 1 (enumerated colourspace)
    var colrData = new byte[7];
    colrData[0] = 1; // METH: Enumerated Colourspace
    colrData[1] = 0; // PREC: precedence
    colrData[2] = 0; // APPROX: approximation
    var enumCs = file.ComponentCount == 1 ? 17u : 16u;
    BinaryPrimitives.WriteUInt32BigEndian(colrData.AsSpan(3), enumCs);
    Jp2Box.WriteBox(ms, Jp2Box.TYPE_COLOUR_SPEC, colrData);

    return ms.ToArray();
  }

  private static byte[] _BuildCodestream(Jpeg2000File file) {
    using var ms = new MemoryStream();

    _WriteMarker(ms, _SOC);
    _WriteSiz(ms, file);
    _WriteCod(ms, file);
    _WriteQcd(ms, file);

    var tileData = _EncodeCoefficients(file);

    _WriteSot(ms, tileData.Length);
    _WriteMarker(ms, _SOD);
    ms.Write(tileData);
    _WriteMarker(ms, _EOC);

    return ms.ToArray();
  }

  private static byte[] _BuildCodestreamEbcot(Jpeg2000File file) {
    using var ms = new MemoryStream();

    _WriteMarker(ms, _SOC);
    _WriteSiz(ms, file);
    _WriteCod(ms, file);
    _WriteQcd(ms, file);

    var tileData = _EncodeEbcot(file);

    _WriteSot(ms, tileData.Length);
    _WriteMarker(ms, _SOD);
    ms.Write(tileData);
    _WriteMarker(ms, _EOC);

    return ms.ToArray();
  }

  private static void _WriteMarker(MemoryStream ms, ushort marker) {
    Span<byte> buf = stackalloc byte[2];
    BinaryPrimitives.WriteUInt16BigEndian(buf, marker);
    ms.Write(buf);
  }

  private static void _WriteSiz(MemoryStream ms, Jpeg2000File file) {
    _WriteMarker(ms, _SIZ);

    var csiz = file.ComponentCount;
    var lsiz = (ushort)(38 + 3 * csiz);
    var data = new byte[lsiz];
    var pos = 0;

    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(pos), lsiz);
    pos += 2;
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(pos), 0); // Rsiz
    pos += 2;
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(pos), (uint)file.Width);
    pos += 4;
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(pos), (uint)file.Height);
    pos += 4;
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(pos), 0); // XOsiz
    pos += 4;
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(pos), 0); // YOsiz
    pos += 4;
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(pos), (uint)file.Width); // XTsiz (single tile)
    pos += 4;
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(pos), (uint)file.Height); // YTsiz
    pos += 4;
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(pos), 0); // XTOsiz
    pos += 4;
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(pos), 0); // YTOsiz
    pos += 4;
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(pos), (ushort)csiz);
    pos += 2;

    for (var c = 0; c < csiz; ++c) {
      data[pos] = (byte)(file.BitsPerComponent - 1); // Ssiz
      ++pos;
      data[pos] = 1; // XRsiz
      ++pos;
      data[pos] = 1; // YRsiz
      ++pos;
    }

    ms.Write(data);
  }

  private static void _WriteCod(MemoryStream ms, Jpeg2000File file) {
    _WriteMarker(ms, _COD);

    var lcod = (ushort)12;
    var data = new byte[lcod];
    var pos = 0;

    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(pos), lcod);
    pos += 2;
    data[pos] = 0; // Scod: no precincts, no SOP, no EPH
    ++pos;
    data[pos] = 0; // Progression order: LRCP
    ++pos;
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(pos), 1); // Number of layers
    pos += 2;
    data[pos] = file.ComponentCount >= 3 ? (byte)1 : (byte)0; // MCT
    ++pos;
    data[pos] = (byte)file.DecompositionLevels;
    ++pos;
    data[pos] = 4; // Code-block width exponent offset: 2^(4+2) = 64
    ++pos;
    data[pos] = 4; // Code-block height exponent offset: 2^(4+2) = 64
    ++pos;
    data[pos] = 0; // Code-block style
    ++pos;
    data[pos] = 1; // Wavelet transform: 1 = 5/3 reversible
    ++pos;

    ms.Write(data);
  }

  private static void _WriteQcd(MemoryStream ms, Jpeg2000File file) {
    _WriteMarker(ms, _QCD);

    var numSubbands = 1 + 3 * file.DecompositionLevels;
    var lqcd = (ushort)(3 + numSubbands);
    var data = new byte[lqcd];
    var pos = 0;

    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(pos), lqcd);
    pos += 2;
    data[pos] = 0; // Sqcd: no quantization (reversible)
    ++pos;

    for (var i = 0; i < numSubbands; ++i) {
      data[pos] = (byte)((file.BitsPerComponent + 1) << 3); // epsilon << 3
      ++pos;
    }

    ms.Write(data);
  }

  private static void _WriteSot(MemoryStream ms, int tileDataLength) {
    _WriteMarker(ms, _SOT);

    var data = new byte[10];
    var pos = 0;

    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(pos), 10); // Lsot
    pos += 2;
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(pos), 0); // Isot: tile 0
    pos += 2;
    var psot = (uint)(2 + 10 + 2 + tileDataLength);
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(pos), psot);
    pos += 4;
    data[pos] = 0; // TPsot
    ++pos;
    data[pos] = 1; // TNsot
    ++pos;

    ms.Write(data);
  }

  /// <summary>Simplified encoding: raw big-endian int32 wavelet coefficients per component.</summary>
  private static byte[] _EncodeCoefficients(Jpeg2000File file) {
    var width = file.Width;
    var height = file.Height;
    var componentCount = file.ComponentCount;
    var levels = file.DecompositionLevels;
    var coeffsPerComponent = width * height;

    var result = new byte[coeffsPerComponent * componentCount * 4];
    var plane = new int[height, width];

    for (var c = 0; c < componentCount; ++c) {
      for (var y = 0; y < height; ++y)
        for (var x = 0; x < width; ++x)
          plane[y, x] = file.PixelData[(y * width + x) * componentCount + c];

      Jp2Wavelet.ForwardMultiLevel(plane, width, height, levels);

      var baseOffset = c * coeffsPerComponent * 4;
      for (var y = 0; y < height; ++y)
        for (var x = 0; x < width; ++x) {
          var idx = baseOffset + (y * width + x) * 4;
          BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(idx), plane[y, x]);
        }
    }

    return result;
  }

  /// <summary>Encode pixel data using the EBCOT pipeline: Forward DWT -> Tier-1 -> Tier-2.</summary>
  internal static byte[] _EncodeEbcot(Jpeg2000File file) {
    var width = file.Width;
    var height = file.Height;
    var componentCount = file.ComponentCount;
    var levels = file.DecompositionLevels;
    var cbWidth = 64;
    var cbHeight = 64;

    var subbands = SubbandInfo.ComputeSubbands(width, height, levels);
    var allCodeBlocks = new List<CodeBlockData>();

    for (var c = 0; c < componentCount; ++c) {
      var plane = new int[height, width];

      // Extract component plane with DC level shift
      var shift = file.BitsPerComponent > 1 ? 1 << (file.BitsPerComponent - 1) : 0;
      for (var y = 0; y < height; ++y)
        for (var x = 0; x < width; ++x)
          plane[y, x] = file.PixelData[(y * width + x) * componentCount + c] - shift;

      // Forward DWT
      Jp2Wavelet.ForwardMultiLevel(plane, width, height, levels);

      // Encode each code-block per subband
      foreach (var sb in subbands) {
        if (sb.Width == 0 || sb.Height == 0)
          continue;

        sb.GetCodeBlockGrid(cbWidth, cbHeight, out var numCbX, out var numCbY);

        for (var cbY = 0; cbY < numCbY; ++cbY)
          for (var cbX = 0; cbX < numCbX; ++cbX) {
            var actualW = Math.Min(cbWidth, sb.Width - cbX * cbWidth);
            var actualH = Math.Min(cbHeight, sb.Height - cbY * cbHeight);
            if (actualW <= 0 || actualH <= 0)
              continue;

            var cbCoeffs = new int[actualH, actualW];
            var baseX = sb.OffsetX + cbX * cbWidth;
            var baseY = sb.OffsetY + cbY * cbHeight;
            for (var y = 0; y < actualH; ++y)
              for (var x = 0; x < actualW; ++x)
                cbCoeffs[y, x] = plane[baseY + y, baseX + x];

            var compData = Tier1Encoder.EncodeCodeBlock(
              cbCoeffs, actualW, actualH,
              out var numPasses, out var zeroBitPlanes
            );

            if (numPasses > 0 && compData.Length > 0)
              allCodeBlocks.Add(new CodeBlockData {
                SubbandIndex = sb.Index + c * subbands.Length,
                CodeBlockX = cbX,
                CodeBlockY = cbY,
                NumCodingPasses = numPasses,
                ZeroBitPlanes = zeroBitPlanes,
                CompressedData = compData,
              });
          }
      }
    }

    var tileInfo = new TileInfo {
      Width = width,
      Height = height,
      DecompLevels = levels,
      ComponentCount = componentCount,
      CodeBlockWidth = cbWidth,
      CodeBlockHeight = cbHeight,
      Layers = 1,
      UseMct = componentCount >= 3,
      BitsPerComponent = file.BitsPerComponent,
    };

    return Tier2Encoder.AssemblePackets(allCodeBlocks, tileInfo);
  }
}
