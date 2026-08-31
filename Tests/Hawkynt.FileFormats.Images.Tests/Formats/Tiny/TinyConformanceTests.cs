using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Tiny.Tests;

[TestFixture]
public sealed class TinyConformanceTests {

  private static byte[] _Assemble(
    byte rawResolution,
    byte[] control,
    byte[] data,
    byte limits = 0,
    sbyte speedDirection = 0,
    ushort duration = 0
  ) {
    using var ms = new MemoryStream();
    ms.WriteByte(rawResolution);
    if (rawResolution >= 3) {
      ms.WriteByte(limits);
      ms.WriteByte(unchecked((byte)speedDirection));
      Span<byte> animation = stackalloc byte[2];
      BinaryPrimitives.WriteUInt16BigEndian(animation, duration);
      ms.Write(animation);
    }

    ms.Write(new byte[32]);
    Span<byte> buffer = stackalloc byte[2];
    BinaryPrimitives.WriteUInt16BigEndian(buffer, (ushort)control.Length);
    ms.Write(buffer);
    BinaryPrimitives.WriteUInt16BigEndian(buffer, (ushort)(data.Length / 2));
    ms.Write(buffer);
    ms.Write(control);
    ms.Write(data);
    return ms.ToArray();
  }

  private static TinyFile _File(
    TinyResolution resolution = TinyResolution.Low,
    bool animated = false,
    byte limits = 0,
    sbyte speedDirection = 0,
    ushort duration = 0
  ) {
    var mode = resolution switch {
      TinyResolution.Low => (320, 200),
      TinyResolution.Medium => (640, 200),
      TinyResolution.High => (640, 400),
      _ => throw new ArgumentOutOfRangeException(nameof(resolution)),
    };

    return new TinyFile {
      Width = mode.Item1,
      Height = mode.Item2,
      Resolution = resolution,
      HasColorAnimation = animated,
      AnimationLimits = limits,
      AnimationSpeedDirection = speedDirection,
      AnimationDuration = duration,
      Palette = new short[16],
      PixelData = new byte[TinyFile.ScreenDataSize],
    };
  }

  [Test]
  public void Reader_PreservesPublishedColorRotationHeader() {
    var bytes = _Assemble(
      3 + (byte)TinyResolution.Medium,
      [0, 0x3E, 0x80],
      [0x12, 0x34],
      limits: 0x2B,
      speedDirection: -6,
      duration: 0x1234
    );

    var file = TinyReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(file.Resolution, Is.EqualTo(TinyResolution.Medium));
      Assert.That(file.HasColorAnimation, Is.True);
      Assert.That(file.AnimationLimits, Is.EqualTo(0x2B));
      Assert.That(file.AnimationSpeedDirection, Is.EqualTo(-6));
      Assert.That(file.AnimationDuration, Is.EqualTo(0x1234));
      Assert.That(file.Width, Is.EqualTo(640));
      Assert.That(file.Height, Is.EqualTo(200));
    });
  }

  [Test]
  public void Writer_AllZeroScreen_UsesMinimalPublishedPacket() {
    var bytes = TinyWriter.ToBytes(_File());

    Assert.Multiple(() => {
      Assert.That(bytes, Has.Length.EqualTo(42));
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(33)), Is.EqualTo(3));
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(35)), Is.EqualTo(1));
      Assert.That(bytes.AsSpan(37, 3).ToArray(), Is.EqualTo(new byte[] { 0x00, 0x3E, 0x80 }));
      Assert.That(bytes.AsSpan(40, 2).ToArray(), Is.EqualTo(new byte[] { 0x00, 0x00 }));
    });
  }

  [Test]
  public void Writer_AnimatedHeaderPrecedesPaletteAndLengths() {
    var bytes = TinyWriter.ToBytes(_File(TinyResolution.Medium, true, 0x17, -9, 0x3456));

    Assert.Multiple(() => {
      Assert.That(bytes[0], Is.EqualTo(4));
      Assert.That(bytes[1], Is.EqualTo(0x17));
      Assert.That(bytes[2], Is.EqualTo(unchecked((byte)-9)));
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(3)), Is.EqualTo(0x3456));
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(37)), Is.EqualTo(3));
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(39)), Is.EqualTo(1));
    });
  }

  [Test]
  public void Decoder_NegativeControlCopiesLiteralWords() {
    var file = TinyReader.FromBytes(_Assemble(
      (byte)TinyResolution.High,
      [unchecked((byte)(sbyte)-2), 0, 0x3E, 0x7E],
      [0x11, 0x11, 0x22, 0x22, 0x00, 0x00]
    ));

    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(file.PixelData), Is.EqualTo(0x1111));
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(file.PixelData.AsSpan(160)), Is.EqualTo(0x2222));
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(file.PixelData.AsSpan(2)), Is.Zero);
    });
  }

  [Test]
  public void Decoder_OneControlReadsExtendedLiteralCount() {
    var data = new byte[(128 + 1) * 2];
    for (var i = 0; i < 128; ++i)
      BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(i * 2), (ushort)(i + 1));

    var file = TinyReader.FromBytes(_Assemble(
      (byte)TinyResolution.Low,
      [1, 0, 128, 0, 0x3E, 0x00],
      data
    ));

    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(file.PixelData), Is.EqualTo(1));
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(file.PixelData.AsSpan(160)), Is.EqualTo(2));
    });
  }

  [Test]
  public void Decoder_PositiveControlRepeatsShortRun() {
    var file = TinyReader.FromBytes(_Assemble(
      (byte)TinyResolution.Low,
      [2, 0, 0x3E, 0x7E],
      [0xA5, 0xA5, 0x00, 0x00]
    ));

    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(file.PixelData), Is.EqualTo(0xA5A5));
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(file.PixelData.AsSpan(160)), Is.EqualTo(0xA5A5));
    });
  }

  [Test]
  public void Decoder_RejectsTruncatedExtendedCount()
    => Assert.Throws<InvalidDataException>(() => TinyCompressor.Decompress([2, 0, 0x3E], [0, 0]));

  [Test]
  public void Decoder_RejectsTooSmallExtendedCount()
    => Assert.Throws<InvalidDataException>(() => TinyCompressor.Decompress([0, 0, 0x7F], [0, 0]));

  [Test]
  public void Decoder_RejectsTooLargeExtendedCount()
    => Assert.Throws<InvalidDataException>(() => TinyCompressor.Decompress([0, 0x80, 0x00], [0, 0]));

  [Test]
  public void Decoder_RejectsExpandedScreenOverrun()
    => Assert.Throws<InvalidDataException>(() => TinyCompressor.Decompress([0, 0x3E, 0x81], [0, 0]));

  [Test]
  public void Decoder_RejectsUnderExpansion()
    => Assert.Throws<InvalidDataException>(() => TinyCompressor.Decompress([127, 127, 127], [0, 0, 0, 0, 0, 0]));

  [Test]
  public void Decoder_RejectsTrailingControlBytes()
    => Assert.Throws<InvalidDataException>(() => TinyCompressor.Decompress([0, 0x3E, 0x80, 2], [0, 0]));

  [Test]
  public void Decoder_RejectsTrailingDataWords()
    => Assert.Throws<InvalidDataException>(() => TinyCompressor.Decompress([0, 0x3E, 0x80], [0, 0, 0, 0]));

  [Test]
  public void Reader_RejectsTruncatedDeclaredDataBlock() {
    var bytes = _Assemble((byte)TinyResolution.Low, [0, 0x3E, 0x80], [0, 0]);
    Array.Resize(ref bytes, bytes.Length - 1);
    Assert.Throws<InvalidDataException>(() => TinyReader.FromBytes(bytes));
  }

  [Test]
  public void Reader_RejectsSurplusFileBytes() {
    var bytes = _Assemble((byte)TinyResolution.Low, [0, 0x3E, 0x80], [0, 0]);
    Array.Resize(ref bytes, bytes.Length + 1);
    Assert.Throws<InvalidDataException>(() => TinyReader.FromBytes(bytes));
  }

  [Test]
  public void Writer_RejectsWrongGeometry() {
    var file = _File() with { Width = 640 };
    Assert.Throws<ArgumentException>(() => TinyWriter.ToBytes(file));
  }

  [Test]
  public void Writer_RejectsWrongPaletteLength() {
    var file = _File() with { Palette = new short[15] };
    Assert.Throws<ArgumentException>(() => TinyWriter.ToBytes(file));
  }

  [Test]
  public void Writer_RejectsWrongScreenLength() {
    var file = _File() with { PixelData = new byte[TinyFile.ScreenDataSize - 2] };
    Assert.Throws<ArgumentException>(() => TinyWriter.ToBytes(file));
  }

  [Test]
  public void Writer_RejectsAnimationMetadataWithoutAnimatedHeader() {
    var file = _File() with { AnimationDuration = 1 };
    Assert.Throws<ArgumentException>(() => TinyWriter.ToBytes(file));
  }

  [Test]
  public void RoundTrip_PreservesAnimationMetadataAndScreen() {
    var screen = new byte[TinyFile.ScreenDataSize];
    for (var i = 0; i < screen.Length; ++i)
      screen[i] = (byte)(i * 29 + 7);

    var original = _File(TinyResolution.Low, true, 0x3C, -4, 0x4567) with { PixelData = screen };
    var restored = TinyReader.FromBytes(TinyWriter.ToBytes(original));

    Assert.Multiple(() => {
      Assert.That(restored.HasColorAnimation, Is.True);
      Assert.That(restored.AnimationLimits, Is.EqualTo(original.AnimationLimits));
      Assert.That(restored.AnimationSpeedDirection, Is.EqualTo(original.AnimationSpeedDirection));
      Assert.That(restored.AnimationDuration, Is.EqualTo(original.AnimationDuration));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [TestCase(320, 200, 16, TinyResolution.Low)]
  [TestCase(640, 200, 4, TinyResolution.Medium)]
  [TestCase(640, 400, 2, TinyResolution.High)]
  public void FromRawImage_SelectsExactAtariMode(int width, int height, int colors, TinyResolution expected) {
    var image = new RawImage {
      Width = width,
      Height = height,
      Format = PixelFormat.Indexed8,
      PixelData = new byte[width * height],
      Palette = new byte[colors * 3],
      PaletteCount = colors,
    };

    var file = TinyFile.FromRawImage(image);
    Assert.That(file.Resolution, Is.EqualTo(expected));
  }

  [Test]
  public void FromRawImage_RejectsUnsupportedGeometry() {
    var image = new RawImage {
      Width = 321,
      Height = 200,
      Format = PixelFormat.Indexed8,
      PixelData = new byte[321 * 200],
      Palette = new byte[16 * 3],
      PaletteCount = 16,
    };

    Assert.Throws<ArgumentException>(() => TinyFile.FromRawImage(image));
  }
}
