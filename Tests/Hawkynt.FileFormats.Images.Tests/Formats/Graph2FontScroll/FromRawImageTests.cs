using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FileFormat.Core;
using FileFormat.Graph2Font;

namespace FileFormat.Graph2FontScroll.Tests;

[TestFixture]
public sealed class Graph2FontScrollFileFromRawImageTests {

  private const int _COLUMNS = 40;
  private const int _FONTS = 2;

  /// <summary>
  /// A project of a shape the encoder does not write, drawing a picture it must nonetheless be able
  /// to state: two character sets rather than thirty, characters shared between cells, and a colour
  /// table that changes down every scanline.
  /// </summary>
  /// <remarks>
  /// Different on purpose, and the same argument as the Graph2Font tests make: a project built the
  /// way the encoder builds one would prove only that the encoder agrees with itself.
  /// </remarks>
  private static byte[] _Handmade(int seed) {
    var fontsOffset = 3 + 30 * _COLUMNS;
    var numbers = fontsOffset + _FONTS * Graph2FontFile.FontSize;
    var data = new byte[numbers + 153724];

    data[0] = _COLUMNS;
    data[2] = _FONTS - 1;
    data[numbers + 147679] = 2;

    for (var row = 0; row < 30; ++row) {
      data[numbers + row] = (byte)(row % _FONTS);
      data[numbers + 153694 + row] = 2;

      // No character carries the high bit: it would ask for a fifth colour, and a fifth colour is
      // one byte per cell where every other colour choice is one byte per scanline.
      for (var column = 0; column < _COLUMNS; ++column)
        data[3 + row * _COLUMNS + column] = (byte)((row * 7 + column * 3 + seed) % 128);
    }

    for (var at = 0; at < _FONTS * Graph2FontFile.FontSize; ++at)
      data[fontsOffset + at] = (byte)(at * 37 + (at >> 5) * 11 + seed);

    for (var y = 0; y < Graph2FontFile.Height; ++y) {
      data[numbers + 30 + y] = (byte)((y + seed) / 15 * 2 & 254);
      data[numbers + 30 + y + 256] = (byte)(0x28 | (y & 14));
      data[numbers + 30 + y + 512] = (byte)(0x94 | (y / 3 & 14));
      data[numbers + 30 + y + 768] = (byte)(0xC2 | (y / 7 & 14));
    }

    return data;
  }

  private static Graph2FontScrollFile _Scroll(int frames)
    => new() {
      Frames = Enumerable.Range(0, frames).Select(i => _Handmade(i * 5)).ToList(),
      Names = Enumerable.Range(0, frames).Select(i => $"piece{i}.g2f").ToList(),
    };

  private static byte[] _Rgb(RawImage image) => PixelConverter.Convert(image, PixelFormat.Rgb24).PixelData;

  [Test]
  [Category("Unit")]
  public void EncodeThenDecode_ReturnsAPictureTheFormatHoldsUnchanged() {
    var source = Graph2FontScrollFile.ToRawImage(_Scroll(3));
    var decoded = Graph2FontScrollFile.ToRawImage(Graph2FontScrollFile.FromRawImage(source));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(Graph2FontScrollFile.Width));
      Assert.That(decoded.Height, Is.EqualTo(Graph2FontScrollFile.FrameHeight * 3));
      Assert.That(_Rgb(decoded), Is.EqualTo(_Rgb(source)));
    });
  }

  [Test]
  [Category("Unit")]
  public void ThePictureIsCutIntoScreensInOrder() {
    var source = Graph2FontScrollFile.ToRawImage(_Scroll(2));
    var encoded = Graph2FontScrollFile.FromRawImage(source);

    // Each piece has to be a picture on its own, and the one that belongs at that height.
    Assert.That(encoded.Frames, Has.Count.EqualTo(2));
    for (var i = 0; i < 2; ++i) {
      var piece = _Rgb(Graph2FontFile.ToRawImage(new() { Data = encoded.Frames[i] }));
      var band = Graph2FontScrollFile.Width * Graph2FontScrollFile.FrameHeight * 3;
      Assert.That(piece, Is.EqualTo(_Rgb(source)[(i * band)..((i + 1) * band)]), $"piece {i}");
    }
  }

  [Test]
  [Category("Unit")]
  public void APictureOfAnotherShapeIsRefusedByName() {
    var tall = Graph2FontScrollFile.ToRawImage(_Scroll(1));

    Assert.Multiple(() => {
      Assert.That(
        Assert.Throws<ArgumentException>(() => Graph2FontScrollFile.FromRawImage(tall.SampleTo(320, 240)))!.Message,
        Does.Contain("336 pixels across"));
      Assert.That(
        Assert.Throws<ArgumentException>(() => Graph2FontScrollFile.FromRawImage(tall.SampleTo(336, 200)))!.Message,
        Does.Contain("240-row screens"));
      Assert.That(
        Assert.Throws<ArgumentException>(() => Graph2FontScrollFile.FromRawImage(tall.SampleTo(336, 480 + 1)))!.Message,
        Does.Contain("240-row screens"));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => Graph2FontScrollFile.FromRawImage(null!));

  [Test]
  [Category("Unit")]
  public void TheProjectsAreNamedAfterTheScrollAndWrittenBesideIt() {
    var directory = Directory.CreateTempSubdirectory("g2fscroll");
    try {
      var target = new FileInfo(Path.Combine(directory.FullName, "tower.vsc"));
      var source = Graph2FontScrollFile.ToRawImage(_Scroll(2));
      FormatIO.WriteToFile<Graph2FontScrollFile>(source, target);

      Assert.Multiple(() => {
        Assert.That(File.ReadAllText(target.FullName), Is.EqualTo("tower0.g2f\r\ntower1.g2f\r\n"));
        Assert.That(File.Exists(Path.Combine(directory.FullName, "tower0.g2f")));
        Assert.That(File.Exists(Path.Combine(directory.FullName, "tower1.g2f")));
      });

      var back = Graph2FontScrollFile.ToRawImage(Graph2FontScrollReader.FromFile(target));
      Assert.That(_Rgb(back), Is.EqualTo(_Rgb(source)));
    } finally {
      try { directory.Delete(recursive: true); } catch (IOException) { /* the temp tree is the OS's problem now */ }
    }
  }

  /// <summary>
  /// A scroll read from its own path and written back names the same projects and draws the same
  /// picture, which is the only round trip the format has — the .vsc file holds nothing else.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void AScrollReadFromDiskSurvivesBeingWrittenBack() {
    var directory = Directory.CreateTempSubdirectory("g2fscroll");
    try {
      File.WriteAllText(Path.Combine(directory.FullName, "one.vsc"), "a.g2f\r\nb.g2f\r\n");
      File.WriteAllBytes(Path.Combine(directory.FullName, "a.g2f"), _Handmade(0));

      // The second one packed, which is the other form a project comes in and the one the reader
      // unpacks — so writing it back is also the check that what came out of the unpacking is a
      // project rather than a buffer that happened to be the right length.
      using (var packed = new MemoryStream()) {
        packed.Write(Encoding.ASCII.GetBytes(Graph2FontFile.CompressedSignature));
        using (var deflate = new System.IO.Compression.ZLibStream(
                 packed, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
          deflate.Write(_Handmade(11));

        File.WriteAllBytes(Path.Combine(directory.FullName, "b.g2f"), packed.ToArray());
      }

      var read = Graph2FontScrollReader.FromFile(new(Path.Combine(directory.FullName, "one.vsc")));
      var rewritten = new FileInfo(Path.Combine(directory.FullName, "again.vsc"));
      File.WriteAllBytes(rewritten.FullName, Graph2FontScrollWriter.ToBytes(read));
      Graph2FontScrollWriter.WriteCompanions(read, rewritten);

      Assert.Multiple(() => {
        Assert.That(File.ReadAllText(rewritten.FullName), Is.EqualTo("a.g2f\r\nb.g2f\r\n"));
        Assert.That(
          _Rgb(Graph2FontScrollFile.ToRawImage(Graph2FontScrollReader.FromFile(rewritten))),
          Is.EqualTo(_Rgb(Graph2FontScrollFile.ToRawImage(read))));
      });
    } finally {
      try { directory.Delete(recursive: true); } catch (IOException) { /* the temp tree is the OS's problem now */ }
    }
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_KeepsTheNamesAndHasNoProjectsToWrite() {
    var read = Graph2FontScrollReader.FromBytes(Encoding.ASCII.GetBytes("x.g2f\r\ny.g2f\r\n"));

    Assert.Multiple(() => {
      Assert.That(read.Names, Is.EqualTo(new[] { "x.g2f", "y.g2f" }));
      Assert.That(read.Frames, Is.Empty);
      Assert.That(Graph2FontScrollWriter.ToBytes(read), Is.EqualTo(Encoding.ASCII.GetBytes("x.g2f\r\ny.g2f\r\n")));
      Assert.That(
        () => Graph2FontScrollWriter.WriteCompanions(read, new("/nowhere/x.vsc")),
        Throws.TypeOf<InvalidDataException>());
    });
  }

  /// <summary>A name a scroll cannot carry is refused rather than quietly rewritten.</summary>
  [Test]
  [Category("Unit")]
  public void ANameTheListCannotHoldIsRefused() {
    IReadOnlyList<byte[]> frames = [_Handmade(0)];

    Assert.Multiple(() => {
      Assert.That(
        () => Graph2FontScrollWriter.ToBytes(new() { Frames = frames, Names = ["over/there.g2f"] }),
        Throws.TypeOf<InvalidDataException>());
      Assert.That(
        () => Graph2FontScrollWriter.ToBytes(new() { Frames = frames, Names = ["schön.g2f"] }),
        Throws.TypeOf<InvalidDataException>());
      Assert.That(
        () => Graph2FontScrollWriter.ToBytes(new() { Frames = [], Names = [] }),
        Throws.TypeOf<InvalidDataException>());
    });
  }
}
