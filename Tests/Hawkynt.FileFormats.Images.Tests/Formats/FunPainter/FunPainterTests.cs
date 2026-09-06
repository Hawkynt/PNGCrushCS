using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.FunPainter.Tests;

[TestFixture]
public sealed class FunPainterReaderTests {

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FunPainterReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".fp2"));
    Assert.Throws<FileNotFoundException>(() => FunPainterReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FunPainterReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => FunPainterReader.FromBytes(new byte[10]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WithoutTheSignature_ThrowsInvalidDataException() {
    var data = FunPainterProbe.Unpacked();
    data[FunPainterFile.SignatureOffset] ^= 0xFF;

    Assert.Throws<InvalidDataException>(() => FunPainterReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_UnpackedButTheWrongLength_ThrowsInvalidDataException() {
    var data = FunPainterProbe.Unpacked();
    Array.Resize(ref data, FunPainterFile.FileSize - 1);

    Assert.Throws<InvalidDataException>(() => FunPainterReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_ParsesDimensions() {
    var result = FunPainterReader.FromBytes(FunPainterProbe.Unpacked());

    Assert.Multiple(() => {
      Assert.That(FunPainterFile.Width, Is.EqualTo(296));
      Assert.That(FunPainterFile.Height, Is.EqualTo(200));
      Assert.That(result.LoadAddress, Is.EqualTo(FunPainterFile.DefaultLoadAddress));
      Assert.That(result.Packed, Is.False);
      Assert.That(result.Data, Has.Length.EqualTo(FunPainterFile.FileSize));
    });
  }

  /// <summary>
  /// The picture is 296 by 200 and holds more colours than the machine has, because two screens are
  /// shown one after the other and the eye mixes them.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void Decoded_IsTheBlendOfTwoScreens() {
    var decoded = FunPainterFile.ToRawImage(FunPainterReader.FromBytes(FunPainterProbe.Unpacked()));

    var colors = new System.Collections.Generic.HashSet<int>();
    for (var i = 0; i < decoded.PixelData.Length; i += 3)
      colors.Add((decoded.PixelData[i] << 16) | (decoded.PixelData[i + 1] << 8) | decoded.PixelData[i + 2]);

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(FunPainterFile.Width));
      Assert.That(decoded.Height, Is.EqualTo(FunPainterFile.Height));
      Assert.That(colors, Has.Count.GreaterThan(Commodore64Graphics.ColorCount),
        "a blend of two screens shows more than the sixteen colours the hardware owns");
    });
  }

  /// <summary>A packed file and the same picture unpacked have to read the same.</summary>
  [Test]
  [Category("Unit")]
  public void APackedFileReadsAsTheSamePictureAsAnUnpackedOne() {
    var plain = FunPainterProbe.Unpacked();
    var packed = FunPainterProbe.Pack(plain);

    var fromPlain = FunPainterReader.FromBytes(plain);
    var fromPacked = FunPainterReader.FromBytes(packed);

    Assert.Multiple(() => {
      Assert.That(packed, Has.Length.LessThan(plain.Length), "the probe is meant to compress");
      Assert.That(fromPacked.Packed, Is.True);
      // Everything but the two header bytes that say how the payload was stored.
      Assert.That(
        fromPacked.Data[FunPainterFile.PayloadOffset..],
        Is.EqualTo(fromPlain.Data[FunPainterFile.PayloadOffset..]));
      Assert.That(
        FunPainterFile.ToRawImage(fromPacked).PixelData,
        Is.EqualTo(FunPainterFile.ToRawImage(fromPlain).PixelData));
    });
  }
}

[TestFixture]
public sealed class FunPainterRoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_UnpackedIsByteForByte() {
    var data = FunPainterProbe.Unpacked();
    var restored = FunPainterReader.FromBytes(FunPainterWriter.ToBytes(FunPainterReader.FromBytes(data)));

    Assert.That(restored.Data, Is.EqualTo(data));
  }

  /// <summary>A picture that arrived packed is written packed rather than silently tripling.</summary>
  [Test]
  [Category("Integration")]
  public void RoundTrip_APackedFileStaysPacked() {
    var packed = FunPainterProbe.Pack(FunPainterProbe.Unpacked());
    var file = FunPainterReader.FromBytes(packed);

    var written = FunPainterWriter.ToBytes(file);

    Assert.Multiple(() => {
      Assert.That(written, Has.Length.LessThan(FunPainterFile.FileSize));
      Assert.That(written[FunPainterFile.PackedFlagOffset], Is.Not.Zero);
      Assert.That(FunPainterReader.FromBytes(written).Data, Is.EqualTo(file.Data));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WithoutData_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => FunPainterWriter.ToBytes(default));
  }
}

/// <summary>Builds Fun Painter files by hand, because none can be drawn freehand.</summary>
/// <remarks>
/// Content is deterministic rather than random so a failure names the same pixel twice running, and
/// it is spread across all five areas of the file — both bitmaps, both sets of video matrices, and
/// the colour memory they share — so a misplaced offset shows up as a wrong picture rather than as
/// a picture that happens to be black.
/// </remarks>
internal static class FunPainterProbe {

  public static byte[] Unpacked() {
    var data = new byte[FunPainterFile.FileSize];
    data[0] = FunPainterFile.DefaultLoadAddress & 0xFF;
    data[1] = FunPainterFile.DefaultLoadAddress >> 8;
    for (var i = 0; i < FunPainterFile.Signature.Length; ++i)
      data[FunPainterFile.SignatureOffset + i] = (byte)FunPainterFile.Signature[i];

    for (var i = 0; i < 8000; ++i) {
      data[FunPainterFile.FirstBitmapOffset + i] = (byte)(i * 37);
      data[FunPainterFile.SecondBitmapOffset + i] = (byte)(i * 91);
    }

    for (var i = 0; i < FunPainterFile.MatrixStride * Commodore64Graphics.CellHeight; ++i) {
      data[FunPainterFile.FirstMatrixOffset + i] = (byte)(i * 13);
      data[FunPainterFile.SecondMatrixOffset + i] = (byte)(i * 29);
    }

    // Runs of one colour so the probe is worth packing; the variety the picture needs comes from
    // the video matrices, which change every scanline.
    for (var i = 0; i < 1000; ++i)
      data[FunPainterFile.ColorRamOffset + i] = (byte)(i / 64 & 15);

    return data;
  }

  /// <summary>Packs a file the way the format does, so the reader's unpacking has something to undo.</summary>
  public static byte[] Pack(byte[] unpacked) {
    var file = FunPainterReader.FromBytes(unpacked) with { Packed = true };

    return FunPainterWriter.ToBytes(file);
  }
}
