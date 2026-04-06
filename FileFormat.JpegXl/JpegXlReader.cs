using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.JpegXl.Codec;

namespace FileFormat.JpegXl;

/// <summary>Reads JPEG XL files from bytes, streams, or file paths.</summary>
public static class JpegXlReader {

  /// <summary>Bare codestream signature: FF 0A.</summary>
  private const byte _CodestreamByte0 = 0xFF;
  private const byte _CodestreamByte1 = 0x0A;

  /// <summary>ISOBMFF brand for JPEG XL: "jxl " (0x6A786C20).</summary>
  private static readonly byte[] _JxlBrand = [(byte)'j', (byte)'x', (byte)'l', (byte)' '];

  /// <summary>Minimum size: at least an ftyp box header (12 bytes) or bare codestream (4 bytes).</summary>
  private const int _MinSize = 4;

  public static JpegXlFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("JPEG XL file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static JpegXlFile FromStream(Stream stream) {
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

  public static JpegXlFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static JpegXlFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < _MinSize)
      throw new InvalidDataException("Data too small for a valid JPEG XL file.");

    // Detect bare codestream (FF 0A) vs ISOBMFF container
    if (data[0] == _CodestreamByte0 && data[1] == _CodestreamByte1)
      return _ParseCodestream(data, 0, data.Length, "jxl ");

    // Try ISOBMFF container: look for ftyp box
    return _ParseContainer(data);
  }

  private static JpegXlFile _ParseContainer(byte[] data) {
    if (data.Length < 12)
      throw new InvalidDataException("Data too small for a valid JPEG XL ISOBMFF container.");

    // Read ftyp box
    var ftypSize = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(0, 4));
    var ftypType = data.AsSpan(4, 4);

    if (ftypType[0] != (byte)'f' || ftypType[1] != (byte)'t' || ftypType[2] != (byte)'y' || ftypType[3] != (byte)'p')
      throw new InvalidDataException("Expected ftyp box at start of JPEG XL container.");

    if (ftypSize < 12 || ftypSize > data.Length)
      throw new InvalidDataException("Invalid ftyp box size.");

    // Validate brand
    var brand = data.AsSpan(8, 4);
    if (brand[0] != _JxlBrand[0] || brand[1] != _JxlBrand[1] || brand[2] != _JxlBrand[2] || brand[3] != _JxlBrand[3])
      throw new InvalidDataException("Invalid JPEG XL brand in ftyp box.");

    var brandStr = System.Text.Encoding.ASCII.GetString(data, 8, 4);

    // Find jxlc or jxlp box after ftyp
    var offset = ftypSize;
    byte[]? codestream = null;
    using var codestreamBuilder = new MemoryStream();

    while (offset + 8 <= data.Length) {
      var boxSize = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4));
      var boxType = data.AsSpan(offset + 4, 4);

      if (boxSize < 8)
        break;

      if (offset + boxSize > data.Length)
        break;

      var isJxlc = boxType[0] == (byte)'j' && boxType[1] == (byte)'x' && boxType[2] == (byte)'l' && boxType[3] == (byte)'c';
      var isJxlp = boxType[0] == (byte)'j' && boxType[1] == (byte)'x' && boxType[2] == (byte)'l' && boxType[3] == (byte)'p';

      if (isJxlc) {
        var payloadOffset = offset + 8;
        var payloadSize = boxSize - 8;
        codestream = new byte[payloadSize];
        data.AsSpan(payloadOffset, payloadSize).CopyTo(codestream.AsSpan(0));
        break;
      }

      if (isJxlp) {
        // jxlp boxes have a 4-byte sequence number before the payload
        var payloadOffset = offset + 12;
        var payloadSize = boxSize - 12;
        if (payloadSize > 0)
          codestreamBuilder.Write(data, payloadOffset, payloadSize);
      }

      offset += boxSize;
    }

    if (codestream == null) {
      if (codestreamBuilder.Length > 0)
        codestream = codestreamBuilder.ToArray();
      else
        throw new InvalidDataException("No jxlc or jxlp box found in JPEG XL container.");
    }

    return _ParseCodestream(codestream, 0, codestream.Length, brandStr);
  }

  private static JpegXlFile _ParseCodestream(byte[] data, int offset, int length, string brand) {
    if (length < 4)
      throw new InvalidDataException("Codestream too small.");

    if (data[offset] != _CodestreamByte0 || data[offset + 1] != _CodestreamByte1)
      throw new InvalidDataException("Invalid JPEG XL codestream signature.");

    // Parse SizeHeader starting after the 2-byte signature
    var sizeHeaderData = data.AsSpan(offset + 2, length - 2);
    var (width, height, bytesConsumed) = JpegXlSizeHeader.Decode(sizeHeaderData);

    // The remaining bytes after signature + size header are the frame data
    var frameDataOffset = offset + 2 + bytesConsumed;
    var frameDataLength = length - 2 - bytesConsumed;

    if (frameDataLength <= 0)
      return new JpegXlFile {
        Width = width,
        Height = height,
        ComponentCount = 3,
        PixelData = [],
        Brand = brand,
      };

    // Read component count byte (our format marker)
    var componentCount = data[frameDataOffset];
    if (componentCount != 1 && componentCount != 3)
      componentCount = 3;

    var encodedDataOffset = frameDataOffset + 1;
    var encodedDataLength = frameDataLength - 1;

    if (encodedDataLength <= 0)
      return new JpegXlFile {
        Width = width,
        Height = height,
        ComponentCount = componentCount,
        PixelData = [],
        Brand = brand,
      };

    // Check the encoding marker: 0x4D = 'M' for modular codec, otherwise raw
    byte[] pixelData;
    if (encodedDataLength > 1 && data[encodedDataOffset] == 0x4D) {
      // Modular codec encoded data
      var codecData = new byte[encodedDataLength - 1];
      Array.Copy(data, encodedDataOffset + 1, codecData, 0, codecData.Length);

      pixelData = JxlFrameDecoder.DecodeFrame(codecData, 0, width, height, componentCount, 8);
    } else {
      // Raw pixel data (legacy/fallback path)
      pixelData = new byte[encodedDataLength];
      Array.Copy(data, encodedDataOffset, pixelData, 0, encodedDataLength);
    }

    return new JpegXlFile {
      Width = width,
      Height = height,
      ComponentCount = componentCount,
      PixelData = pixelData,
      Brand = brand,
    };
  }
}
