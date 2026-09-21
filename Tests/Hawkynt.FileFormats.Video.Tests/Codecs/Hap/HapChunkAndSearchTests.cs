using System;
using FileFormat.Codecs.Hap;
using FileFormat.Core;

namespace FileFormat.Codecs.Tests;

[TestFixture]
public sealed class HapChunkAndSearchTests {

  private static MediaStreamInfo _Stream(string code, int width, int height) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters(code),
    Width = width,
    Height = height,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };

  [Test]
  [Category("Unit")]
  public void RequestedChunkCountIsReducedToAWholeBlockDivisorAndDecodedInParallelForm() {
    const int width = 16;
    const int height = 16;
    var pixels = new byte[width * height * 4];
    for (var pixel = 0; pixel < width * height; ++pixel) {
      pixels[pixel * 4] = (byte)(pixel * 17);
      pixels[pixel * 4 + 1] = (byte)(pixel * 29);
      pixels[pixel * 4 + 2] = (byte)(pixel * 43);
      pixels[pixel * 4 + 3] = 255;
    }

    var source = new RawImage { Width = width, Height = height, Format = PixelFormat.Rgba32, PixelData = pixels };
    var encoder = HapVideoEncoder.Create(_Stream("Hap1", width, height), 7);
    Assert.That(encoder.ChunkCount, Is.EqualTo(4), "16 texture blocks cannot be split equally into 7, 6 or 5 chunks");
    Assert.That(encoder.TryEncode(source, 0, out var packet), Is.True);

    var frame = packet.Data.ToArray();
    Assert.That(frame[3] & 0xF0, Is.EqualTo(0xC0));
    _AssertChunkTables(frame, 4);

    var decoder = HapDecoder.Create(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(width));
      Assert.That(decoded.Height, Is.EqualTo(height));
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Rgb24));
    });
  }

  [Test]
  [Category("Unit")]
  public void HapQAlphaChunksBothNestedTextures() {
    const int width = 16;
    const int height = 16;
    var pixels = new byte[width * height * 4];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var at = (y * width + x) * 4;
        pixels[at] = (byte)(x * 13);
        pixels[at + 1] = (byte)(y * 11);
        pixels[at + 2] = (byte)(255 - x * 7);
        pixels[at + 3] = (byte)(x * 9 + y * 5);
      }

    var source = new RawImage { Width = width, Height = height, Format = PixelFormat.Rgba32, PixelData = pixels };
    var encoder = HapVideoEncoder.Create(_Stream("HapM", width, height), 4);
    Assert.That(encoder.TryEncode(source, 0, out var packet), Is.True);

    var frame = packet.Data.ToArray();
    var top = HapSection.ReadAt(frame, 0, "Hap Q Alpha frame");
    Assert.That(top.Type, Is.EqualTo(0x0D));
    var payload = frame.AsSpan(top.DataOffset, top.DataLength);
    var colour = HapSection.ReadAt(payload, 0, "Hap Q Alpha colour image");
    var alpha = HapSection.ReadAt(payload, colour.EndOffset, "Hap Q Alpha alpha image");
    Assert.Multiple(() => {
      Assert.That(colour.Type, Is.EqualTo(0xCF));
      Assert.That(alpha.Type, Is.EqualTo(0xC1));
    });

    _AssertChunkTables(payload.Slice(0, colour.EndOffset).ToArray(), 4);
    _AssertChunkTables(payload.Slice(colour.EndOffset, alpha.EndOffset - colour.EndOffset).ToArray(), 4);

    var decoder = HapDecoder.Create(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);

    // Hap Q Alpha carries alpha as BC4, which fits one eight-entry ramp between a block's lowest and
    // highest sample. Alpha here is x*9 + y*5, so every 4x4 block spans 3*9 + 3*5 = 42 and the ramp
    // steps by six; nothing in the block can land further than three from its source value. That is
    // also the best any endpoint pair achieves for this block, so three is the format's floor and not
    // a slack tolerance. What the assertion still catches is alpha being dropped, zeroed, replaced by
    // the colour image's, or quantised against the wrong block.
    for (var pixel = 0; pixel < width * height; ++pixel)
      Assert.That(
        decoded.PixelData[pixel * 4 + 3],
        Is.EqualTo(pixels[pixel * 4 + 3]).Within(3),
        $"alpha at pixel {pixel}");
  }

  [Test]
  [Category("Unit")]
  public void HapRUsesTwoSubsetModeWhenItReducesBlockError() {
    Span<byte> rgba = stackalloc byte[64];
    for (var y = 0; y < 4; ++y)
      for (var x = 0; x < 4; ++x) {
        var at = (y * 4 + x) * 4;
        if (x < 2) {
          rgba[at] = (byte)(20 + y * 50);
          rgba[at + 1] = (byte)(15 + y * 45);
          rgba[at + 2] = (byte)(10 + y * 40);
          rgba[at + 3] = (byte)(30 + y * 35);
        } else {
          rgba[at] = (byte)(230 - y * 35);
          rgba[at + 1] = (byte)(20 + y * 55);
          rgba[at + 2] = (byte)(210 - y * 45);
          rgba[at + 3] = (byte)(220 - y * 30);
        }
      }

    Span<byte> encoded = stackalloc byte[16];
    HapBc7Encoding.Compress(rgba, encoded);

    var mode = 0;
    while (mode < 8 && (encoded[0] & (1 << mode)) == 0)
      ++mode;
    Assert.That(mode, Is.EqualTo(7), "the split block should benefit from two independently fitted endpoint pairs");
  }

  [Test]
  [Category("Unit")]
  public void HapHdrUsesTwoSubsetModeWhenItReducesBlockError() {
    // BC6H mode 0 stores subsets one to three as five-bit deltas from the ten-bit base endpoint, so
    // the four endpoints must sit within about thirty quantisation steps of each other. A block whose
    // two halves are far apart therefore cannot be expressed in mode 0 at all, and the one-subset
    // direct mode — ten-bit endpoints and sixteen index levels — wins on error every time. Where the
    // two subsets are close but each carries its own ramp, one shared sixteen-level ramp has to span
    // both and mode 0's two eight-level ramps resolve each half more finely. That is the case the
    // partition search exists for, so that is the case this fixture builds.
    Span<ushort> rgb = stackalloc ushort[48];
    for (var y = 0; y < 4; ++y)
      for (var x = 0; x < 4; ++x) {
        var at = (y * 4 + x) * 3;
        var value = 1.0f + (x < 2 ? 0.0f : 0.002f) + y * 0.005f;
        rgb[at] = BitConverter.HalfToUInt16Bits((Half)value);
        rgb[at + 1] = BitConverter.HalfToUInt16Bits((Half)(value * 0.9f));
        rgb[at + 2] = BitConverter.HalfToUInt16Bits((Half)(value * 1.1f));
      }

    Span<byte> encoded = stackalloc byte[16];
    HapBc6Encoding.Compress(rgb, false, encoded);

    Assert.That(encoded[0] & 0x03, Is.Zero, "BC6H mode 0 begins with the two-bit code 00");
  }

  [Test]
  [Category("Unit")]
  public void DeterministicHapRAndHdrProbeFramesAreAvailableToTheExternalReferenceOracle() {
    const int width = 8;
    const int height = 8;

    var rgba = new byte[width * height * 4];
    for (var pixel = 0; pixel < width * height; ++pixel) {
      rgba[pixel * 4] = (byte)(pixel * 19);
      rgba[pixel * 4 + 1] = (byte)(pixel * 37);
      rgba[pixel * 4 + 2] = (byte)(255 - pixel * 11);
      rgba[pixel * 4 + 3] = (byte)(31 + pixel * 3);
    }
    var rEncoder = HapVideoEncoder.Create(_Stream("Hap7", width, height), 4);
    Assert.That(rEncoder.TryEncode(
      new RawImage { Width = width, Height = height, Format = PixelFormat.Rgba32, PixelData = rgba },
      0,
      out var rPacket), Is.True);

    var hdr = new byte[width * height * 6];
    for (var pixel = 0; pixel < width * height; ++pixel) {
      _WriteHalf(hdr, pixel * 6, (Half)(0.25f + pixel * 0.03f));
      _WriteHalf(hdr, pixel * 6 + 2, (Half)(1.0f + pixel * 0.05f));
      _WriteHalf(hdr, pixel * 6 + 4, (Half)(2.0f + pixel * 0.07f));
    }
    var hEncoder = HapVideoEncoder.Create(_Stream("HapH", width, height), 4);
    Assert.That(hEncoder.TryEncode(
      new RawImage { Width = width, Height = height, Format = PixelFormat.RgbF16, PixelData = hdr },
      0,
      out var hPacket), Is.True);

    TestContext.Out.WriteLine("HAP7_REFERENCE_PROBE=" + Convert.ToBase64String(rPacket.Data.Span));
    TestContext.Out.WriteLine("HAPH_REFERENCE_PROBE=" + Convert.ToBase64String(hPacket.Data.Span));
  }

  private static void _AssertChunkTables(byte[] frame, int expectedChunks) {
    var top = HapSection.ReadAt(frame, 0, "chunked Hap image");
    var payload = frame.AsSpan(top.DataOffset, top.DataLength);
    var container = HapSection.ReadAt(payload, 0, "decode instructions");
    Assert.That(container.Type, Is.EqualTo(0x01));

    var instructions = payload.Slice(container.DataOffset, container.DataLength);
    var compressors = HapSection.ReadAt(instructions, 0, "chunk compressor table");
    var sizes = HapSection.ReadAt(instructions, compressors.EndOffset, "chunk size table");
    Assert.Multiple(() => {
      Assert.That(compressors.Type, Is.EqualTo(0x02));
      Assert.That(compressors.DataLength, Is.EqualTo(expectedChunks));
      Assert.That(sizes.Type, Is.EqualTo(0x03));
      Assert.That(sizes.DataLength, Is.EqualTo(expectedChunks * 4));
    });
  }

  private static void _WriteHalf(Span<byte> destination, int offset, Half value) {
    var bits = BitConverter.HalfToUInt16Bits(value);
    destination[offset] = (byte)bits;
    destination[offset + 1] = (byte)(bits >> 8);
  }
}
