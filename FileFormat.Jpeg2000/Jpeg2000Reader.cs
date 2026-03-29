using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Jpeg2000.Codec;

namespace FileFormat.Jpeg2000;

/// <summary>Reads JPEG 2000 files (JP2 container or raw J2K codestream) from bytes, streams, or file paths.</summary>
public static class Jpeg2000Reader {

  /// <summary>Minimum size: 12-byte JP2 signature or at least SOC+SIZ+EOC markers.</summary>
  private const int _MINIMUM_SIZE = 12;

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

  public static Jpeg2000File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("JPEG 2000 file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static Jpeg2000File FromStream(Stream stream) {
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

  public static Jpeg2000File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < _MINIMUM_SIZE)
      throw new InvalidDataException("Data too small for a valid JPEG 2000 file.");

    // Check for JP2 container (box-based)
    if (_IsJp2Container(data))
      return _ParseJp2(data);

    // Check for raw J2K codestream
    if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0x4F)
      return _ParseCodestream(data, 0, data.Length);

    throw new InvalidDataException("Invalid JPEG 2000 signature: expected JP2 box or J2K SOC marker.");
  }

  private static bool _IsJp2Container(byte[] data) {
    if (data.Length < 12)
      return false;

    for (var i = 0; i < Jp2Box.JP2_SIGNATURE_BYTES.Length; ++i)
      if (data[i] != Jp2Box.JP2_SIGNATURE_BYTES[i])
        return false;

    return true;
  }

  private static Jpeg2000File _ParseJp2(byte[] data) {
    var boxes = Jp2Box.ReadBoxes(data, 0, data.Length);

    foreach (var box in boxes)
      if (box.Type == Jp2Box.TYPE_CODESTREAM)
        return _ParseCodestream(box.Data, 0, box.Data.Length);

    throw new InvalidDataException("JP2 file missing Contiguous Codestream (jp2c) box.");
  }

  private static Jpeg2000File _ParseCodestream(byte[] data, int offset, int length) {
    var end = offset + length;
    var pos = offset;

    if (pos + 2 > end || BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(pos)) != _SOC)
      throw new InvalidDataException("J2K codestream missing SOC marker.");

    pos += 2;

    var width = 0;
    var height = 0;
    var componentCount = 0;
    var bitsPerComponent = 8;
    var decompositionLevels = 3;
    var codeBlockWidthExp = 4; // 2^(4+2) = 64
    var codeBlockHeightExp = 4;
    var layers = 1;
    var useMct = false;
    var quantStyle = 0; // 0 = no quantization (reversible)
    var guardBits = 0;
    byte[]? tileData = null;

    while (pos + 2 <= end) {
      var marker = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(pos));
      pos += 2;

      switch (marker) {
        case _SIZ:
          _ParseSiz(data, ref pos, end, out width, out height, out componentCount, out bitsPerComponent);
          break;

        case _COD:
          _ParseCod(data, ref pos, end, out decompositionLevels, out codeBlockWidthExp, out codeBlockHeightExp, out layers, out useMct);
          break;

        case _QCD:
          _ParseQcd(data, ref pos, end, out quantStyle, out guardBits);
          break;

        case _SOT:
          _ParseSot(data, ref pos, end, out tileData);
          break;

        case _EOC:
          goto done;

        default:
          if (marker >= 0xFF30 && marker <= 0xFF3F)
            break;
          if (pos + 2 <= end) {
            var segLen = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(pos));
            pos += segLen;
          }
          break;
      }
    }

    done:
    if (width == 0 || height == 0)
      throw new InvalidDataException("J2K codestream missing SIZ marker or has zero dimensions.");

    var cbWidth = 1 << (codeBlockWidthExp + 2);
    var cbHeight = 1 << (codeBlockHeightExp + 2);

    var tileInfo = new TileInfo {
      Width = width,
      Height = height,
      DecompLevels = decompositionLevels,
      ComponentCount = componentCount,
      CodeBlockWidth = cbWidth,
      CodeBlockHeight = cbHeight,
      Layers = layers,
      UseMct = useMct,
      BitsPerComponent = bitsPerComponent,
    };

    byte[] pixelData;
    if (tileData != null)
      pixelData = _DecodeTileData(tileData, tileInfo, quantStyle, guardBits);
    else
      pixelData = new byte[width * height * componentCount];

    return new Jpeg2000File {
      Width = width,
      Height = height,
      ComponentCount = componentCount,
      BitsPerComponent = bitsPerComponent,
      DecompositionLevels = decompositionLevels,
      PixelData = pixelData,
    };
  }

  private static void _ParseSiz(byte[] data, ref int pos, int end, out int width, out int height, out int componentCount, out int bitsPerComponent) {
    if (pos + 2 > end)
      throw new InvalidDataException("SIZ marker segment truncated.");

    var segLen = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(pos));
    var segEnd = pos + segLen;
    if (segEnd > end)
      throw new InvalidDataException("SIZ marker segment extends beyond data.");

    if (segLen < 38)
      throw new InvalidDataException("SIZ marker segment too small.");

    var p = pos + 4; // skip Lsiz(2) + Rsiz(2)
    width = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(p));
    p += 4;
    height = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(p));
    p += 4;
    p += 24; // skip XOsiz, YOsiz, XTsiz, YTsiz, XTOsiz, YTOsiz

    componentCount = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(p));
    p += 2;

    if (p < segEnd) {
      var ssiz = data[p];
      bitsPerComponent = (ssiz & 0x7F) + 1;
    } else
      bitsPerComponent = 8;

    pos = segEnd;
  }

  private static void _ParseCod(byte[] data, ref int pos, int end, out int decompositionLevels, out int cbWidthExp, out int cbHeightExp, out int layers, out bool useMct) {
    if (pos + 2 > end)
      throw new InvalidDataException("COD marker segment truncated.");

    var segLen = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(pos));
    var segEnd = pos + segLen;
    if (segEnd > end)
      throw new InvalidDataException("COD marker segment extends beyond data.");

    decompositionLevels = 3;
    cbWidthExp = 4;
    cbHeightExp = 4;
    layers = 1;
    useMct = false;

    if (segLen >= 12) {
      // Lcod(2) + Scod(1) + SGcod: ProgOrder(1) + NumLayers(2) + MCT(1) + SPcod: NL(1) + cbW(1) + cbH(1) + cbStyle(1) + transform(1) = 12
      var p = pos + 3; // skip Lcod(2) + Scod(1)
      // SGcod
      p += 1; // skip progression order
      layers = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(p));
      p += 2;
      useMct = data[p] != 0;
      p += 1;
      // SPcod
      decompositionLevels = data[p];
      p += 1;
      cbWidthExp = data[p];
      p += 1;
      cbHeightExp = data[p];
    } else if (segLen >= 10) {
      var nlOffset = pos + 7;
      decompositionLevels = data[nlOffset];
    }

    pos = segEnd;
  }

  private static void _ParseQcd(byte[] data, ref int pos, int end, out int quantStyle, out int guardBits) {
    if (pos + 2 > end)
      throw new InvalidDataException("QCD marker segment truncated.");

    var segLen = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(pos));
    var segEnd = pos + segLen;
    if (segEnd > end)
      throw new InvalidDataException("QCD marker segment extends beyond data.");

    quantStyle = 0;
    guardBits = 0;

    if (segLen >= 3) {
      // Lqcd(2) + Sqcd(1)
      var sqcd = data[pos + 2];
      quantStyle = sqcd & 0x1F;     // Quantization style (0=no quant, 1=scalar derived, 2=scalar expounded)
      guardBits = (sqcd >> 5) & 0x07; // Guard bits
    }

    pos = segEnd;
  }

  private static void _ParseSot(byte[] data, ref int pos, int end, out byte[]? tileData) {
    if (pos + 2 > end)
      throw new InvalidDataException("SOT marker segment truncated.");

    var segLen = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(pos));
    var segEnd = pos + segLen;
    if (segEnd > end)
      throw new InvalidDataException("SOT marker segment extends beyond data.");

    var psot = 0u;
    if (segLen >= 10)
      psot = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos + 6));

    pos = segEnd;

    tileData = null;
    if (pos + 2 > end)
      return;

    var sodMarker = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(pos));
    if (sodMarker != _SOD)
      return;

    pos += 2;

    int tileDataLen;
    if (psot > 0)
      tileDataLen = (int)(psot - 2u - (uint)segLen - 2u);
    else
      tileDataLen = end - pos - 2;

    if (tileDataLen < 0)
      tileDataLen = 0;

    if (pos + tileDataLen > end)
      tileDataLen = end - pos;

    if (tileDataLen > 0) {
      tileData = new byte[tileDataLen];
      data.AsSpan(pos, tileDataLen).CopyTo(tileData.AsSpan(0));
    }

    pos += tileDataLen;
  }

  /// <summary>Decode tile data: tries simplified (raw int32) format first, then EBCOT codec pipeline.</summary>
  private static byte[] _DecodeTileData(byte[] tileData, TileInfo tile, int quantStyle, int guardBits) {
    var width = tile.Width;
    var height = tile.Height;
    var componentCount = tile.ComponentCount;
    var levels = tile.DecompLevels;

    // Try simplified raw-int32 format first (our default writer output)
    var expectedSimplifiedSize = width * height * componentCount * 4;
    if (tileData.Length >= expectedSimplifiedSize)
      return _DecodeSimplified(tileData, width, height, componentCount, levels);

    // Try EBCOT decoding for spec-compliant JPEG 2000 bitstreams
    return _DecodeEbcot(tileData, tile, quantStyle, guardBits);
  }

  /// <summary>Decode via the full EBCOT pipeline.</summary>
  private static byte[] _DecodeEbcot(byte[] tileData, TileInfo tile, int quantStyle, int guardBits) {
    var width = tile.Width;
    var height = tile.Height;
    var componentCount = tile.ComponentCount;
    var levels = tile.DecompLevels;

    // Step 1: Tier-2 packet parsing
    var codeBlocks = Tier2Decoder.ParsePackets(tileData, 0, tileData.Length, tile);

    // Step 2: Tier-1 decoding of each code-block + assembly into component coefficient planes
    var subbands = SubbandInfo.ComputeSubbands(width, height, levels);
    var pixelData = new byte[width * height * componentCount];

    for (var c = 0; c < componentCount; ++c) {
      var plane = new int[height, width];

      // Decode each code-block and place coefficients into the subband region
      foreach (var cb in codeBlocks) {
        // Find which subband this code-block belongs to for this component
        var sbIdx = cb.SubbandIndex - c * subbands.Length;
        if (sbIdx < 0 || sbIdx >= subbands.Length)
          continue;

        var sb = subbands[sbIdx];
        if (cb.CompressedData.Length == 0 || cb.NumCodingPasses == 0)
          continue;

        var cbActualW = Math.Min(tile.CodeBlockWidth, sb.Width - cb.CodeBlockX * tile.CodeBlockWidth);
        var cbActualH = Math.Min(tile.CodeBlockHeight, sb.Height - cb.CodeBlockY * tile.CodeBlockHeight);
        if (cbActualW <= 0 || cbActualH <= 0)
          continue;

        // Tier-1 decode
        var cbCoeffs = Tier1Decoder.DecodeCodeBlock(
          cb.CompressedData, cbActualW, cbActualH,
          cb.NumCodingPasses, cb.ZeroBitPlanes
        );

        // Step 3: Dequantize (reversible = no-op for integer coefficients)
        if (quantStyle == 0)
          Jp2Dequantizer.DequantizeReversible(cbCoeffs, guardBits);

        // Place into the coefficient plane at the subband offset
        var baseX = sb.OffsetX + cb.CodeBlockX * tile.CodeBlockWidth;
        var baseY = sb.OffsetY + cb.CodeBlockY * tile.CodeBlockHeight;
        for (var y = 0; y < cbActualH; ++y)
          for (var x = 0; x < cbActualW; ++x) {
            var py = baseY + y;
            var px = baseX + x;
            if (py < height && px < width)
              plane[py, px] = cbCoeffs[y, x];
          }
      }

      // Step 4: Inverse DWT (already exists)
      Jp2Wavelet.InverseMultiLevel(plane, width, height, levels);

      // Step 5: Extract pixels with level shift and clamping
      var shift = tile.BitsPerComponent > 1 ? 1 << (tile.BitsPerComponent - 1) : 0;
      var maxVal = (1 << tile.BitsPerComponent) - 1;
      for (var y = 0; y < height; ++y)
        for (var x = 0; x < width; ++x) {
          var val = plane[y, x] + shift;
          if (val < 0) val = 0;
          else if (val > maxVal) val = maxVal;
          pixelData[(y * width + x) * componentCount + c] = (byte)val;
        }
    }

    return pixelData;
  }

  /// <summary>Backward-compatible simplified format: raw big-endian int32 wavelet coefficients per component.</summary>
  private static byte[] _DecodeSimplified(byte[] tileData, int width, int height, int componentCount, int levels) {
    var coeffsPerComponent = width * height;
    var expectedSize = coeffsPerComponent * componentCount * 4;

    if (tileData.Length < expectedSize)
      throw new InvalidDataException($"Tile data too small: expected {expectedSize} bytes, got {tileData.Length}.");

    var pixelData = new byte[width * height * componentCount];
    var plane = new int[height, width];

    for (var c = 0; c < componentCount; ++c) {
      var baseOffset = c * coeffsPerComponent * 4;

      for (var y = 0; y < height; ++y)
        for (var x = 0; x < width; ++x) {
          var idx = baseOffset + (y * width + x) * 4;
          plane[y, x] = BinaryPrimitives.ReadInt32BigEndian(tileData.AsSpan(idx));
        }

      Jp2Wavelet.InverseMultiLevel(plane, width, height, levels);

      for (var y = 0; y < height; ++y)
        for (var x = 0; x < width; ++x) {
          var val = plane[y, x];
          if (val < 0) val = 0;
          else if (val > 255) val = 255;
          pixelData[(y * width + x) * componentCount + c] = (byte)val;
        }
    }

    return pixelData;
  }
}
