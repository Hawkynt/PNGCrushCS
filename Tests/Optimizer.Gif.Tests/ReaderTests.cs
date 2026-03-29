using System;
using System.IO;
using Hawkynt.GifFileFormat;
using NUnit.Framework;

namespace Optimizer.Gif.Tests;

[TestFixture]
public sealed class ReaderTests {
  private static FileInfo _CreateSimpleGif(int width = 4, int height = 4, int frameCount = 1,
    bool useTransparency = false) {
    var tempFile = new FileInfo(Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.gif"));

    using var ms = new MemoryStream();
    _WriteGifManually(ms, width, height, frameCount, useTransparency);
    File.WriteAllBytes(tempFile.FullName, ms.ToArray());

    return tempFile;
  }

  private static void _WriteGifManually(Stream stream, int width, int height, int frameCount, bool useTransparency) {
    using var writer = new BinaryWriter(stream);

    // Header
    writer.Write("GIF89a"u8);

    // Logical Screen Descriptor
    writer.Write((ushort)width);
    writer.Write((ushort)height);
    byte packed = 0xF1; // GCT present, 8-bit color resolution, 4-entry GCT (1 = log2(4)-1)
    writer.Write(packed);
    writer.Write((byte)0); // bg color index
    writer.Write((byte)0); // pixel aspect ratio

    // Global Color Table (4 entries)
    writer.Write(new byte[] { 255, 0, 0 }); // Red
    writer.Write(new byte[] { 0, 255, 0 }); // Green
    writer.Write(new byte[] { 0, 0, 255 }); // Blue
    writer.Write(new byte[] { 255, 255, 255 }); // White

    // NETSCAPE2.0 for animation
    if (frameCount > 1) {
      writer.Write((byte)0x21); // Extension
      writer.Write((byte)0xFF); // Application
      writer.Write((byte)0x0B); // Block size
      writer.Write("NETSCAPE"u8);
      writer.Write("2.0"u8);
      writer.Write((byte)0x03);
      writer.Write((byte)0x01);
      writer.Write((ushort)0); // Infinite loop
      writer.Write((byte)0x00); // Terminator
    }

    for (var f = 0; f < frameCount; ++f) {
      // Graphics Control Extension
      writer.Write((byte)0x21);
      writer.Write((byte)0xF9);
      writer.Write((byte)0x04);
      var gcePacked = (byte)((f == 0 ? 0 : 2) << 2); // disposal: 0=unspecified, 2=restore to bg
      if (useTransparency)
        gcePacked |= 0x01;
      writer.Write(gcePacked);
      writer.Write((ushort)10); // 100ms delay
      writer.Write(useTransparency ? (byte)0 : (byte)0); // transparent color index
      writer.Write((byte)0x00);

      // Image Descriptor (no LCT, use GCT)
      writer.Write((byte)0x2C);
      writer.Write((ushort)0); // left
      writer.Write((ushort)0); // top
      writer.Write((ushort)width);
      writer.Write((ushort)height);
      writer.Write((byte)0x00); // no LCT

      // LZW image data — minimum code size 2 (for 4-color palette)
      writer.Write((byte)2);

      // Generate pixel data
      var pixels = new byte[width * height];
      for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x)
        pixels[y * width + x] = (byte)((x + y + f) % 4);

      // Simple LZW encoding using uncompressed approach
      var lzwData = _EncodeLzwUncompressed(pixels, 2);

      // Write as sub-blocks
      var offset = 0;
      while (offset < lzwData.Length) {
        var blockSize = Math.Min(255, lzwData.Length - offset);
        writer.Write((byte)blockSize);
        writer.Write(lzwData, offset, blockSize);
        offset += blockSize;
      }

      writer.Write((byte)0x00); // Block terminator
    }

    writer.Write((byte)0x3B); // Trailer
  }

  private static byte[] _EncodeLzwUncompressed(byte[] pixels, byte minCodeSize) {
    var clearCode = 1 << minCodeSize;
    var eoiCode = clearCode + 1;
    var codeSize = (byte)(minCodeSize + 1);

    using var ms = new MemoryStream();
    uint buffer = 0;
    var bits = 0;

    void WriteBits(int value, int numBits) {
      buffer |= (uint)value << bits;
      bits += numBits;
      while (bits >= 8) {
        ms.WriteByte((byte)(buffer & 0xFF));
        buffer >>= 8;
        bits -= 8;
      }
    }

    // Write clear code first
    WriteBits(clearCode, codeSize);

    var count = 0;
    foreach (var pixel in pixels) {
      if (count > 0 && count % ((1 << codeSize) - eoiCode - 1) == 0)
        WriteBits(clearCode, codeSize);
      WriteBits(pixel, codeSize);
      ++count;
    }

    WriteBits(eoiCode, codeSize);

    // Flush remaining bits
    if (bits > 0)
      ms.WriteByte((byte)buffer);

    return ms.ToArray();
  }

  [Test]
  [Category("Unit")]
  public void FromFile_SingleFrame_ParsesHeader() {
    var file = _CreateSimpleGif();
    try {
      var gif = Reader.FromFile(file);

      Assert.That(gif.Version, Is.EqualTo("89a"));
      Assert.That(gif.LogicalScreenSize.Width, Is.EqualTo(4));
      Assert.That(gif.LogicalScreenSize.Height, Is.EqualTo(4));
      Assert.That(gif.Frames.Count, Is.EqualTo(1));
    } finally {
      file.Delete();
    }
  }

  [Test]
  [Category("Unit")]
  public void FromFile_MultiFrame_ParsesAllFrames() {
    var file = _CreateSimpleGif(frameCount: 3);
    try {
      var gif = Reader.FromFile(file);

      Assert.That(gif.Frames.Count, Is.EqualTo(3));
      Assert.That(gif.LoopCount.IsSet, Is.True);
      Assert.That(gif.LoopCount.IsInfinite, Is.True);

      foreach (var frame in gif.Frames) {
        Assert.That(frame.Size.Width, Is.EqualTo(4));
        Assert.That(frame.Size.Height, Is.EqualTo(4));
        Assert.That(frame.IndexedPixels.Length, Is.EqualTo(16));
      }
    } finally {
      file.Delete();
    }
  }

  [Test]
  [Category("Unit")]
  public void FromFile_WithTransparency_ParsesTransparentIndex() {
    var file = _CreateSimpleGif(useTransparency: true);
    try {
      var gif = Reader.FromFile(file);
      Assert.That(gif.Frames[0].TransparentColorIndex, Is.EqualTo(0));
    } finally {
      file.Delete();
    }
  }

  [Test]
  [Category("Unit")]
  public void FromFile_FramePixels_MatchWrittenData() {
    var file = _CreateSimpleGif();
    try {
      var gif = Reader.FromFile(file);
      var pixels = gif.Frames[0].IndexedPixels;

      Assert.That(pixels.Length, Is.EqualTo(16));
      Assert.That(pixels[0], Is.EqualTo(0)); // (0+0+0) % 4 = 0
      Assert.That(pixels[1], Is.EqualTo(1)); // (1+0+0) % 4 = 1
      Assert.That(pixels[2], Is.EqualTo(2)); // (2+0+0) % 4 = 2
      Assert.That(pixels[3], Is.EqualTo(3)); // (3+0+0) % 4 = 3
      Assert.That(pixels[4], Is.EqualTo(1)); // (0+1+0) % 4 = 1
    } finally {
      file.Delete();
    }
  }

  [Test]
  [Category("Unit")]
  public void FromFile_GlobalColorTable_ParsesPalette() {
    var file = _CreateSimpleGif();
    try {
      var gif = Reader.FromFile(file);

      Assert.That(gif.GlobalColorTable, Is.Not.Null);
      Assert.That(gif.GlobalColorTable!.Length, Is.EqualTo(4));
      Assert.That(gif.GlobalColorTable[0].R, Is.EqualTo(255));
      Assert.That(gif.GlobalColorTable[0].G, Is.EqualTo(0));
      Assert.That(gif.GlobalColorTable[0].B, Is.EqualTo(0));
    } finally {
      file.Delete();
    }
  }

  [Test]
  [Category("Unit")]
  public void FromFile_FrameDelay_ParsedCorrectly() {
    var file = _CreateSimpleGif();
    try {
      var gif = Reader.FromFile(file);
      Assert.That(gif.Frames[0].Delay.TotalMilliseconds, Is.EqualTo(100).Within(10));
    } finally {
      file.Delete();
    }
  }

  [Test]
  [Category("Unit")]
  public void FromFile_NonExistentFile_ThrowsFileNotFoundException() {
    var file = new FileInfo(Path.Combine(Path.GetTempPath(), "nonexistent.gif"));
    Assert.Throws<FileNotFoundException>(() => Reader.FromFile(file));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_InvalidData_ThrowsInvalidDataException() {
    using var ms = new MemoryStream([0, 1, 2, 3, 4, 5]);
    Assert.Throws<InvalidDataException>(() => Reader.FromStream(ms));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_DisposalMethod_ParsedCorrectly() {
    var file = _CreateSimpleGif(frameCount: 2);
    try {
      var gif = Reader.FromFile(file);

      Assert.That(gif.Frames[0].DisposalMethod, Is.EqualTo(FrameDisposalMethod.Unspecified));
      Assert.That(gif.Frames[1].DisposalMethod, Is.EqualTo(FrameDisposalMethod.RestoreToBackground));
    } finally {
      file.Delete();
    }
  }
}
