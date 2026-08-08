using System;
using FileFormat.Core;

namespace FileFormat.Pl4Picture;

/// <summary>Assembles PL4 bytes from a <see cref="Pl4PictureFile"/>.</summary>
/// <remarks>
/// The pair of screens is wrapped in a single stored LZ4 block rather than a compressed one. The
/// frame format says a block whose length has its top bit set was not worth compressing and is kept
/// as it is, so a decoder that reads the format at all reads this — and writing a real LZ4 match
/// finder would buy space in a format whose whole point is that its packer is somebody else's.
/// </remarks>
public static class Pl4PictureWriter {

  /// <summary>The frame descriptor: version one, independent blocks, no checksums and no size.</summary>
  private const byte _DESCRIPTOR = 0x40;

  /// <summary>The block maximum size byte: four megabytes, more than a frame here ever needs.</summary>
  private const byte _BLOCK_MAXIMUM = 0x70;

  public static byte[] ToBytes(Pl4PictureFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var unpacked = file.Unpacked ?? [];
    var length = Math.Min(unpacked.Length, Pl4PictureFile.UnpackedSize);
    var data = new byte[7 + 4 + length + 4];

    Lz4Frame.Magic.CopyTo(data);
    data[4] = _DESCRIPTOR;
    data[5] = _BLOCK_MAXIMUM;

    // This reader skips the descriptor's checksum; a general LZ4 decoder refuses a frame without it.
    data[6] = (byte)(_XxHash32(data.AsSpan(4, 2)) >> 8);

    // The top bit says the block is stored rather than compressed.
    var stored = (uint)length | 0x80000000u;
    data[7] = (byte)stored;
    data[8] = (byte)(stored >> 8);
    data[9] = (byte)(stored >> 16);
    data[10] = (byte)(stored >> 24);

    unpacked.AsSpan(0, length).CopyTo(data.AsSpan(11));

    return data;
  }

  /// <summary>
  /// The hash LZ4 seals its frame descriptor with, of which only the second byte is stored.
  /// </summary>
  /// <remarks>
  /// Two bytes never reach the algorithm's sixteen-byte stride, so only its tail is needed: the
  /// length, then each remaining byte mixed in turn, then the final avalanche.
  /// </remarks>
  private static uint _XxHash32(ReadOnlySpan<byte> data) {
    const uint prime1 = 2654435761, prime2 = 2246822519, prime3 = 3266489917, prime5 = 374761393;

    var hash = prime5 + (uint)data.Length;
    foreach (var b in data)
      hash = uint.RotateLeft(hash + b * prime5, 11) * prime1;

    hash = (hash ^ (hash >> 15)) * prime2;
    hash = (hash ^ (hash >> 13)) * prime3;

    return hash ^ (hash >> 16);
  }
}
