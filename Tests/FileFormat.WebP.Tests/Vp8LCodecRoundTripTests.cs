using System;
using FileFormat.Core;
using FileFormat.WebP;
using FileFormat.WebP.Vp8L;

namespace FileFormat.WebP.Tests;

[TestFixture]
public sealed class Vp8LCodecRoundTripTests {

  #region Encoder structural tests

  [Test]
  [Category("Unit")]
  public void Encoder_NullArgb_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Vp8LEncoder.Encode(null!, 4, 4, false));
  }

  [Test]
  [Category("Unit")]
  public void Encoder_ZeroWidth_ThrowsArgumentException() {
    Assert.Throws<ArgumentException>(() => Vp8LEncoder.Encode(new uint[16], 0, 4, false));
  }

  [Test]
  [Category("Unit")]
  public void Encoder_ZeroHeight_ThrowsArgumentException() {
    Assert.Throws<ArgumentException>(() => Vp8LEncoder.Encode(new uint[16], 4, 0, false));
  }

  [Test]
  [Category("Unit")]
  public void Encoder_ProducesVp8LSignatureByte() {
    var argb = new uint[4];
    Array.Fill(argb, 0xFF000000u);
    var encoded = Vp8LEncoder.Encode(argb, 2, 2, false);
    Assert.That(encoded[0], Is.EqualTo(0x2F));
  }

  [Test]
  [Category("Unit")]
  public void Encoder_HeaderEncodesCorrectDimensions_4x4() {
    var argb = new uint[16];
    Array.Fill(argb, 0xFF000000u);
    var encoded = Vp8LEncoder.Encode(argb, 4, 4, false);
    var bitField = (uint)encoded[1] | ((uint)encoded[2] << 8) | ((uint)encoded[3] << 16) | ((uint)encoded[4] << 24);
    var w = (int)(bitField & 0x3FFF) + 1;
    var h = (int)((bitField >> 14) & 0x3FFF) + 1;
    Assert.That(w, Is.EqualTo(4));
    Assert.That(h, Is.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void Encoder_HeaderEncodesCorrectDimensions_8x2() {
    var argb = new uint[16];
    Array.Fill(argb, 0xFF000000u);
    var encoded = Vp8LEncoder.Encode(argb, 8, 2, false);
    var bitField = (uint)encoded[1] | ((uint)encoded[2] << 8) | ((uint)encoded[3] << 16) | ((uint)encoded[4] << 24);
    var w = (int)(bitField & 0x3FFF) + 1;
    var h = (int)((bitField >> 14) & 0x3FFF) + 1;
    Assert.That(w, Is.EqualTo(8));
    Assert.That(h, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void Encoder_HeaderAlphaFlag_SetWhenHasAlpha() {
    var argb = new uint[4];
    Array.Fill(argb, 0x80FF0000u);
    var encoded = Vp8LEncoder.Encode(argb, 2, 2, true);
    var bitField = (uint)encoded[1] | ((uint)encoded[2] << 8) | ((uint)encoded[3] << 16) | ((uint)encoded[4] << 24);
    var alphaFlag = (bitField >> 28) & 1;
    Assert.That(alphaFlag, Is.EqualTo(1u));
  }

  [Test]
  [Category("Unit")]
  public void Encoder_HeaderAlphaFlag_ClearWhenNoAlpha() {
    var argb = new uint[4];
    Array.Fill(argb, 0xFFFF0000u);
    var encoded = Vp8LEncoder.Encode(argb, 2, 2, false);
    var bitField = (uint)encoded[1] | ((uint)encoded[2] << 8) | ((uint)encoded[3] << 16) | ((uint)encoded[4] << 24);
    var alphaFlag = (bitField >> 28) & 1;
    Assert.That(alphaFlag, Is.EqualTo(0u));
  }

  [Test]
  [Category("Unit")]
  public void Encoder_OutputMinimumLength_AtLeast5ByteHeader() {
    var argb = new uint[1];
    argb[0] = 0xFF000000u;
    var encoded = Vp8LEncoder.Encode(argb, 1, 1, false);
    Assert.That(encoded.Length, Is.GreaterThanOrEqualTo(5));
  }

  [Test]
  [Category("Unit")]
  public void Encoder_OutputContainsDataBeyondHeader() {
    var argb = new uint[4];
    Array.Fill(argb, 0xFF000000u);
    var encoded = Vp8LEncoder.Encode(argb, 2, 2, false);
    Assert.That(encoded.Length, Is.GreaterThan(5));
  }

  #endregion

  #region Decoder structural tests

  [Test]
  [Category("Unit")]
  public void Decoder_OutputLength_NoAlpha_4BytesPerPixel() {
    var argb = new uint[4];
    Array.Fill(argb, 0xFF000000u);
    var encoded = Vp8LEncoder.Encode(argb, 2, 2, false);
    var decoded = Vp8LDecoder.Decode(encoded, 2, 2, false);
    Assert.That(decoded.Length, Is.EqualTo(2 * 2 * 4));
  }

  [Test]
  [Category("Unit")]
  public void Decoder_OutputLength_WithAlpha_4BytesPerPixel() {
    var argb = new uint[4];
    Array.Fill(argb, 0x80000000u);
    var encoded = Vp8LEncoder.Encode(argb, 2, 2, true);
    var decoded = Vp8LDecoder.Decode(encoded, 2, 2, true);
    Assert.That(decoded.Length, Is.EqualTo(2 * 2 * 4));
  }

  [Test]
  [Category("Unit")]
  public void Decoder_OutputLength_LargerImage() {
    var argb = new uint[64];
    Array.Fill(argb, 0xFF808080u);
    var encoded = Vp8LEncoder.Encode(argb, 8, 8, false);
    var decoded = Vp8LDecoder.Decode(encoded, 8, 8, false);
    Assert.That(decoded.Length, Is.EqualTo(8 * 8 * 4));
  }

  [Test]
  [Category("Unit")]
  public void Decoder_NoAlpha_AlphaChannelIs0xFF() {
    var argb = new uint[4];
    Array.Fill(argb, 0xFF123456u);
    var encoded = Vp8LEncoder.Encode(argb, 2, 2, false);
    var decoded = Vp8LDecoder.Decode(encoded, 2, 2, false);
    for (var i = 0; i < 4; ++i)
      Assert.That(decoded[i * 4 + 3], Is.EqualTo(0xFF), "Alpha at pixel " + i + " should be 0xFF when hasAlpha=false");
  }

  #endregion

  #region Determinism

  [Test]
  [Category("Integration")]
  public void Encode_Deterministic_SameInputProducesSameOutput() {
    var argb = new uint[16];
    for (var i = 0; i < 16; ++i)
      argb[i] = 0xFF000000u | ((uint)(i * 16) << 16) | ((uint)(i * 8) << 8) | (uint)(i * 4);

    var encoded1 = Vp8LEncoder.Encode(argb, 4, 4, false);
    var encoded2 = Vp8LEncoder.Encode(argb, 4, 4, false);

    Assert.That(encoded1, Is.EqualTo(encoded2));
  }

  [Test]
  [Category("Integration")]
  public void Decode_Deterministic_SameInputProducesSameOutput() {
    var argb = new uint[16];
    Array.Fill(argb, 0xFF808080u);
    var encoded = Vp8LEncoder.Encode(argb, 4, 4, false);

    var decoded1 = Vp8LDecoder.Decode(encoded, 4, 4, false);
    var decoded2 = Vp8LDecoder.Decode(encoded, 4, 4, false);

    Assert.That(decoded1, Is.EqualTo(decoded2));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SolidColor_AllPixelsUniform() {
    var argb = new uint[16];
    Array.Fill(argb, 0xFFAABBCCu);
    var encoded = Vp8LEncoder.Encode(argb, 4, 4, false);
    var decoded = Vp8LDecoder.Decode(encoded, 4, 4, false);

    var r0 = decoded[0];
    var g0 = decoded[1];
    var b0 = decoded[2];
    for (var i = 1; i < 16; ++i) {
      var off = i * 4;
      Assert.That(decoded[off], Is.EqualTo(r0), "Red at pixel " + i);
      Assert.That(decoded[off + 1], Is.EqualTo(g0), "Green at pixel " + i);
      Assert.That(decoded[off + 2], Is.EqualTo(b0), "Blue at pixel " + i);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_1x1Image_ProducesValidOutput() {
    var argb = new uint[] { 0xFF123456u };
    var encoded = Vp8LEncoder.Encode(argb, 1, 1, false);
    var decoded = Vp8LDecoder.Decode(encoded, 1, 1, false);

    Assert.That(decoded.Length, Is.EqualTo(4));
    Assert.That(decoded[3], Is.EqualTo(0xFF), "Alpha should be 0xFF for no-alpha decode");
  }

  #endregion

  #region WebPFile API

  [Test]
  [Category("Integration")]
  public void WebPFile_FromRawImage_Rgb24_PreservesDimensions() {
    var raw = new RawImage {
      Width = 4,
      Height = 4,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[4 * 4 * 3]
    };
    for (var i = 0; i < raw.PixelData.Length; ++i)
      raw.PixelData[i] = (byte)(i % 256);

    var webp = WebPFile.FromRawImage(raw);
    var restored = WebPFile.ToRawImage(webp);

    Assert.That(restored.Width, Is.EqualTo(4));
    Assert.That(restored.Height, Is.EqualTo(4));
    Assert.That(restored.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(restored.PixelData.Length, Is.EqualTo(raw.PixelData.Length));
  }

  [Test]
  [Category("Integration")]
  public void WebPFile_FromRawImage_Rgba32_PreservesDimensions() {
    var raw = new RawImage {
      Width = 4,
      Height = 4,
      Format = PixelFormat.Rgba32,
      PixelData = new byte[4 * 4 * 4]
    };
    for (var i = 0; i < raw.PixelData.Length; ++i)
      raw.PixelData[i] = (byte)(i % 256);

    var webp = WebPFile.FromRawImage(raw);
    var restored = WebPFile.ToRawImage(webp);

    Assert.That(restored.Width, Is.EqualTo(4));
    Assert.That(restored.Height, Is.EqualTo(4));
    Assert.That(restored.Format, Is.EqualTo(PixelFormat.Rgba32));
    Assert.That(restored.PixelData.Length, Is.EqualTo(raw.PixelData.Length));
  }

  [Test]
  [Category("Integration")]
  public void WebPFile_FromRawImage_ProducesLosslessWebP() {
    var raw = new RawImage {
      Width = 2,
      Height = 2,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[12]
    };

    var webp = WebPFile.FromRawImage(raw);

    Assert.That(webp.IsLossless, Is.True);
    Assert.That(webp.Features.IsLossless, Is.True);
    Assert.That(webp.Features.Width, Is.EqualTo(2));
    Assert.That(webp.Features.Height, Is.EqualTo(2));
  }

  [Test]
  [Category("Integration")]
  public void WebPFile_FromRawImage_Gray8_ProducesValidOutput() {
    var raw = new RawImage {
      Width = 2,
      Height = 2,
      Format = PixelFormat.Gray8,
      PixelData = new byte[] { 0, 128, 255, 64 }
    };

    var webp = WebPFile.FromRawImage(raw);
    var restored = WebPFile.ToRawImage(webp);

    Assert.That(restored.Width, Is.EqualTo(2));
    Assert.That(restored.Height, Is.EqualTo(2));
    Assert.That(restored.PixelData.Length, Is.GreaterThan(0));
  }

  [Test]
  [Category("Integration")]
  public void WebPFile_ToBytes_FromBytes_PreservesContainerStructure() {
    var raw = new RawImage {
      Width = 2,
      Height = 2,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[12]
    };
    Array.Fill(raw.PixelData, (byte)128);

    var webp = WebPFile.FromRawImage(raw);
    var bytes = WebPFile.ToBytes(webp);
    var restored = WebPFile.FromBytes(bytes);

    Assert.That(restored.Features.Width, Is.EqualTo(2));
    Assert.That(restored.Features.Height, Is.EqualTo(2));
    Assert.That(restored.Features.IsLossless, Is.True);
    Assert.That(restored.ImageData, Is.EqualTo(webp.ImageData));
  }

  [Test]
  [Category("Integration")]
  public void WebPFile_RoundTrip_Rgb24_Deterministic() {
    var raw = new RawImage {
      Width = 4,
      Height = 4,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[48]
    };
    for (var i = 0; i < 48; ++i)
      raw.PixelData[i] = (byte)(i * 5);

    var restored1 = WebPFile.ToRawImage(WebPFile.FromRawImage(raw));
    var restored2 = WebPFile.ToRawImage(WebPFile.FromRawImage(raw));

    Assert.That(restored1.PixelData, Is.EqualTo(restored2.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void WebPFile_FromRawImage_NullImage_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => WebPFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void WebPFile_ToRawImage_NullFile_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => WebPFile.ToRawImage(null!));
  }

  #endregion
}
