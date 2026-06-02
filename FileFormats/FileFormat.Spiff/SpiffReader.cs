using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace FileFormat.Spiff;

public static class SpiffReader {

  private static readonly byte[] _Soi = [0xFF, 0xD8];
  private static readonly byte[] _App8 = [0xFF, 0xE8];
  private static readonly byte[] _SpiffId = "SPIFF\0"u8.ToArray();

  public static SpiffFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("SPIFF file not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static SpiffFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static SpiffFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static SpiffFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 4 + 2 + 6 + 24)
      throw new InvalidDataException("SPIFF data too small for SOI + APP8 + identifier + directory header.");

    if (!data[..2].SequenceEqual(_Soi))
      throw new InvalidDataException("Missing JPEG SOI marker (FFD8).");
    if (!data.Slice(2, 2).SequenceEqual(_App8))
      throw new InvalidDataException("Missing APP8 marker (FFE8) immediately after SOI.");

    var app8Length = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(4, 2));
    if (4 + app8Length > data.Length)
      throw new InvalidDataException("APP8 segment length exceeds file size.");

    // APP8 body starts at offset 6 (length includes the length bytes themselves).
    if (!data.Slice(6, 6).SequenceEqual(_SpiffId))
      throw new InvalidDataException("Missing 'SPIFF\\0' identifier inside APP8 segment.");

    // SPIFF directory: 24 bytes — version major (1), version minor (1), profile (1),
    // num-components (1), height (BE32), width (BE32), colour-space (1), bps (1),
    // compression-type (1), resolution-units (1), vertical-res (BE32), horizontal-res (BE32).
    var dir = data.Slice(12, 24);
    var profile = dir[2];
    var components = dir[3];
    var height = (int)BinaryPrimitives.ReadUInt32BigEndian(dir.Slice(4, 4));
    var width = (int)BinaryPrimitives.ReadUInt32BigEndian(dir.Slice(8, 4));
    var colorSpace = dir[12];
    var bps = dir[13];
    var compression = dir[14];

    var payloadStart = 4 + app8Length;
    var payload = data[payloadStart..].ToArray();

    return new SpiffFile {
      ProfileId = profile,
      ComponentCount = components,
      Width = width,
      Height = height,
      ColorSpace = colorSpace,
      BitsPerSample = bps,
      CompressionType = compression,
      CompressedPayload = payload,
    };
  }
}
