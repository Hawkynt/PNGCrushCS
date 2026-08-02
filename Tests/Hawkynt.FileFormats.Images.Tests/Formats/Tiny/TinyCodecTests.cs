using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Tiny.Tests;

/// <summary>
/// The coding a Tiny file actually uses, as against the one that used to be assumed here.
/// </summary>
/// <remarks>
/// Both halves of it were invented and agreed with themselves: counts and values were read from a
/// single interleaved stream, so every file this library wrote could be read back by nothing else,
/// and no real file could be read at all — the pictures came out as static.
/// <para/>
/// A real file keeps its control bytes and its data words in two blocks whose lengths the header
/// states, and stores the Atari's screen memory as it stands: sixteen thousand words of four
/// interleaved bitplanes whatever the resolution, running down each column before moving across.
/// <para/>
/// Checked against RECOIL on real files of every variant: <c>.tn1</c>, <c>.tn3</c>, <c>.tn4</c> and
/// <c>.tny</c> come back byte-identical, and <c>.tn2</c> does too once RECOIL's doubling of medium
/// resolution rows for display is undone.
/// </remarks>
[TestFixture]
public sealed class TinyCodecTests {

  private static byte[] _Screen(Func<int, short> word) {
    var screen = new byte[32000];
    for (var i = 0; i < 16000; ++i)
      BinaryPrimitives.WriteInt16BigEndian(screen.AsSpan(i * 2), word(i));

    return screen;
  }

  private static byte[] _Assemble(TinyResolution resolution, byte[] control, byte[] data, short[]? palette = null) {
    using var ms = new MemoryStream();
    ms.WriteByte((byte)resolution);

    Span<byte> buffer = stackalloc byte[2];
    for (var i = 0; i < 16; ++i) {
      BinaryPrimitives.WriteInt16BigEndian(buffer, palette != null && i < palette.Length ? palette[i] : (short)0);
      ms.Write(buffer);
    }

    BinaryPrimitives.WriteUInt16BigEndian(buffer, (ushort)control.Length);
    ms.Write(buffer);
    BinaryPrimitives.WriteUInt16BigEndian(buffer, (ushort)(data.Length / 2));
    ms.Write(buffer);
    ms.Write(control);
    ms.Write(data);

    return ms.ToArray();
  }

  [Test]
  [Category("Unit")]
  public void Read_TakesTheTwoBlockLengthsFromTheHeader() {
    // One control byte repeating a single word over the whole screen.
    var control = new byte[] { 0, 0x3E, 0x80 };
    var data = new byte[] { 0x12, 0x34 };
    var file = TinyReader.FromBytes(_Assemble(TinyResolution.High, control, data));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(640));
      Assert.That(file.Height, Is.EqualTo(400));
      Assert.That(file.PixelData, Has.Length.EqualTo(32000));
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(file.PixelData), Is.EqualTo(0x1234));
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(file.PixelData.AsSpan(31998)), Is.EqualTo(0x1234));
    });
  }

  [Test]
  [Category("Unit")]
  public void Read_PutsWordsWhereTheInterleaveSaysAndNotInOrder() {
    // Two literal words, then the rest one repeated value.
    var control = new byte[] { unchecked((byte)(sbyte)-2), 0, 0x3E, 0x7E };
    var data = new byte[] { 0xAA, 0xAA, 0xBB, 0xBB, 0x00, 0x00 };
    var file = TinyReader.FromBytes(_Assemble(TinyResolution.High, control, data));

    Assert.Multiple(() => {
      // The first stored word is the screen's first; the second belongs a line further down,
      // because the file runs down a column rather than across a row.
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(file.PixelData), Is.EqualTo(unchecked((short)0xAAAA)));
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(file.PixelData.AsSpan(80 * 2)), Is.EqualTo(unchecked((short)0xBBBB)));
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(file.PixelData.AsSpan(2)), Is.EqualTo(0), "the next word across is not the next word stored");
    });
  }

  [Test]
  [Category("Unit")]
  public void Read_TakesTheAnimatedResolutionsAndTheirExtraBytes() {
    var control = new byte[] { 0, 0x3E, 0x80 };
    var data = new byte[] { 0x0F, 0x0F };

    using var ms = new MemoryStream();
    ms.WriteByte(3 + (byte)TinyResolution.High); // high resolution, colours cycling
    ms.Write(new byte[4]);                       // the settings that then precede the palette
    ms.Write(new byte[32]);
    Span<byte> buffer = stackalloc byte[2];
    BinaryPrimitives.WriteUInt16BigEndian(buffer, (ushort)control.Length);
    ms.Write(buffer);
    BinaryPrimitives.WriteUInt16BigEndian(buffer, (ushort)(data.Length / 2));
    ms.Write(buffer);
    ms.Write(control);
    ms.Write(data);

    var file = TinyReader.FromBytes(ms.ToArray());

    Assert.Multiple(() => {
      Assert.That(file.Resolution, Is.EqualTo(TinyResolution.High));
      Assert.That(file.Width, Is.EqualTo(640));
    });
  }

  [Test]
  [Category("Unit")]
  public void Read_RefusesAResolutionNoneOfTheSixNames()
    => Assert.Throws<InvalidDataException>(() => TinyReader.FromBytes(_Assemble((TinyResolution)9, [1], [0, 0])));

  [Test]
  [Category("Unit")]
  public void Read_RefusesAFileStatingMoreControlBytesThanItCarries() {
    var bytes = _Assemble(TinyResolution.High, [0, 0x3E, 0x80], [0, 0]);
    BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(33), 5000);

    Assert.Throws<InvalidDataException>(() => TinyReader.FromBytes(bytes));
  }

  [Test]
  [Category("Unit")]
  public void Decoded_DrawsAMonochromeScreenInBlackAndWhiteWhateverThePaletteHolds() {
    // The second entry is red, which a high-resolution screen does not use.
    short[] palette = [0x0777, 0x0700, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    var file = TinyReader.FromBytes(_Assemble(TinyResolution.High, [0, 0x3E, 0x80], [0xFF, 0xFF], palette));
    var image = TinyFile.ToRawImage(file);

    Assert.That(image.Palette, Is.Not.Null);
    Assert.Multiple(() => {
      Assert.That(image.Palette![0], Is.EqualTo(255), "paper is white");
      Assert.That(image.Palette![3], Is.EqualTo(0), "ink is black, not the red the file leaves in the register");
      Assert.That(image.Palette![4], Is.EqualTo(0));
      Assert.That(image.Palette![5], Is.EqualTo(0));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_KeepsEveryWordOfTheScreen() {
    var screen = _Screen(i => (short)(i * 2654435761L % 65521));
    var original = new TinyFile {
      Width = 320, Height = 200, Resolution = TinyResolution.Low,
      Palette = new short[16], PixelData = screen,
    };

    var restored = TinyReader.FromBytes(TinyWriter.ToBytes(original));

    Assert.That(restored.PixelData, Is.EqualTo(screen));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_KeepsAScreenOfLongRunsAndShortensIt() {
    var screen = _Screen(i => (short)(i / 500));
    var original = new TinyFile {
      Width = 640, Height = 400, Resolution = TinyResolution.High,
      Palette = new short[16], PixelData = screen,
    };

    var written = TinyWriter.ToBytes(original);

    Assert.Multiple(() => {
      Assert.That(TinyReader.FromBytes(written).PixelData, Is.EqualTo(screen));
      Assert.That(written, Has.Length.LessThan(32000), "runs are what the format is for");
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_KeepsAScreenThatCannotBeShortenedAtAll() {
    var screen = _Screen(i => (short)(i % 2 == 0 ? i : ~i));
    var original = new TinyFile {
      Width = 640, Height = 400, Resolution = TinyResolution.High,
      Palette = new short[16], PixelData = screen,
    };

    Assert.That(TinyReader.FromBytes(TinyWriter.ToBytes(original)).PixelData, Is.EqualTo(screen));
  }
}
