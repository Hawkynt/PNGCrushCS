using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Kqp.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  /// <summary>A gentle ramp, since what comes back has been through a lossy coder.</summary>
  private static RawImage _Ramp(int width, int height) {
    var data = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var at = (y * width + x) * 3;
        data[at] = (byte)(100 + x % 128);
        data[at + 1] = (byte)(110 + y % 128);
        data[at + 2] = 128;
      }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = data };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Ramp_ComesBackAtItsSizeAndVeryNearlyItsColours() {
    var source = _Ramp(37, 11);
    var decoded = KqpFile.ToRawImage(KqpReader.FromBytes(KqpWriter.ToBytes(KqpFile.FromRawImage(source))));

    long error = 0;
    for (var i = 0; i < source.PixelData.Length; ++i)
      error += Math.Abs(decoded.PixelData[i] - source.PixelData[i]);

    Assert.Multiple(() => {
      Assert.That((decoded.Width, decoded.Height), Is.EqualTo((37, 11)));
      Assert.That((double)error / source.PixelData.Length, Is.LessThan(4.0), "a JPEG at the camera's own tables");
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_KeepsWhateverSizeItIsGiven() {
    var wide = KqpFile.FromRawImage(_Ramp(200, 3));
    var tall = KqpFile.FromRawImage(_Ramp(3, 200));

    Assert.Multiple(() => {
      Assert.That((wide.Width, wide.Height), Is.EqualTo((200, 3)));
      Assert.That((tall.Width, tall.Height), Is.EqualTo((3, 200)));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromRawImage_AcceptsAGrey() {
    var grey = new RawImage { Width = 37, Height = 11, Format = PixelFormat.Gray8, PixelData = new byte[37 * 11] };
    var decoded = KqpFile.ToRawImage(KqpReader.FromBytes(KqpWriter.ToBytes(KqpFile.FromRawImage(grey))));

    Assert.That((decoded.Width, decoded.Height), Is.EqualTo((37, 11)));
  }

  /// <summary>
  /// The whole point of the format: the stream carries no quantisation tables and no Huffman tables,
  /// and a reader has to supply both. A writer leaving them in would produce a file this reader
  /// refuses outright, and one quantising against any other tables would produce a file that decodes
  /// to some other picture everywhere but here.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void ToBytes_TheStreamCarriesNeitherKindOfTable() {
    var bytes = KqpWriter.ToBytes(KqpFile.FromRawImage(_Ramp(37, 11)));
    var offset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(KqpFile.DataOffsetField));

    var markers = new System.Collections.Generic.List<byte>();
    for (var at = offset + 2; at + 4 <= bytes.Length;) {
      var marker = bytes[at + 1];
      markers.Add(marker);
      if (marker == 0xDA)
        break;

      at += 2 + BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(at + 2));
    }

    Assert.Multiple(() => {
      Assert.That(bytes[offset], Is.EqualTo(0xFF));
      Assert.That(bytes[offset + 1], Is.EqualTo(0xD8));
      Assert.That(markers, Does.Not.Contain((byte)0xDB), "no quantisation tables");
      Assert.That(markers, Does.Not.Contain((byte)0xC4), "no Huffman tables");
      Assert.That(markers, Does.Contain((byte)0xDA), "and it does reach a scan");
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_StatesTheBitmapHeaderTheseCarry() {
    var bytes = KqpWriter.ToBytes(KqpFile.FromRawImage(_Ramp(37, 11)));
    var at = KqpFile.FileHeaderSize;

    var headerSize = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at));
    var width = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at + 4));
    var height = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at + 8));
    var depth = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(at + 14));
    var compression = bytes.AsSpan(at + 16, 4).SequenceEqual(KqpFile.JpegCompression);

    Assert.Multiple(() => {
      Assert.That(headerSize, Is.EqualTo(KqpFile.InfoHeaderSize));
      Assert.That(width, Is.EqualTo(37));
      Assert.That(height, Is.EqualTo(-11), "the rows run top-down");
      Assert.That(depth, Is.EqualTo(24));
      Assert.That(compression, Is.True);
    });
  }

  /// <summary>
  /// Split at the stated offset and given the tables the format leaves out, what is inside has to be
  /// a JPEG anything can read. Asking a decoder that is not this one is the only way to tell a
  /// stream that is really coded against those tables from one this reader happens to agree with.
  /// </summary>
  [Test]
  [Category("Conformance")]
  public void SomethingElseReadsTheStreamOnceItsTablesAreBack() {
    var bytes = KqpWriter.ToBytes(KqpFile.FromRawImage(_Ramp(37, 11)));
    var offset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(KqpFile.DataOffsetField));

    var stream = bytes.AsSpan(offset);
    var scan = 2;
    while (stream[scan + 1] != 0xDA)
      scan += 2 + BinaryPrimitives.ReadUInt16BigEndian(stream[(scan + 2)..]);

    using var complete = new MemoryStream();
    complete.Write(stream[..scan]);
    complete.Write(KqpTestTables.Segments);
    complete.Write(stream[scan..]);

    var directory = Directory.CreateTempSubdirectory("kqp");
    try {
      var path = Path.Combine(directory.FullName, "sample.jpg");
      File.WriteAllBytes(path, complete.ToArray());

      using var identify = Process.Start(new ProcessStartInfo("identify", $"-format \"%wx%h\" \"{path}\"") {
        RedirectStandardOutput = true, RedirectStandardError = true,
      });

      if (identify == null)
        Assert.Ignore("no ImageMagick here to ask");

      var reported = identify!.StandardOutput.ReadToEnd().Trim().Trim('"');
      identify.WaitForExit();

      if (identify.ExitCode != 0)
        Assert.Fail($"ImageMagick refused the stream a Konica file we wrote carries: {identify.StandardError.ReadToEnd().Trim()}");

      Assert.That(reported, Is.EqualTo("37x11"));
    } finally {
      try { directory.Delete(recursive: true); } catch { /* best effort */ }
    }
  }
}
