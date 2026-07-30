using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Rla;

namespace FileFormat.Rla.Tests;

[TestFixture]
public sealed class RlaReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => RlaReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => RlaReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".rla"));
    Assert.Throws<FileNotFoundException>(() => RlaReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => RlaReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => RlaReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb_ParsesCorrectly() {
    var data = _BuildMinimalRla(4, 3, 3, 0, 8);
    var result = RlaReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(3));
    Assert.That(result.NumChannels, Is.EqualTo(3));
    Assert.That(result.NumMatte, Is.EqualTo(0));
    Assert.That(result.NumBits, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WithMatte_ParsesChannelCount() {
    var data = _BuildMinimalRla(2, 2, 3, 1, 8);
    var result = RlaReader.FromBytes(data);

    Assert.That(result.NumChannels, Is.EqualTo(3));
    Assert.That(result.NumMatte, Is.EqualTo(1));
  }

  private static byte[] _BuildMinimalRla(int width, int height, int numChannels, int numMatte, int numBits) {
    var totalChannels = numChannels + numMatte;
    var bytesPerChannel = numBits <= 8 ? 1 : 2;
    var scanlineChannelSize = width * bytesPerChannel;

    // Build pixel data and compress each channel per scanline
    var compressedChunks = new byte[height * totalChannels][];
    for (var row = 0; row < height; ++row)
      for (var ch = 0; ch < totalChannels; ++ch) {
        var scanline = new byte[scanlineChannelSize];
        for (var j = 0; j < scanlineChannelSize; ++j)
          scanline[j] = (byte)((row * totalChannels + ch + j) * 7 % 256);

        compressedChunks[row * totalChannels + ch] = RlaRleCompressor.Compress(scanline);
      }

    var offsetTableSize = height * 4;
    var dataStart = RlaHeader.StructSize + offsetTableSize;

    // Calculate positions
    var scanlineOffsets = new int[height];
    var currentOffset = dataStart;
    for (var row = 0; row < height; ++row) {
      scanlineOffsets[row] = currentOffset;
      for (var ch = 0; ch < totalChannels; ++ch)
        currentOffset += 2 + compressedChunks[row * totalChannels + ch].Length;
    }

    var data = new byte[currentOffset];

    // Write header
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(8), 0);                     // ActiveWindowLeft
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(10), (short)(width - 1));    // ActiveWindowRight
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(12), 0);                     // ActiveWindowBottom
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(14), (short)(height - 1));   // ActiveWindowTop
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(20), (short)numChannels);    // NumChannels
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(22), (short)numMatte);       // NumMatte
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(658), (short)numBits);       // NumBits

    // Write offset table
    for (var i = 0; i < height; ++i)
      BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(RlaHeader.StructSize + i * 4), scanlineOffsets[i]);

    // Write scanline data
    for (var row = 0; row < height; ++row) {
      var pos = scanlineOffsets[row];
      for (var ch = 0; ch < totalChannels; ++ch) {
        var chunk = compressedChunks[row * totalChannels + ch];
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(pos), (ushort)chunk.Length);
        pos += 2;
        Array.Copy(chunk, 0, data, pos, chunk.Length);
        pos += chunk.Length;
      }
    }

    return data;
  }
}
