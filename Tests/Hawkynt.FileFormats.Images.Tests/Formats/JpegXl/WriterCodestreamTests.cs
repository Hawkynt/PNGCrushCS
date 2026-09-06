using System;
using System.Text;
using FileFormat.JpegXl;
using FileFormat.JpegXl.Codec;

namespace FileFormat.JpegXl.Tests;

/// <summary>
/// What the writer emits has to be a JPEG XL codestream as the standard defines
/// it, not an arrangement only this package understands.
/// </summary>
/// <remarks>
/// The round trips below go through this package's own decoder, which is the
/// one measured against libjxl elsewhere in this folder. That it agrees is
/// necessary but not sufficient: the files these tests build were also handed to
/// <c>djxl</c> and came back sample for sample, which is the check a unit test
/// cannot make because it cannot assume the reference decoder is installed.
/// </remarks>
[TestFixture]
public sealed class WriterCodestreamTests {

  private static byte[] _Picture(int width, int height, int components, int seed) {
    var pixels = new byte[width * height * components];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 37 + seed * 101 >> 1 & 0xFF);
    return pixels;
  }

  private static JpegXlFile _Write(int width, int height, int components, int seed = 0) => new() {
    Width = width,
    Height = height,
    ComponentCount = components,
    BitsPerSample = 8,
    PixelData = _Picture(width, height, components, seed),
  };

  [Test]
  [Category("Unit")]
  public void Written_FileIsAContainerHoldingACodestream() {
    var bytes = JpegXlWriter.ToBytes(_Write(8, 8, 3));

    Assert.Multiple(() => {
      Assert.That(bytes[..4], Is.EqualTo(new byte[] { 0, 0, 0, 12 }), "the signature box states its own length");
      Assert.That(Encoding.ASCII.GetString(bytes, 4, 4), Is.EqualTo("JXL "));
      Assert.That(bytes[8..12], Is.EqualTo(new byte[] { 0x0D, 0x0A, 0x87, 0x0A }));
      Assert.That(Encoding.ASCII.GetString(bytes, 16, 4), Is.EqualTo("ftyp"));
      Assert.That(Encoding.ASCII.GetString(bytes, 20, 4), Is.EqualTo("jxl "));
      Assert.That(Encoding.ASCII.GetString(bytes, 36, 4), Is.EqualTo("jxlc"));
      Assert.That(bytes[40], Is.EqualTo(0xFF), "the codestream opens with its own signature");
      Assert.That(bytes[41], Is.EqualTo(0x0A));
    });
  }

  /// <summary>
  /// The header fields the decoder reads have to say what the frame actually is,
  /// because everything behind them is positioned by them.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void Written_MetadataStatesALosslessModularFrame() {
    var bytes = JpegXlWriter.ToBytes(_Write(37, 19, 3));

    Assert.That(JpegXlReader.TryReadSpecMetadata(bytes, out var metadata), Is.True);
    Assert.Multiple(() => {
      Assert.That(metadata.Width, Is.EqualTo(37));
      Assert.That(metadata.Height, Is.EqualTo(19));
      Assert.That(metadata.BitsPerSample, Is.EqualTo(8));
      Assert.That(metadata.IsFloatSample, Is.False);
      Assert.That(metadata.IsXybEncoded, Is.False);
      Assert.That(metadata.IsModularFrame, Is.True);
      Assert.That(metadata.IsProgressiveFrame, Is.False);
      Assert.That(metadata.NumExtraChannels, Is.Zero);
    });
  }

  [Test]
  [Category("Unit")]
  public void Written_AlphaBecomesAnExtraChannel() {
    var bytes = JpegXlWriter.ToBytes(_Write(6, 6, 4));

    Assert.That(JpegXlReader.TryReadSpecMetadata(bytes, out var metadata), Is.True);
    Assert.That(metadata.NumExtraChannels, Is.EqualTo(1));
  }

  [TestCase(1, 1)]
  [TestCase(3, 1)]
  [TestCase(1, 3)]
  [TestCase(5, 3)]
  [TestCase(7, 7)]
  [TestCase(8, 8)]
  [TestCase(13, 11)]
  [TestCase(31, 17)]
  [TestCase(64, 48)]
  [TestCase(100, 75)]
  [TestCase(129, 257)]
  [Category("Integration")]
  public void RoundTrip_Rgb_IsSampleForSample(int width, int height) {
    var original = _Write(width, height, 3, width + height);
    var restored = JpegXlReader.FromBytes(JpegXlWriter.ToBytes(original));

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(width));
      Assert.That(restored.Height, Is.EqualTo(height));
      Assert.That(restored.ComponentCount, Is.EqualTo(3));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [TestCase(1, 1)]
  [TestCase(5, 3)]
  [TestCase(17, 23)]
  [TestCase(64, 64)]
  [TestCase(255, 129)]
  [Category("Integration")]
  public void RoundTrip_Gray_IsSampleForSample(int width, int height) {
    var original = _Write(width, height, 1, width * 3 + height);
    var restored = JpegXlReader.FromBytes(JpegXlWriter.ToBytes(original));

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(width));
      Assert.That(restored.Height, Is.EqualTo(height));
      Assert.That(restored.ComponentCount, Is.EqualTo(1));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [TestCase(2)]
  [TestCase(4)]
  [Category("Integration")]
  public void RoundTrip_WithAlpha_IsSampleForSample(int components) {
    var original = _Write(9, 11, components, components);
    var restored = JpegXlReader.FromBytes(JpegXlWriter.ToBytes(original));

    Assert.Multiple(() => {
      Assert.That(restored.ComponentCount, Is.EqualTo(components));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  /// <summary>A picture of one colour is where a prefix code has a single symbol
  /// and states no bits at all per sample, which is its own path through the
  /// writer.</summary>
  [Test]
  [Category("Integration")]
  public void RoundTrip_SingleColour_IsSampleForSample() {
    var pixels = new byte[20 * 20 * 3];
    for (var i = 0; i < pixels.Length; i += 3) {
      pixels[i] = 17;
      pixels[i + 1] = 200;
      pixels[i + 2] = 99;
    }

    var original = new JpegXlFile {
      Width = 20, Height = 20, ComponentCount = 3, BitsPerSample = 8, PixelData = pixels,
    };
    var restored = JpegXlReader.FromBytes(JpegXlWriter.ToBytes(original));
    Assert.That(restored.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SixteenBit_IsSampleForSample() {
    const int width = 40;
    const int height = 24;
    var pixels = new byte[width * height * 3 * 2];
    for (var i = 0; i < width * height * 3; ++i) {
      var value = (ushort)(i * 613 % 65536);
      pixels[i * 2] = (byte)(value >> 8);
      pixels[i * 2 + 1] = (byte)value;
    }

    var original = new JpegXlFile {
      Width = width, Height = height, ComponentCount = 3, BitsPerSample = 16, PixelData = pixels,
    };
    var restored = JpegXlReader.FromBytes(JpegXlWriter.ToBytes(original));

    Assert.Multiple(() => {
      Assert.That(restored.BitsPerSample, Is.EqualTo(16));
      Assert.That(restored.PixelData, Is.EqualTo(pixels));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SixteenBitWithAlpha_IsSampleForSample() {
    const int width = 21;
    const int height = 13;
    var pixels = new byte[width * height * 4 * 2];
    for (var i = 0; i < width * height * 4; ++i) {
      var value = (ushort)(i * 4093 % 65536);
      pixels[i * 2] = (byte)(value >> 8);
      pixels[i * 2 + 1] = (byte)value;
    }

    var original = new JpegXlFile {
      Width = width, Height = height, ComponentCount = 4, BitsPerSample = 16, PixelData = pixels,
    };
    var restored = JpegXlReader.FromBytes(JpegXlWriter.ToBytes(original));

    Assert.Multiple(() => {
      Assert.That(restored.ComponentCount, Is.EqualTo(4));
      Assert.That(restored.BitsPerSample, Is.EqualTo(16));
      Assert.That(restored.PixelData, Is.EqualTo(pixels));
    });
  }

  /// <summary>
  /// A picture wider or taller than a group is stated a group at a time, and the
  /// sizes below are the ones that get that wrong: a side one pixel past a group
  /// boundary, a side one pixel short of one, and a picture that is many groups
  /// one way and part of a group the other.
  /// </summary>
  /// <remarks>
  /// A group is a thousand and twenty-four pixels a side here, so these are the
  /// smallest pictures that take more than one — which is what keeps a test of
  /// the arrangement from being a test of how fast it runs.
  /// </remarks>
  [TestCase(1025, 1)]
  [TestCase(1, 1025)]
  [TestCase(1025, 3)]
  [TestCase(3, 1025)]
  [TestCase(1025, 1025)]
  [TestCase(1023, 1027)]
  [TestCase(2049, 2)]
  [TestCase(2, 2049)]
  [TestCase(3073, 5)]
  [Category("Integration")]
  public void RoundTrip_SeveralGroups_IsSampleForSample(int width, int height) {
    var original = _Write(width, height, 3, width + height * 7);
    var restored = JpegXlReader.FromBytes(JpegXlWriter.ToBytes(original));

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(width));
      Assert.That(restored.Height, Is.EqualTo(height));
      Assert.That(restored.ComponentCount, Is.EqualTo(3));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [TestCase(1, 8)]
  [TestCase(2, 8)]
  [TestCase(4, 8)]
  [TestCase(1, 16)]
  [TestCase(2, 16)]
  [TestCase(3, 16)]
  [TestCase(4, 16)]
  [Category("Integration")]
  public void RoundTrip_SeveralGroups_EveryChannelArrangement_IsSampleForSample(int components, int bits) {
    const int width = 1027;
    const int height = 9;
    var pixels = new byte[width * height * components * (bits > 8 ? 2 : 1)];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 61 + components * 13 + bits >> 1 & 0xFF);

    var original = new JpegXlFile {
      Width = width, Height = height, ComponentCount = components, BitsPerSample = bits, PixelData = pixels,
    };
    var restored = JpegXlReader.FromBytes(JpegXlWriter.ToBytes(original));

    Assert.Multiple(() => {
      Assert.That(restored.ComponentCount, Is.EqualTo(components));
      Assert.That(restored.BitsPerSample, Is.EqualTo(bits));
      Assert.That(restored.PixelData, Is.EqualTo(pixels));
    });
  }

  /// <summary>
  /// A frame in several groups states a section per group, plus the two the
  /// format asks for whether or not it has anything to put in them and one per
  /// low-frequency group — and the sections have to add up to the frame, because
  /// each group is found by summing the ones before it.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void Written_SeveralGroups_StatesEverySectionTheFormatAsksFor() {
    // Three groups across by two down at a thousand and twenty-four a side, and
    // one low-frequency group, which is eight groups a side.
    var bytes = JpegXlWriter.ToBytes(_Write(2049, 1025, 3));
    var codestream = _Codestream(bytes);

    var reader = new JxlBitReader(codestream, 2);
    var (width, height) = JxlSizeHeader.Decode(reader);
    var metadata = JxlImageMetadata.Decode(reader);
    JxlCustomTransformData.Decode(reader, metadata.XybEncoded);
    reader.ZeroPadToByte();
    var frame = JxlSpecFrameHeader.Decode(reader, metadata, width, height);
    var toc = JxlFrameToc.Decode(reader, numGroups: 6, numPasses: 1, numDcGroups: 1);
    var frameBody = checked((int)(reader.BitsRead / 8));

    var total = 0;
    foreach (var size in toc.SectionSizes)
      total += size;

    Assert.Multiple(() => {
      Assert.That((int)frame.GroupSizeShift, Is.EqualTo(3), "a picture past one group takes the largest group there is");
      Assert.That(toc.SectionSizes, Has.Length.EqualTo(9), "one global, one low-frequency group, one high-frequency global, six groups");
      Assert.That(toc.SectionSizes[0], Is.GreaterThan(0), "the global section carries the tree and the code");
      Assert.That(toc.SectionSizes[1..3], Is.All.Zero, "a modular frame puts nothing in either of those");
      Assert.That(toc.SectionSizes[3..], Is.All.GreaterThan(0), "every group carries samples");
      Assert.That(frameBody + total, Is.EqualTo(codestream.Length), "the sections are the frame");
    });
  }

  /// <summary>The codestream out of the container box that holds it.</summary>
  private static byte[] _Codestream(byte[] file) {
    for (var at = 0; at + 8 <= file.Length; ++at)
      if (file[at + 4] == 'j' && file[at + 5] == 'x' && file[at + 6] == 'l' && file[at + 7] == 'c')
        return file[(at + 8)..];
    throw new AssertionException("The written file holds no codestream box.");
  }
}
