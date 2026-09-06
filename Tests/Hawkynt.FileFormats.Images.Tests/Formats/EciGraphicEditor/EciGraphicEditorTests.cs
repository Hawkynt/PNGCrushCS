using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.EciGraphicEditor.Tests;

[TestFixture]
public sealed class EciGraphicEditorReaderTests {

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => EciGraphicEditorReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".eci"));
    Assert.Throws<FileNotFoundException>(() => EciGraphicEditorReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => EciGraphicEditorReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => EciGraphicEditorReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => EciGraphicEditorReader.FromBytes(new byte[2]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PackedButTruncated_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => EciGraphicEditorReader.FromBytes(new byte[100]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Unpacked_SplitsIntoBanks() {
    var data = TestHelpers.Unpacked(0x4000);
    var result = EciGraphicEditorReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(296));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.LoadAddress, Is.EqualTo(0x4000));
    Assert.That(result.FirstBitmap[0], Is.EqualTo(data[2]));
    Assert.That(result.FirstScreens[0], Is.EqualTo(data[2 + 8192]));
    Assert.That(result.SecondBitmap[0], Is.EqualTo(data[2 + 16384]));
    Assert.That(result.SecondScreens[0], Is.EqualTo(data[2 + 16384 + 8192]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_LoadAddress_ParsedAsLittleEndian() {
    var result = EciGraphicEditorReader.FromBytes(TestHelpers.Unpacked(0x5C00));

    Assert.That(result.LoadAddress, Is.EqualTo(0x5C00));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Unpacked_ParsesCorrectly() {
    using var ms = new MemoryStream(TestHelpers.Unpacked(0x4000));
    var result = EciGraphicEditorReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(296));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.LoadAddress, Is.EqualTo(0x4000));
  }

  /// <summary>The .ecp form has to arrive at the same picture as the .eci it was packed from.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_Packed_DecodesToTheSamePicture() {
    var unpacked = TestHelpers.Unpacked(0x4000);
    // Runs, so the coder has something to code and the test is not measuring the escape path alone.
    Array.Fill(unpacked, (byte)0xC0, 2 + 8000, 192);
    Array.Fill(unpacked, (byte)0x17, 2 + 16384, 8000);

    var packed = TestHelpers.Pack(unpacked);
    Assert.That(packed.Length, Is.LessThan(unpacked.Length), "the probe did not compress at all");

    var fromPacked = EciGraphicEditorFile.ToRawImage(EciGraphicEditorReader.FromBytes(packed));
    var fromUnpacked = EciGraphicEditorFile.ToRawImage(EciGraphicEditorReader.FromBytes(unpacked));

    Assert.That(fromPacked.PixelData, Is.EqualTo(fromUnpacked.PixelData));
  }
}

[TestFixture]
public sealed class EciGraphicEditorDecodeTests {

  /// <summary>
  /// One raster line of a cell must take its colours from its own video matrix and no other, which
  /// is the whole of what FLI buys and what a non-FLI reader gets wrong.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void ToRawImage_EachRasterLine_TakesItsOwnVideoMatrix() {
    var file = TestHelpers.Blank();

    // Every pixel lit, so each row shows its matrix's high nibble.
    Array.Fill(file.FirstBitmap, (byte)0xFF);
    Array.Fill(file.SecondBitmap, (byte)0xFF);
    for (var line = 0; line < 8; ++line)
      for (var cell = 0; cell < 1000; ++cell) {
        file.FirstScreens[line * 1024 + cell] = (byte)(line << 4);
        file.SecondScreens[line * 1024 + cell] = (byte)(line << 4);
      }

    var image = EciGraphicEditorFile.ToRawImage(file);

    for (var y = 0; y < 200; ++y) {
      var expected = Commodore64Graphics.HexColors[y % 8];
      var at = y * 296 * 3;
      Assert.That((image.PixelData[at], image.PixelData[at + 1], image.PixelData[at + 2]),
        Is.EqualTo(((byte)(expected >> 16), (byte)(expected >> 8), (byte)expected)),
        $"row {y} did not read matrix {y % 8}");
    }
  }

  /// <summary>The picture starts three character cells into the stored row, where FLI can colour it.</summary>
  [Test]
  [Category("Unit")]
  public void ToRawImage_SkipsTheThreeCellsFliCannotColour() {
    var file = TestHelpers.Blank();
    Array.Fill(file.FirstBitmap, (byte)0xFF);
    Array.Fill(file.SecondBitmap, (byte)0xFF);

    // Cell column 3 is the first visible one; anything left of it must not be drawn.
    for (var line = 0; line < 8; ++line) {
      file.FirstScreens[line * 1024 + 0] = 0x10;
      file.SecondScreens[line * 1024 + 0] = 0x10;
      file.FirstScreens[line * 1024 + 3] = 0x70;
      file.SecondScreens[line * 1024 + 3] = 0x70;
    }

    var image = EciGraphicEditorFile.ToRawImage(file);
    var expected = Commodore64Graphics.HexColors[7];

    Assert.That(image.PixelData[0], Is.EqualTo((byte)(expected >> 16)));
    Assert.That(image.PixelData[1], Is.EqualTo((byte)(expected >> 8)));
    Assert.That(image.PixelData[2], Is.EqualTo((byte)expected));
  }

  /// <summary>Two frames naming different colours show the average of the two, not either of them.</summary>
  [Test]
  [Category("Unit")]
  public void ToRawImage_DifferingFrames_ShowsTheirAverage() {
    var file = TestHelpers.Blank();
    Array.Fill(file.FirstBitmap, (byte)0xFF);
    Array.Fill(file.SecondBitmap, (byte)0xFF);
    for (var line = 0; line < 8; ++line)
      for (var cell = 0; cell < 1000; ++cell) {
        file.FirstScreens[line * 1024 + cell] = 0x10;
        file.SecondScreens[line * 1024 + cell] = 0x00;
      }

    var image = EciGraphicEditorFile.ToRawImage(file);

    // White over black, averaged the way the reference decoder averages: rounding down.
    Assert.That((image.PixelData[0], image.PixelData[1], image.PixelData[2]),
      Is.EqualTo(((byte)0x7F, (byte)0x7F, (byte)0x7F)));
  }
}

[TestFixture]
public sealed class EciGraphicEditorEncodeTests {

  /// <summary>A picture the format can hold has to come back out of it unchanged.</summary>
  [Test]
  [Category("Integration")]
  public void FromRawImageExact_APictureTheFormatHolds_RoundTripsPixelForPixel() {
    var original = EciGraphicEditorFile.ToRawImage(
      EciGraphicEditorReader.FromBytes(TestHelpers.Unpacked(0x4000)));

    var encoded = EciGraphicEditorFile.FromRawImageExact(original);
    var decoded = EciGraphicEditorFile.ToRawImage(
      EciGraphicEditorReader.FromBytes(EciGraphicEditorWriter.ToBytes(encoded)));

    Assert.That(decoded.PixelData, Is.EqualTo(original.PixelData));
  }

  /// <summary>A group of eight pixels holds four colours; a fifth is refused rather than dropped.</summary>
  [Test]
  [Category("Unit")]
  public void FromRawImageExact_FiveColoursInOneGroup_Refuses() {
    var image = TestHelpers.Solid(0x000000);
    int[] colors = [0xFFFFFF, 0x68372B, 0x70A4B2, 0x6F3D86, 0x588D43];
    for (var i = 0; i < colors.Length; ++i) {
      image.PixelData[i * 3] = (byte)(colors[i] >> 16);
      image.PixelData[i * 3 + 1] = (byte)(colors[i] >> 8);
      image.PixelData[i * 3 + 2] = (byte)colors[i];
    }

    Assert.Throws<NotSupportedException>(() => EciGraphicEditorFile.FromRawImageExact(image));
  }

  /// <summary>A colour no two of the machine's sixteen average to is refused rather than approximated.</summary>
  [Test]
  [Category("Unit")]
  public void FromRawImageExact_AColourTheBlendCannotReach_Refuses() {
    var image = TestHelpers.Solid(0x000000);
    image.PixelData[0] = 1;
    image.PixelData[1] = 2;
    image.PixelData[2] = 3;

    Assert.Throws<NotSupportedException>(() => EciGraphicEditorFile.FromRawImageExact(image));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImageExact_WrongSize_Refuses() {
    var image = new RawImage {
      Width = 320, Height = 200, Format = PixelFormat.Rgb24, PixelData = new byte[320 * 200 * 3],
    };

    Assert.Throws<ArgumentException>(() => EciGraphicEditorFile.FromRawImageExact(image));
  }

  /// <summary>A blend of two of the machine's colours is one the format holds, unlike either alone.</summary>
  [Test]
  [Category("Unit")]
  public void FromRawImageExact_ABlendedGrey_IsHeldExactly() {
    var image = TestHelpers.Solid(0x7F7F7F);

    var decoded = EciGraphicEditorFile.ToRawImage(EciGraphicEditorFile.FromRawImageExact(image));

    Assert.That(decoded.PixelData, Is.EqualTo(image.PixelData));
  }

  /// <summary>The registry's path takes any picture and any size, approximating what it must.</summary>
  [Test]
  [Category("Unit")]
  public void FromRawImage_AnArbitraryPicture_IsAccepted() {
    var image = new RawImage {
      Width = 64, Height = 48, Format = PixelFormat.Rgb24, PixelData = new byte[64 * 48 * 3],
    };
    for (var i = 0; i < image.PixelData.Length; ++i)
      image.PixelData[i] = (byte)(i * 37);

    var bytes = EciGraphicEditorWriter.ToBytes(EciGraphicEditorFile.FromRawImage(image));

    Assert.That(bytes.Length, Is.EqualTo(EciGraphicEditorFile.FileSize));
  }

  /// <summary>
  /// Interlacing has to earn its second bank: the same picture must come out closer than a single
  /// high-resolution FLI frame manages.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromRawImage_UsesBothFrames_AndBeatsASingleOne() {
    var image = TestHelpers.Gradient();

    var interlaced = EciGraphicEditorFile.ToRawImage(EciGraphicEditorFile.FromRawImage(image));
    var single = PixelConverter.Convert(
      FileFormat.Afli.AfliFile.ToRawImage(FileFormat.Afli.AfliFile.FromRawImage(image)), PixelFormat.Rgb24);

    Assert.That(TestHelpers.SquaredError(image.PixelData, interlaced.PixelData),
      Is.LessThan(TestHelpers.SquaredError(image.PixelData, single.PixelData)));
  }
}

[TestFixture]
public sealed class EciGraphicEditorRoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_EveryBankPreserved() {
    var original = EciGraphicEditorReader.FromBytes(TestHelpers.Unpacked(0x4000));

    var restored = EciGraphicEditorReader.FromBytes(EciGraphicEditorWriter.ToBytes(original));

    Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(restored.FirstBitmap, Is.EqualTo(original.FirstBitmap));
    Assert.That(restored.FirstScreens, Is.EqualTo(original.FirstScreens));
    Assert.That(restored.SecondBitmap, Is.EqualTo(original.SecondBitmap));
    Assert.That(restored.SecondScreens, Is.EqualTo(original.SecondScreens));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_CustomLoadAddress() {
    var original = EciGraphicEditorReader.FromBytes(TestHelpers.Unpacked(0xFFFF));

    var restored = EciGraphicEditorReader.FromBytes(EciGraphicEditorWriter.ToBytes(original));

    Assert.That(restored.LoadAddress, Is.EqualTo(0xFFFF));
  }

  [Test]
  [Category("Integration")]
  public void ToBytes_AlwaysWritesTheWholeFile() {
    var bytes = EciGraphicEditorWriter.ToBytes(TestHelpers.Blank());

    Assert.That(bytes.Length, Is.EqualTo(EciGraphicEditorFile.FileSize));
    Assert.That(bytes.Length, Is.EqualTo(32770));
  }
}

file static class TestHelpers {

  /// <summary>An empty file with every section allocated.</summary>
  internal static EciGraphicEditorFile Blank() => new() {
    LoadAddress = 0x4000,
    FirstBitmap = new byte[8000],
    FirstScreens = new byte[8192],
    SecondBitmap = new byte[8000],
    SecondScreens = new byte[8192],
  };

  /// <summary>A whole unpacked file whose every byte differs from its neighbours.</summary>
  internal static byte[] Unpacked(ushort loadAddress) {
    var data = new byte[32770];
    data[0] = (byte)loadAddress;
    data[1] = (byte)(loadAddress >> 8);

    for (var i = 2; i < data.Length; ++i)
      data[i] = (byte)(i * 37 + (i >> 8) * 11);

    return data;
  }

  /// <summary>Packs a file into the .ecp form: one chosen byte value introduces a run.</summary>
  internal static byte[] Pack(byte[] unpacked) {
    const byte escape = 0xC0;
    var packed = new List<byte> { unpacked[0], unpacked[1], escape };

    for (var at = 2; at < unpacked.Length;) {
      var value = unpacked[at];
      var run = 1;
      while (run < 255 && at + run < unpacked.Length && unpacked[at + run] == value)
        ++run;

      if (run >= 3 || value == escape) {
        packed.Add(escape);
        packed.Add((byte)run);
        packed.Add(value);
        at += run;
      } else {
        packed.Add(value);
        ++at;
      }
    }

    return packed.ToArray();
  }

  internal static RawImage Solid(int color) {
    var rgb = new byte[296 * 200 * 3];
    for (var i = 0; i < 296 * 200; ++i) {
      rgb[i * 3] = (byte)(color >> 16);
      rgb[i * 3 + 1] = (byte)(color >> 8);
      rgb[i * 3 + 2] = (byte)color;
    }

    return new() { Width = 296, Height = 200, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  internal static RawImage Gradient() {
    var rgb = new byte[296 * 200 * 3];
    for (var y = 0; y < 200; ++y)
      for (var x = 0; x < 296; ++x) {
        var at = (y * 296 + x) * 3;
        rgb[at] = (byte)(x * 255 / 295);
        rgb[at + 1] = (byte)(y * 255 / 199);
        rgb[at + 2] = (byte)((x ^ y) & 0xFF);
      }

    return new() { Width = 296, Height = 200, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  internal static long SquaredError(byte[] first, byte[] second) {
    long total = 0;
    for (var i = 0; i < first.Length; ++i) {
      long difference = first[i] - second[i];
      total += difference * difference;
    }

    return total;
  }
}
