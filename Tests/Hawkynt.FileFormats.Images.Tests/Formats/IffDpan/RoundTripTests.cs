using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;
using FileFormat.IffDpan;

namespace FileFormat.IffDpan.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void Encode_FromRawImage_WritesNormativeDpanChunk() {
    var source = _CreateIndexedImage();

    var bytes = FormatIO.Encode<IffDpanFile>(source);

    Assert.Multiple(() => {
      Assert.That(bytes.AsSpan(0, 4).ToArray(), Is.EqualTo("FORM"u8.ToArray()));
      Assert.That(BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(4, 4)), Is.EqualTo((uint)(bytes.Length - 8)));
      Assert.That(bytes.AsSpan(8, 4).ToArray(), Is.EqualTo("ANIM"u8.ToArray()));
      Assert.That(bytes.AsSpan(12, 4).ToArray(), Is.EqualTo("FORM"u8.ToArray()));
      Assert.That(bytes.AsSpan(20, 4).ToArray(), Is.EqualTo("ILBM"u8.ToArray()));
    });

    var frameLength = checked(8 + (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4)));
    // Copied out rather than sliced in place: a ref struct local cannot be captured by the lambda
    // Assert.Multiple takes.
    var frame = bytes.AsSpan(12, frameLength).ToArray();
    var dpanOffset = _FindChunk(frame, "DPAN"u8);
    var bodyOffset = _FindChunk(frame, "BODY"u8);

    Assert.Multiple(() => {
      Assert.That(dpanOffset, Is.GreaterThanOrEqualTo(12));
      Assert.That(dpanOffset, Is.LessThan(bodyOffset), "Deluxe Paint places DPAN in the first ILBM before BODY.");
      Assert.That(BinaryPrimitives.ReadUInt32BigEndian(frame.AsSpan(dpanOffset + 4, 4)), Is.EqualTo(8u));
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(dpanOffset + 8, 2)), Is.EqualTo(IffDpanFile.CurrentVersion));
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(dpanOffset + 10, 2)), Is.EqualTo(1));
      Assert.That(BinaryPrimitives.ReadUInt32BigEndian(frame.AsSpan(dpanOffset + 12, 4)), Is.Zero);
    });
  }

  [Test]
  [Category("Integration")]
  public void EncodeThenDecode_PaletteImage_PreservesDisplayedPixels() {
    var source = _CreateIndexedImage();

    var bytes = FormatIO.Encode<IffDpanFile>(source);
    var decoded = IffDpanFile.ToRawImage(IffDpanReader.FromBytes(bytes));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(source.Width));
      Assert.That(decoded.Height, Is.EqualTo(source.Height));
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(decoded.PixelData, Is.EqualTo(new byte[] {
        0, 0, 0,
        255, 255, 255,
        255, 0, 0,
        0, 255, 0,
        0, 255, 0,
        255, 0, 0,
        255, 255, 255,
        0, 0, 0,
      }));
    });
  }

  [Test]
  [Category("Integration")]
  public void EncodeThenDecode_Rgb24ImageWithAtMost256Colours_PreservesPixelsExactly() {
    var source = new RawImage {
      Width = 3,
      Height = 2,
      Format = PixelFormat.Rgb24,
      PixelData = [
        12, 34, 56, 78, 90, 123, 210, 180, 150,
        1, 2, 3, 17, 33, 65, 129, 193, 255,
      ],
    };

    var bytes = FormatIO.Encode<IffDpanFile>(source);
    var decoded = IffDpanFile.ToRawImage(IffDpanReader.FromBytes(bytes));

    Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ParsedAnimation_PreservesOriginalBytes() {
    var bytes = FormatIO.Encode<IffDpanFile>(_CreateIndexedImage());
    var parsed = IffDpanReader.FromBytes(bytes);

    var rewritten = IffDpanWriter.ToBytes(parsed);

    Assert.Multiple(() => {
      Assert.That(rewritten, Is.EqualTo(bytes));
      Assert.That(rewritten, Is.Not.SameAs(bytes));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_WrongPixelLength_ThrowsInvalidDataException() {
    var file = new IffDpanFile {
      Width = 2,
      Height = 2,
      PixelData = new byte[11],
      RawData = [],
    };

    Assert.Throws<InvalidDataException>(() => IffDpanFile.ToRawImage(file));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_NewFileWithWrongPixelLength_ThrowsInvalidDataException() {
    var file = new IffDpanFile {
      Width = 2,
      Height = 2,
      PixelData = new byte[11],
      RawData = [],
      Version = IffDpanFile.CurrentVersion,
      FrameCount = 1,
    };

    Assert.Throws<InvalidDataException>(() => IffDpanWriter.ToBytes(file));
  }

  private static RawImage _CreateIndexedImage() => new() {
    Width = 4,
    Height = 2,
    Format = PixelFormat.Indexed8,
    PixelData = [0, 1, 2, 3, 3, 2, 1, 0],
    Palette = [
      0, 0, 0,
      255, 255, 255,
      255, 0, 0,
      0, 255, 0,
    ],
    PaletteCount = 4,
  };

  private static int _FindChunk(ReadOnlySpan<byte> form, ReadOnlySpan<byte> chunkId) {
    var end = checked(8 + (int)BinaryPrimitives.ReadUInt32BigEndian(form.Slice(4, 4)));
    var offset = 12;
    while (offset + 8 <= end) {
      var size = checked((int)BinaryPrimitives.ReadUInt32BigEndian(form.Slice(offset + 4, 4)));
      if (form.Slice(offset, 4).SequenceEqual(chunkId))
        return offset;

      offset = checked(offset + 8 + size + (size & 1));
    }

    return -1;
  }
}
