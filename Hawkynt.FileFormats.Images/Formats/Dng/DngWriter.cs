using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace FileFormat.Dng;

/// <summary>Assembles DNG (Adobe Digital Negative) file bytes: a single uncompressed IFD, little-endian.</summary>
/// <remarks>
/// A DNG is a TIFF, but not every TIFF is a DNG. The specification makes three things mandatory
/// beyond what TIFF asks: the version, a non-empty camera model, and a colour matrix saying what the
/// camera's own primaries were. What this wrote before had the version, an empty model and no matrix
/// at all — a plain RGB TIFF with two extra tags, which lenient readers open as a TIFF and strict
/// ones refuse as a DNG.
/// </remarks>
public static class DngWriter {

  private const ushort _TAG_NEW_SUBFILE_TYPE = 254;
  private const ushort _TAG_IMAGE_WIDTH = 256;
  private const ushort _TAG_IMAGE_LENGTH = 257;
  private const ushort _TAG_BITS_PER_SAMPLE = 258;
  private const ushort _TAG_COMPRESSION = 259;
  private const ushort _TAG_PHOTOMETRIC_INTERPRETATION = 262;
  private const ushort _TAG_STRIP_OFFSETS = 273;
  private const ushort _TAG_SAMPLES_PER_PIXEL = 277;
  private const ushort _TAG_ROWS_PER_STRIP = 278;
  private const ushort _TAG_STRIP_BYTE_COUNTS = 279;
  private const ushort _TAG_DNG_VERSION = 50706;
  private const ushort _TAG_DNG_BACKWARD_VERSION = 50707;
  private const ushort _TAG_UNIQUE_CAMERA_MODEL = 50708;
  private const ushort _TAG_COLOR_MATRIX_1 = 50721;
  private const ushort _TAG_AS_SHOT_NEUTRAL = 50728;
  private const ushort _TAG_CALIBRATION_ILLUMINANT_1 = 50778;

  private const ushort _TYPE_BYTE = 1;
  private const ushort _TYPE_ASCII = 2;
  private const ushort _TYPE_SHORT = 3;
  private const ushort _TYPE_LONG = 4;
  private const ushort _TYPE_RATIONAL = 5;
  private const ushort _TYPE_SRATIONAL = 10;

  /// <summary>What the model field says when the caller names nothing.</summary>
  /// <remarks>
  /// It may not be empty: the specification uses it to tell one camera's calibration from another's,
  /// and a reader that finds nothing there has no way to interpret the matrix beside it.
  /// </remarks>
  private const string _DefaultCameraModel = "Linear DNG";

  /// <summary>D65, which is what the matrix below is stated against.</summary>
  private const ushort _IlluminantD65 = 21;

  /// <summary>The denominator every rational here is written over.</summary>
  private const int _RationalScale = 10000;

  /// <summary>
  /// The matrix taking CIE XYZ to the picture's own primaries, which for a picture that never came
  /// off a sensor are sRGB's.
  /// </summary>
  private static readonly int[] _ColorMatrixSrgb = [
     32406, -15372,  -4986,
     -9689,  18758,    415,
       557,  -2040,  10570,
  ];

  public static byte[] ToBytes(DngFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var samplesPerPixel = file.SamplesPerPixel;
    var bitsPerSample = file.BitsPerSample;
    var width = file.Width;
    var height = file.Height;
    var model = string.IsNullOrEmpty(file.CameraModel) ? _DefaultCameraModel : file.CameraModel;
    var modelBytes = Encoding.ASCII.GetBytes(model + '\0');

    var photometric = (ushort)file.Photometric;
    if (photometric == 0)
      photometric = (ushort)(samplesPerPixel == 1 ? 1 : 2);

    var version = file.DngVersion is { Length: >= 4 } stated ? stated : [1, 4, 0, 0];

    // Everything that does not fit a tag's four-byte value field goes into one block after the IFD,
    // and each entry records where in that block its own piece landed.
    var extra = new List<byte>();
    var entries = new List<(ushort Tag, ushort Type, uint Count, uint Value, bool IsOffset)>();

    void Inline(ushort tag, ushort type, uint count, uint value) => entries.Add((tag, type, count, value, false));

    void Block(ushort tag, ushort type, uint count, ReadOnlySpan<byte> bytes) {
      // Every block starts on an even offset, which is what TIFF asks of anything it points at.
      if ((extra.Count & 1) != 0)
        extra.Add(0);

      entries.Add((tag, type, count, (uint)extra.Count, true));
      foreach (var b in bytes)
        extra.Add(b);
    }

    Inline(_TAG_NEW_SUBFILE_TYPE, _TYPE_LONG, 1, 0);
    Inline(_TAG_IMAGE_WIDTH, _TYPE_LONG, 1, (uint)width);
    Inline(_TAG_IMAGE_LENGTH, _TYPE_LONG, 1, (uint)height);

    if (samplesPerPixel <= 2) {
      uint packed = 0;
      for (var i = 0; i < samplesPerPixel; ++i)
        packed |= (uint)bitsPerSample << (i * 16);

      Inline(_TAG_BITS_PER_SAMPLE, _TYPE_SHORT, (uint)samplesPerPixel, packed);
    } else {
      var bits = new byte[samplesPerPixel * 2];
      for (var i = 0; i < samplesPerPixel; ++i)
        BinaryPrimitives.WriteUInt16LittleEndian(bits.AsSpan(i * 2), (ushort)bitsPerSample);

      Block(_TAG_BITS_PER_SAMPLE, _TYPE_SHORT, (uint)samplesPerPixel, bits);
    }

    Inline(_TAG_COMPRESSION, _TYPE_SHORT, 1, 1);
    Inline(_TAG_PHOTOMETRIC_INTERPRETATION, _TYPE_SHORT, 1, photometric);

    // Filled in once the layout is settled; the strip is the last thing in the file.
    Inline(_TAG_STRIP_OFFSETS, _TYPE_LONG, 1, 0);

    Inline(_TAG_SAMPLES_PER_PIXEL, _TYPE_SHORT, 1, (uint)samplesPerPixel);
    Inline(_TAG_ROWS_PER_STRIP, _TYPE_LONG, 1, (uint)height);

    var bytesPerPixel = samplesPerPixel * (bitsPerSample / 8);
    var totalPixelBytes = width * height * bytesPerPixel;
    Inline(_TAG_STRIP_BYTE_COUNTS, _TYPE_LONG, 1, (uint)totalPixelBytes);

    Inline(_TAG_DNG_VERSION, _TYPE_BYTE, 4,
      (uint)(version[0] | (version[1] << 8) | (version[2] << 16) | (version[3] << 24)));
    Inline(_TAG_DNG_BACKWARD_VERSION, _TYPE_BYTE, 4, 1u | (4u << 8));

    if (modelBytes.Length <= 4) {
      uint packed = 0;
      for (var i = 0; i < modelBytes.Length; ++i)
        packed |= (uint)modelBytes[i] << (i * 8);

      Inline(_TAG_UNIQUE_CAMERA_MODEL, _TYPE_ASCII, (uint)modelBytes.Length, packed);
    } else
      Block(_TAG_UNIQUE_CAMERA_MODEL, _TYPE_ASCII, (uint)modelBytes.Length, modelBytes);

    var matrix = new byte[_ColorMatrixSrgb.Length * 8];
    for (var i = 0; i < _ColorMatrixSrgb.Length; ++i) {
      BinaryPrimitives.WriteInt32LittleEndian(matrix.AsSpan(i * 8), _ColorMatrixSrgb[i]);
      BinaryPrimitives.WriteInt32LittleEndian(matrix.AsSpan(i * 8 + 4), _RationalScale);
    }

    Block(_TAG_COLOR_MATRIX_1, _TYPE_SRATIONAL, (uint)_ColorMatrixSrgb.Length, matrix);

    // The white point, which for a picture already in sRGB is no correction at all.
    var neutral = new byte[3 * 8];
    for (var i = 0; i < 3; ++i) {
      BinaryPrimitives.WriteUInt32LittleEndian(neutral.AsSpan(i * 8), 1);
      BinaryPrimitives.WriteUInt32LittleEndian(neutral.AsSpan(i * 8 + 4), 1);
    }

    Block(_TAG_AS_SHOT_NEUTRAL, _TYPE_RATIONAL, 3, neutral);
    Inline(_TAG_CALIBRATION_ILLUMINANT_1, _TYPE_SHORT, 1, _IlluminantD65);

    // TIFF wants the entries in ascending tag order, and readers rely on it to find one quickly.
    entries.Sort((a, b) => a.Tag.CompareTo(b.Tag));

    const int ifdOffset = 8;
    var ifdSize = 2 + entries.Count * 12 + 4;
    var extraOffset = ifdOffset + ifdSize;
    var pixelDataOffset = extraOffset + extra.Count;
    if ((pixelDataOffset & 1) != 0)
      ++pixelDataOffset;

    var result = new byte[pixelDataOffset + totalPixelBytes];
    var span = result.AsSpan();

    result[0] = (byte)'I';
    result[1] = (byte)'I';
    BinaryPrimitives.WriteUInt16LittleEndian(span[2..], 42);
    BinaryPrimitives.WriteUInt32LittleEndian(span[4..], ifdOffset);

    var strip = entries.FindIndex(e => e.Tag == _TAG_STRIP_OFFSETS);
    entries[strip] = entries[strip] with { Value = (uint)pixelDataOffset };

    var pos = ifdOffset;
    BinaryPrimitives.WriteUInt16LittleEndian(span[pos..], (ushort)entries.Count);
    pos += 2;

    foreach (var (tag, type, count, value, isOffset) in entries) {
      BinaryPrimitives.WriteUInt16LittleEndian(span[pos..], tag);
      BinaryPrimitives.WriteUInt16LittleEndian(span[(pos + 2)..], type);
      BinaryPrimitives.WriteUInt32LittleEndian(span[(pos + 4)..], count);

      if (isOffset)
        BinaryPrimitives.WriteUInt32LittleEndian(span[(pos + 8)..], value + (uint)extraOffset);
      else if (type == _TYPE_SHORT && count == 1)
        BinaryPrimitives.WriteUInt16LittleEndian(span[(pos + 8)..], (ushort)value);
      else
        BinaryPrimitives.WriteUInt32LittleEndian(span[(pos + 8)..], value);

      pos += 12;
    }

    BinaryPrimitives.WriteUInt32LittleEndian(span[pos..], 0);
    extra.CopyTo(result, extraOffset);

    var pixelData = file.PixelData ?? [];
    pixelData.AsSpan(0, Math.Min(totalPixelBytes, pixelData.Length)).CopyTo(result.AsSpan(pixelDataOffset));

    return result;
  }
}
