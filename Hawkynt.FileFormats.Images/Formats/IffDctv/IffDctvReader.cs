using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Ilbm;

namespace FileFormat.IffDctv;

/// <summary>Reads IFF DCTV (Digital Composite Television) images from bytes, streams, or file paths.</summary>
/// <remarks>
/// A DCTV file is an ordinary IFF ILBM, so there is no magic number to go on beyond the top line: the
/// synchronisation sequence in the first row is the only thing that distinguishes a DCTV screen from
/// a picture that merely looks like television static. That check is what this reader keys off.
/// </remarks>
public static class IffDctvReader {

  public static IffDctvFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("DCTV file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static IffDctvFile FromStream(Stream stream) {
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

  public static IffDctvFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static IffDctvFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < IffDctvFile.MinFileSize)
      throw new InvalidDataException($"Invalid DCTV data: expected at least {IffDctvFile.MinFileSize} bytes, got {data.Length}.");

    if (!data[..4].SequenceEqual("FORM"u8) || !data.Slice(8, 4).SequenceEqual("ILBM"u8))
      throw new InvalidDataException("Invalid DCTV data: a DCTV picture is carried in an IFF ILBM FORM.");

    var width = 0;
    var height = 0;
    var planes = 0;
    var compression = 0;
    var colourMap = new int[256];
    var haveColourMap = false;
    byte[]? body = null;

    for (var offset = 12; offset + 8 <= data.Length;) {
      var id = data.Slice(offset, 4);
      var length = (int)BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset + 4, 4));
      var payload = offset + 8;
      if (length < 0 || payload + length > data.Length)
        length = data.Length - payload;

      if (id.SequenceEqual("BMHD"u8) && length >= 20) {
        width = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(payload, 2));
        height = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(payload + 2, 2));
        planes = data[payload + 8];
        compression = data[payload + 10];
      } else if (id.SequenceEqual("CMAP"u8)) {
        _ReadColourMap(data.Slice(payload, length), colourMap);
        haveColourMap = true;
      } else if (id.SequenceEqual("BODY"u8)) {
        body = data.Slice(payload, length).ToArray();
      }

      offset = payload + length + (length & 1);
    }

    if (width <= 0 || height <= 0 || planes is < 1 or > 8)
      throw new InvalidDataException("Invalid DCTV data: the ILBM bitmap header is missing or unusable.");

    if (width < IffDctvCodec.MinimumWidth || width > IffDctvCodec.MaximumWidth)
      throw new InvalidDataException($"Invalid DCTV data: {width} pixels is outside the width a DCTV screen can have.");

    if (body is null)
      throw new InvalidDataException("Invalid DCTV data: no BODY chunk.");

    if (!haveColourMap)
      throw new InvalidDataException("Invalid DCTV data: no CMAP chunk, so the sample levels cannot be recovered.");

    var bytesPerPlane = (width + 15) >> 4 << 1;
    var bytesPerRow = bytesPerPlane * planes;
    var planar = compression switch {
      0 => body,
      1 => ByteRun1Compressor.Decode(body, bytesPerRow * height),
      _ => throw new InvalidDataException($"Invalid DCTV data: BODY compression {compression} is not one a DCTV file uses."),
    };

    if (planar.Length < bytesPerRow * height)
      throw new InvalidDataException("Invalid DCTV data: the BODY chunk is shorter than the bitmap it describes.");

    var samples = _ToSamples(planar, colourMap, width, height, planes, bytesPerPlane, bytesPerRow);

    if (!IffDctvCodec.IsSignatureLine(samples.AsSpan(0, width)))
      throw new InvalidDataException("Invalid DCTV data: the first line does not carry the DCTV synchronisation sequence.");

    // Two signature lines mean an interlaced screen and square pixels; one means a non-interlaced
    // one whose rows each cover two scanlines. CAMG says so as well, but the lines are what the
    // reconstruction actually counts, so they are what is counted here.
    var interlaced = height > 1 && IffDctvCodec.IsSignatureLine(samples.AsSpan(width, width));

    if (height < (interlaced ? 3 : 2))
      throw new InvalidDataException("Invalid DCTV data: the screen holds nothing but signature lines.");

    return new() {
      Width = width,
      ContentHeight = height,
      Interlaced = interlaced,
      PlaneCount = planes,
      Samples = samples,
    };
  }

  private static void _ReadColourMap(ReadOnlySpan<byte> chunk, int[] colourMap) {
    var colours = Math.Min(chunk.Length / 3, colourMap.Length);

    // An Amiga colour map holding only high nibbles is an OCS palette and each gun is replicated into
    // its low nibble; that is where the bits the sample nibble is read from end up.
    var ocs = true;
    for (var c = 0; c < colours && ocs; ++c)
      ocs = (chunk[c * 3] & 15) == 0 && (chunk[c * 3 + 1] & 15) == 0 && (chunk[c * 3 + 2] & 15) == 0;

    for (var c = 0; c < colours; ++c) {
      var rgb = chunk[c * 3] << 16 | chunk[c * 3 + 1] << 8 | chunk[c * 3 + 2];
      colourMap[c] = ocs ? rgb | rgb >> 4 : rgb;
    }
  }

  private static byte[] _ToSamples(byte[] planar, int[] colourMap, int width, int height, int planes, int bytesPerPlane, int bytesPerRow) {
    var samples = new byte[width * height];
    for (var y = 0; y < height; ++y) {
      var row = y * bytesPerRow;
      for (var x = 0; x < width; ++x) {
        var bit = 7 - (x & 7);
        var index = 0;
        for (var plane = planes - 1; plane >= 0; --plane)
          index = index << 1 | (planar[row + plane * bytesPerPlane + (x >> 3)] >> bit & 1);

        samples[y * width + x] = IffDctvCodec.NibbleFromColour(colourMap[index]);
      }
    }

    return samples;
  }
}
