using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Avif.Codec;

namespace FileFormat.Avif;

/// <summary>Reads AVIF files from bytes, streams, or file paths.</summary>
public static class AvifReader {

  private const int _MIN_FILE_SIZE = 12;
  private const string _AVIF_BRAND = "avif";
  private const string _AVIS_BRAND = "avis";

  public static AvifFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("AVIF file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AvifFile FromStream(Stream stream) {
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

  public static AvifFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static AvifFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < _MIN_FILE_SIZE)
      throw new InvalidDataException("Data too small for a valid AVIF file.");

    var boxes = IsoBmffBox.ReadBoxes(data, 0, data.Length);

    var ftypBox = boxes.FirstOrDefault(b => b.Type == IsoBmffBox.Ftyp)
                  ?? throw new InvalidDataException("Missing ftyp box.");

    var brand = _ReadBrand(ftypBox.Data);
    if (brand != _AVIF_BRAND && brand != _AVIS_BRAND)
      throw new InvalidDataException($"Invalid AVIF brand: '{brand}'.");

    var width = 0;
    var height = 0;

    var metaBox = boxes.FirstOrDefault(b => b.Type == IsoBmffBox.Meta);
    if (metaBox != null)
      _ParseMetaBox(metaBox.Data, ref width, ref height);

    var rawImageData = Array.Empty<byte>();
    var mdatBox = boxes.FirstOrDefault(b => b.Type == IsoBmffBox.Mdat);
    if (mdatBox != null)
      rawImageData = mdatBox.Data;

    var pixelData = Array.Empty<byte>();
    var expectedPixelBytes = width * height * 3;
    if (expectedPixelBytes > 0 && rawImageData.Length == expectedPixelBytes) {
      // Raw uncompressed pixel data (round-trip from our writer)
      pixelData = new byte[expectedPixelBytes];
      rawImageData.AsSpan(0, expectedPixelBytes).CopyTo(pixelData.AsSpan(0));
    } else if (rawImageData.Length > 0 && _LooksLikeAv1Bitstream(rawImageData)) {
      // Attempt AV1 decode
      try {
        var (decW, decH, rgbData) = Av1FrameDecoder.Decode(rawImageData, 0, rawImageData.Length);
        width = decW;
        height = decH;
        pixelData = rgbData;
      } catch {
        // AV1 decode failed: return container-level data only
        pixelData = new byte[expectedPixelBytes > 0 ? expectedPixelBytes : 0];
      }
    }

    return new AvifFile {
      Width = width,
      Height = height,
      PixelData = pixelData,
      Brand = brand,
      RawImageData = rawImageData,
    };
  }

  /// <summary>Checks whether the data starts with a valid AV1 OBU header.</summary>
  private static bool _LooksLikeAv1Bitstream(byte[] data) {
    if (data.Length < 2)
      return false;

    // Check that forbidden bit is 0 and OBU type is valid
    var header = data[0];
    if ((header & 0x80) != 0)
      return false;

    var obuType = (header >> 3) & 0x0F;
    // Sequence header (1) or temporal delimiter (2) typically comes first
    return obuType is >= 1 and <= 8 or 15;
  }

  private static string _ReadBrand(byte[] ftypData) {
    if (ftypData.Length < 4)
      throw new InvalidDataException("Invalid ftyp box data.");

    var chars = new char[4];
    for (var i = 0; i < 4; ++i)
      chars[i] = (char)ftypData[i];

    return new(chars);
  }

  private static void _ParseMetaBox(byte[] data, ref int width, ref int height) {
    if (data.Length < 4)
      return;

    // meta is a full box: 4 bytes version/flags before children
    var childBoxes = IsoBmffBox.ReadBoxes(data, 4, data.Length - 4);

    foreach (var child in childBoxes) {
      if (child.Type == IsoBmffBox.Iprp)
        _ParseIprpBox(child.Data, ref width, ref height);
    }
  }

  private static void _ParseIprpBox(byte[] data, ref int width, ref int height) {
    var childBoxes = IsoBmffBox.ReadBoxes(data, 0, data.Length);

    foreach (var child in childBoxes) {
      if (child.Type == IsoBmffBox.Ipco)
        _ParseIpcoBox(child.Data, ref width, ref height);
    }
  }

  private static void _ParseIpcoBox(byte[] data, ref int width, ref int height) {
    var childBoxes = IsoBmffBox.ReadBoxes(data, 0, data.Length);

    foreach (var child in childBoxes) {
      if (child.Type == IsoBmffBox.Ispe && child.Data.Length >= 12) {
        // ispe is a full box: 4 bytes version/flags, then 4 bytes width, 4 bytes height
        width = (int)BinaryPrimitives.ReadUInt32BigEndian(child.Data.AsSpan(4));
        height = (int)BinaryPrimitives.ReadUInt32BigEndian(child.Data.AsSpan(8));
      }
    }
  }
}
