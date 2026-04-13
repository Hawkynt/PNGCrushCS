using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FileFormat.Heif.Codec;

namespace FileFormat.Heif;

/// <summary>Reads HEIF/HEIC files from bytes, streams, or file paths.</summary>
public static class HeifReader {

  private const int _MIN_FILE_SIZE = 12; // at least ftyp box header + 4-byte brand

  private static readonly HashSet<string> _HEIF_BRANDS = new(StringComparer.Ordinal) {
    "heic", "heix", "hevc", "heim", "heis", "hevm", "hevs", "mif1",
  };

  public static HeifFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("HEIF file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static HeifFile FromStream(Stream stream) {
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

  public static HeifFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < _MIN_FILE_SIZE)
      throw new InvalidDataException("Data too small for a valid HEIF file.");

    var bytes = data.ToArray();
    var boxes = IsoBmffBox.ReadBoxes(bytes, 0, bytes.Length);

    var ftypBox = _FindBox(boxes, IsoBmffBox.Ftyp);
    if (ftypBox == null)
      throw new InvalidDataException("Missing ftyp box; not a valid ISOBMFF file.");

    var brand = _ReadBrand(ftypBox.Value.Data);
    if (!_HEIF_BRANDS.Contains(brand))
      throw new InvalidDataException($"Unsupported major brand '{brand}'; expected a HEIF brand.");

    var width = 0;
    var height = 0;
    byte[]? rawImageData = null;
    byte[]? hvcCData = null;

    // Parse meta box for dimensions and hvcC config
    var metaBox = _FindBox(boxes, IsoBmffBox.Meta);
    if (metaBox != null)
      _ParseMetaBox(metaBox.Value.Data, ref width, ref height, ref hvcCData);

    // Extract mdat payload
    var mdatBox = _FindBox(boxes, IsoBmffBox.Mdat);
    if (mdatBox != null)
      rawImageData = mdatBox.Value.Data;

    rawImageData ??= [];

    // Try HEVC decode if we have mdat data that doesn't look like raw pixels
    var expectedPixelBytes = width * height * 3;
    byte[] pixelData;

    if (rawImageData.Length > 0 && rawImageData.Length != expectedPixelBytes && _LooksLikeHevcData(rawImageData)) {
      try {
        var (decW, decH, rgbData) = HeifHevcDecoder.Decode(hvcCData, rawImageData);
        width = decW;
        height = decH;
        pixelData = rgbData;
      } catch {
        // HEVC decode failed: return container-level data only
        pixelData = new byte[expectedPixelBytes > 0 ? expectedPixelBytes : 0];
        if (expectedPixelBytes > 0)
          rawImageData.AsSpan(0, Math.Min(rawImageData.Length, expectedPixelBytes)).CopyTo(pixelData.AsSpan(0));
      }
    } else {
      // Raw uncompressed or fallback
      pixelData = new byte[expectedPixelBytes > 0 ? expectedPixelBytes : 0];
      if (expectedPixelBytes > 0)
        rawImageData.AsSpan(0, Math.Min(rawImageData.Length, expectedPixelBytes)).CopyTo(pixelData.AsSpan(0));
    }

    return new HeifFile {
      Width = width,
      Height = height,
      PixelData = pixelData,
      Brand = brand,
      RawImageData = rawImageData,
    };
  }

  public static HeifFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  /// <summary>Checks whether data looks like HEVC NAL unit data (length-prefixed or Annex B).</summary>
  private static bool _LooksLikeHevcData(byte[] data) {
    if (data.Length < 4)
      return false;

    // Check for Annex B start code
    if (data[0] == 0 && data[1] == 0 && (data[2] == 1 || (data[2] == 0 && data.Length > 3 && data[3] == 1)))
      return true;

    // Check for length-prefixed NAL: first 4 bytes as BE length should be reasonable
    var length = BinaryPrimitives.ReadUInt32BigEndian(data);
    return length > 0 && length < (uint)data.Length;
  }

  private static string _ReadBrand(byte[] ftypData) {
    if (ftypData.Length < 4)
      return string.Empty;

    return Encoding.ASCII.GetString(ftypData.AsSpan(0, 4));
  }

  private static void _ParseMetaBox(byte[] data, ref int width, ref int height, ref byte[]? hvcCData) {
    // meta is a FullBox: skip version (1 byte) + flags (3 bytes)
    if (data.Length < 4)
      return;

    var subBoxes = IsoBmffBox.ReadBoxes(data, 4, data.Length - 4);
    foreach (var box in subBoxes) {
      if (box.Type == IsoBmffBox.Iprp)
        _ParseIprpBox(box.Data, ref width, ref height, ref hvcCData);
    }
  }

  private static void _ParseIprpBox(byte[] data, ref int width, ref int height, ref byte[]? hvcCData) {
    var subBoxes = IsoBmffBox.ReadBoxes(data, 0, data.Length);
    foreach (var box in subBoxes) {
      if (box.Type == IsoBmffBox.Ipco)
        _ParseIpcoBox(box.Data, ref width, ref height, ref hvcCData);
    }
  }

  private static void _ParseIpcoBox(byte[] data, ref int width, ref int height, ref byte[]? hvcCData) {
    var subBoxes = IsoBmffBox.ReadBoxes(data, 0, data.Length);
    foreach (var box in subBoxes) {
      if (box.Type == IsoBmffBox.Ispe && box.Data.Length >= 12) {
        width = (int)BinaryPrimitives.ReadUInt32BigEndian(box.Data.AsSpan(4));
        height = (int)BinaryPrimitives.ReadUInt32BigEndian(box.Data.AsSpan(8));
      } else if (box.Type == IsoBmffBox.HvcC) {
        hvcCData = box.Data;
      }
    }
  }

  private static IsoBmffBox? _FindBox(List<IsoBmffBox> boxes, string type) {
    foreach (var box in boxes)
      if (box.Type == type)
        return box;

    return null;
  }
}
