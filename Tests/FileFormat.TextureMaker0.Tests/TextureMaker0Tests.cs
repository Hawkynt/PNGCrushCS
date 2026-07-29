using System;
using System.IO;
using FileFormat.Core;
using FileFormat.TextureMaker0;

namespace FileFormat.TextureMaker0.Tests;

[TestFixture]
public sealed class TextureMaker0Tests {

  private static TextureMaker0File _Sample() {
    var texels = new byte[TextureMaker0File.TexelDataSize];
    for (var i = 0; i < texels.Length; ++i)
      texels[i] = (byte)(i % TextureMaker0File.ColorCount);

    return new() { TexelData = texels, Color = 0x30 };
  }

  [Test]
  [Category("Unit")]
  public void FileSize_Is257() {
    // 16x16 luminance texels plus one colour byte.
    Assert.That(TextureMaker0File.FileSize, Is.EqualTo(257));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesTexelsAndColor() {
    var original = _Sample();
    var restored = TextureMaker0Reader.FromBytes(TextureMaker0Writer.ToBytes(original));

    Assert.Multiple(() => {
      Assert.That(restored.TexelData, Is.EqualTo(original.TexelData));
      Assert.That(restored.Color, Is.EqualTo(original.Color));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_RejectsAnyOtherLength()
    => Assert.Throws<InvalidDataException>(() => TextureMaker0Reader.FromBytes(new byte[TextureMaker0File.TexelDataSize]));

  [Test]
  [Category("Unit")]
  public void ToRawImage_ScalesEachTexelToA4x4Block() {
    var raw = TextureMaker0File.ToRawImage(_Sample());

    Assert.Multiple(() => {
      Assert.That(raw.Width, Is.EqualTo(TextureMaker0File.DisplaySize));
      Assert.That(raw.Height, Is.EqualTo(TextureMaker0File.DisplaySize));
      // The whole first 4x4 block comes from texel 0.
      for (var y = 0; y < TextureMaker0File.TexelScale; ++y)
      for (var x = 0; x < TextureMaker0File.TexelScale; ++x)
        Assert.That(raw.PixelData[y * TextureMaker0File.DisplaySize + x], Is.EqualTo(raw.PixelData[0]));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ProducesAConformantFile() {
    var data = new byte[TextureMaker0File.DisplaySize * TextureMaker0File.DisplaySize * 4];
    for (var i = 0; i < data.Length; i += 4) {
      data[i] = data[i + 1] = data[i + 2] = (byte)(i % 251);
      data[i + 3] = 255;
    }

    var raw = new RawImage {
      Width = TextureMaker0File.DisplaySize, Height = TextureMaker0File.DisplaySize,
      Format = PixelFormat.Rgba32, PixelData = data,
    };

    Assert.That(TextureMaker0Writer.ToBytes(TextureMaker0File.FromRawImage(raw)),
      Has.Length.EqualTo(TextureMaker0File.FileSize));
  }
}
