using System;
using FileFormat.UtahRle;

namespace FileFormat.UtahRle.Tests;

[TestFixture]
public sealed class UtahRleDecoderTests {

  [Test]
  [Category("Unit")]
  public void Decode_EofOnly_ReturnsZeroFilledData() {
    var data = new byte[] { 7, 0, 0 }; // EOF opcode (long form: opcode + 16-bit count)
    var result = UtahRleDecoder.Decode(data, 2, 2, 1, null);

    Assert.That(result.Length, Is.EqualTo(4));
    Assert.That(result, Is.All.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void Decode_RunData_FillsPixels() {
    var file = new UtahRleFile {
      Width = 4,
      Height = 1,
      NumChannels = 1,
      PixelData = [42, 42, 42, 42]
    };

    var encoded = UtahRleEncoder.Encode(file.PixelData, 4, 1, 1);
    var decoded = UtahRleDecoder.Decode(encoded, 4, 1, 1, null);

    Assert.That(decoded, Is.EqualTo(new byte[] { 42, 42, 42, 42 }));
  }

  [Test]
  [Category("Unit")]
  public void Decode_ByteData_LiteralValues() {
    var file = new UtahRleFile {
      Width = 3,
      Height = 1,
      NumChannels = 1,
      PixelData = [10, 20, 30]
    };

    var encoded = UtahRleEncoder.Encode(file.PixelData, 3, 1, 1);
    var decoded = UtahRleDecoder.Decode(encoded, 3, 1, 1, null);

    Assert.That(decoded, Is.EqualTo(new byte[] { 10, 20, 30 }));
  }

  [Test]
  [Category("Unit")]
  public void Decode_WithBackground_FillsUnsetPixels() {
    var background = new byte[] { 128 };
    var data = new byte[] { 7, 0, 0 }; // EOF immediately (long form)

    var result = UtahRleDecoder.Decode(data, 2, 2, 1, background);

    Assert.That(result, Is.All.EqualTo(128));
  }

  [Test]
  [Category("Unit")]
  public void Decode_MultiChannel_InterleavedCorrectly() {
    var pixelData = new byte[] { 10, 20, 30, 40, 50, 60 }; // 2 pixels, 3 channels each
    var encoded = UtahRleEncoder.Encode(pixelData, 2, 1, 3);
    var decoded = UtahRleDecoder.Decode(encoded, 2, 1, 3, null);

    Assert.That(decoded, Is.EqualTo(pixelData));
  }
}
