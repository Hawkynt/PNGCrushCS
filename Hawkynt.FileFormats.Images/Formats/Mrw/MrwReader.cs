using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.CameraRaw;

namespace FileFormat.Mrw;

/// <summary>Reads Minolta raw files from bytes, streams, or file paths.</summary>
public static class MrwReader {

  /// <summary>Twelve bits to a sample means two samples in every three bytes.</summary>
  private const int _SamplesPerGroup = 2;

  /// <summary>How many bytes those two samples take.</summary>
  private const int _BytesPerGroup = 3;

  /// <summary>The largest a twelve-bit sample can be.</summary>
  private const int _WhiteLevel = (1 << MrwFile.SupportedBitsPerSample) - 1;

  /// <summary>
  /// The mosaic phase, settled against the preview the file carries rather than assumed.
  /// </summary>
  private const BayerPattern _Pattern = BayerPattern.RGGB;

  public static MrwFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Minolta raw file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MrwFile FromStream(Stream stream) {
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

  public static MrwFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < MrwFile.HeaderSize || !data[..4].SequenceEqual(MrwFile.Magic))
      throw new InvalidDataException("Not a Minolta raw file: it does not open with MRM.");

    var blocksLength = (int)BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
    if (blocksLength < 0 || MrwFile.HeaderSize + (long)blocksLength > data.Length)
      throw new InvalidDataException($"A Minolta raw states {blocksLength} bytes of blocks and is {data.Length} bytes.");

    var sensorAt = MrwFile.HeaderSize + blocksLength;
    var picture = _FindBlock(data, blocksLength, MrwFile.PictureBlock)
      ?? throw new InvalidDataException("A Minolta raw describes its picture in a PRD block and this one has none.");

    if (picture.Length < MrwFile.PictureBlockSize)
      throw new InvalidDataException($"A Minolta raw PRD block is {picture.Length} bytes.");

    var block = data.Slice(picture.Offset, picture.Length);
    var sensorHeight = BinaryPrimitives.ReadUInt16BigEndian(block[8..]);
    var sensorWidth = BinaryPrimitives.ReadUInt16BigEndian(block[10..]);
    var height = BinaryPrimitives.ReadUInt16BigEndian(block[12..]);
    var width = BinaryPrimitives.ReadUInt16BigEndian(block[14..]);
    var storedBits = block[16];
    var sampleBits = block[17];

    if (width < 1 || height < 1 || sensorWidth < width || sensorHeight < height)
      throw new InvalidDataException($"A Minolta raw states a {sensorWidth} by {sensorHeight} sensor holding a {width} by {height} picture.");

    if (storedBits != MrwFile.SupportedBitsPerSample || sampleBits != MrwFile.SupportedBitsPerSample)
      throw new InvalidDataException($"A Minolta raw storing {storedBits} bits a sample is not one this reads.");

    // Two samples in every three bytes, and the sensor's whole array with nothing after it. A file
    // where this does not come out to the byte has not been walked correctly, and going on would
    // read the picture at the wrong stride.
    var samples = sensorWidth * sensorHeight;
    if (samples % _SamplesPerGroup != 0)
      throw new InvalidDataException($"A Minolta raw sensor of {sensorWidth} by {sensorHeight} does not pack evenly.");

    var expected = (long)samples / _SamplesPerGroup * _BytesPerGroup;
    if (sensorAt + expected != data.Length)
      throw new InvalidDataException(
        $"A Minolta raw sensor of {sensorWidth} by {sensorHeight} at twelve bits needs {sensorAt + expected} bytes and the file is {data.Length}.");

    var raw = _Unpack(data.Slice(sensorAt, (int)expected), samples);

    // The picture is the corner of the array the camera read, so the rows are taken at the sensor's
    // stride and cut to the picture's width.
    var cropped = new ushort[width * height];
    for (var y = 0; y < height; ++y)
      raw.AsSpan(y * sensorWidth, width).CopyTo(cropped.AsSpan(y * width));

    var whiteBalance = _WhiteBalance(data, blocksLength);
    var prepared = RawPreprocessor.Process(cropped, width, height, _Pattern, [0], _WhiteLevel, whiteBalance);
    var rgb = BayerDemosaic.Ahd(prepared, width, height, _Pattern);
    RawPostprocessor.Process(rgb, null);

    return new() {
      Width = width,
      Height = height,
      PixelData = rgb,
    };
  }

  public static MrwFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  /// <summary>Where a named block's data is, or null when the file carries no such block.</summary>
  private static (int Offset, int Length)? _FindBlock(ReadOnlySpan<byte> data, int blocksLength, ReadOnlySpan<byte> name) {
    var at = MrwFile.HeaderSize;
    var end = MrwFile.HeaderSize + blocksLength;

    while (at + MrwFile.BlockHeaderSize <= end) {
      var length = (int)BinaryPrimitives.ReadUInt32BigEndian(data[(at + 4)..]);
      if (length < 0 || at + MrwFile.BlockHeaderSize + (long)length > end)
        throw new InvalidDataException($"A Minolta raw block at {at} states {length} bytes, which its header cannot hold.");

      if (data.Slice(at, 4).SequenceEqual(name))
        return (at + MrwFile.BlockHeaderSize, length);

      at += MrwFile.BlockHeaderSize + length;
    }

    return null;
  }

  /// <summary>What the camera metered, as multipliers against green, or null when it said nothing.</summary>
  /// <remarks>
  /// Four bytes of per-channel scaling and then four sixteen-bit values, in the order red, the first
  /// green, the second green, blue. The order is worth stating because getting it wrong is not
  /// obvious from the numbers alone — it puts blue's coefficient on a green and leaves the picture
  /// looking merely as though the camera had metered badly. What settles it is that the two greens
  /// of a Bayer sensor are metered alike: read this way the file's two are both 256 and blue is 539,
  /// and read the other way the greens would differ by a factor of two.
  /// </remarks>
  private static float[]? _WhiteBalance(ReadOnlySpan<byte> data, int blocksLength) {
    if (_FindBlock(data, blocksLength, MrwFile.WhiteBalanceBlock) is not { } found || found.Length < 12)
      return null;

    var block = data.Slice(found.Offset, found.Length);
    var red = BinaryPrimitives.ReadUInt16BigEndian(block[4..]);
    var greenOne = BinaryPrimitives.ReadUInt16BigEndian(block[6..]);
    var greenTwo = BinaryPrimitives.ReadUInt16BigEndian(block[8..]);
    var blue = BinaryPrimitives.ReadUInt16BigEndian(block[10..]);

    var green = (greenOne + greenTwo) / 2.0f;
    if (red < 1 || blue < 1 || green < 1)
      return null;

    return [red / green, 1.0f, blue / green];
  }

  /// <summary>Twelve-bit samples, most significant bits first, two to every three bytes.</summary>
  private static ushort[] _Unpack(ReadOnlySpan<byte> packed, int samples) {
    var result = new ushort[samples];

    var at = 0;
    for (var i = 0; i + 1 < samples; i += _SamplesPerGroup, at += _BytesPerGroup) {
      var first = packed[at];
      var middle = packed[at + 1];
      var last = packed[at + 2];
      result[i] = (ushort)((first << 4) | (middle >> 4));
      result[i + 1] = (ushort)(((middle & 0x0F) << 8) | last);
    }

    return result;
  }
}
