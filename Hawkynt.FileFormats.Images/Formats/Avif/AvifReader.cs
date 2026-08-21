using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Avif.Codec;

namespace FileFormat.Avif;

/// <summary>Reads AVIF files from bytes, streams, or file paths.</summary>
/// <remarks>
/// The AV1 path refuses, and the reason is settled rather than suspected.
///
/// This file used to say that three AVIF samples decoded to the right size and the wrong picture —
/// mean channel error 107, 127 and 127 of 255 against ImageMagick, which agrees with XnView exactly
/// on all three — and that the decoder under <c>Codec</c> was left in place because three thousand
/// lines of entropy coding, transforms, intra prediction and loop filtering "may be close to right".
/// It asked for somebody to step through one of those files against a reference decoder rather than
/// judge it from outside. That has now been done, and the answer removes the reason for keeping it.
///
/// A 32x32 still AVIF written by libaom was decoded here and by the reference decoder. The reference
/// luma begins 74, 74, 74, 74, 74, 74, 74, 74, 146, 146. This decoder returns a flat 130 across the
/// whole plane: 1024 samples of 1024 wrong, with no structure at all. That is not a rounding fault
/// with a picture underneath it.
///
/// The cause is structural. AV1 codes its syntax elements with context-indexed cumulative
/// distribution functions; this decoder reads plain equal-probability literal bits for the partition
/// type, the intra mode and the coefficients, collapses every non-square partition to a split, and
/// fixes the transform to DCT-DCT whatever the stream signals. The normative default CDF tables the
/// format requires — several thousand values across partition, mode, skip, transform size and
/// coefficient contexts — are not present anywhere in the directory. Reading uniform bits out of an
/// arithmetic-coded stream desynchronises at the first partition decision, so everything after it is
/// noise, and there is no partial credit to repair.
///
/// So it refuses. A caller can act on a refusal and can do nothing at all with a plausible wrong
/// picture, which is the worst shape a defect takes in this library. The dead decoder is left in the
/// tree rather than deleted because its container-side parsing — OBU, sequence header, frame header
/// — is worth keeping for whoever builds the real one, and <c>Hawkynt.FileFormats.Video</c> is where
/// that belongs.
/// </remarks>
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
      return FromSpan(data);
    }
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromSpan(ms.ToArray());
  }

  public static AvifFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < _MIN_FILE_SIZE)
      throw new InvalidDataException("Data too small for a valid AVIF file.");

    var bytes = data.ToArray();
    var boxes = IsoBmffBox.ReadBoxes(bytes, 0, bytes.Length);

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
    } else if (rawImageData.Length > 0 && _LooksLikeAv1Bitstream(rawImageData))

      // The decoder under Codec does not implement AV1's entropy layer — it reads equal-probability
      // literal bits where the format uses context-indexed CDFs, and carries none of the normative
      // default tables — so it desynchronises at the first partition decision. Measured against the
      // reference on a 32x32 still: 1024 samples of 1024 wrong, a flat 130 where the reference has
      // structure. It does not fail while running, which is what makes it dangerous; it returns a
      // picture, and nothing downstream can tell that picture from a real one.
      //
      // Refusing is the same call already made for HEIF and for the zero-fill that used to sit here.
      // A caller can act on a refusal and can do nothing with a plausible wrong picture.
      throw new NotSupportedException(
        $"AVIF: the picture is AV1-coded. The container is readable — {width}x{height} — but this "
        + "library has no working AV1 decoder: the one present reads uniform bits from an "
        + "arithmetic-coded stream and produces a uniform field rather than a picture. It is "
        + "refused rather than handed back, because a wrong picture nothing announces is worse "
        + "than no picture.");

    else if (rawImageData.Length > 0)

      // Neither our own uncompressed payload nor anything recognisable as AV1. Falling through left
      // PixelData empty while Width and Height still stated a picture, which reads downstream as a
      // successful decode of nothing.
      throw new NotSupportedException(
        $"AVIF: the item's payload is {rawImageData.Length} bytes, which is neither an uncompressed "
        + $"raster for {width}x{height} nor an AV1 bitstream this reader recognises.");

    return new AvifFile {
      Width = width,
      Height = height,
      PixelData = pixelData,
      Brand = brand,
      RawImageData = rawImageData,
    };
  }

  public static AvifFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
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
