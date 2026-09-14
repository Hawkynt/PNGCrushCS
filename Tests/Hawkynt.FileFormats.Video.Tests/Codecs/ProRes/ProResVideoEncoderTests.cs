using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using FileFormat.Core;

namespace FileFormat.Codecs.ProRes.Tests;

/// <summary>
/// The Apple ProRes encoder: the bytes it lays down, the inverses it depends on, and its refusals.
/// </summary>
[TestFixture]
public class ProResVideoEncoderTests {

  // ============================================================================================
  // The pieces, against the decoding steps they invert
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void EveryCodebookWritesWhatItReads() {
    ProResGolombCode[] codebooks = [
      ProResGolombCode.ExpGolomb(0), ProResGolombCode.ExpGolomb(1), ProResGolombCode.ExpGolomb(2),
      ProResGolombCode.ExpGolomb(3), ProResGolombCode.ExpGolomb(5),
      new(1, 2, 3), new(2, 0, 1), new(1, 0, 1), new(1, 1, 2), new(2, 0, 2),
    ];

    foreach (var codebook in codebooks)
      for (var symbol = 0; symbol <= 4096; ++symbol) {
        var writer = new ProResBitWriter();
        codebook.Write(writer, symbol);
        var written = writer.BitCount;

        var reader = new ProResBitReader(writer.ToArray());
        Assert.That(codebook.Read(reader), Is.EqualTo(symbol), $"{codebook} lost the symbol {symbol}");
        Assert.That(reader.Position, Is.EqualTo(written), $"{codebook} disagrees about the length of symbol {symbol}");
      }
  }

  [Test]
  [Category("Unit")]
  public void ACodedComponentReadsBackAsTheCoefficientsItWasGiven() {
    var random = new Random(36);

    for (var attempt = 0; attempt < 200; ++attempt) {
      var blockCount = 1 + random.Next(32);
      var coefficients = new int[blockCount * 64];

      for (var i = 0; i < blockCount; ++i)
        coefficients[i] = random.Next(-2048, 2048);

      for (var i = blockCount; i < coefficients.Length; ++i)
        if (random.Next(4) == 0)
          coefficients[i] = random.Next(2) == 0 ? -1 - random.Next(600) : 1 + random.Next(600);

      var writer = new ProResBitWriter();
      ProResCoefficients.Encode(writer, coefficients, blockCount);

      Assert.That(
        ProResCoefficients.Decode(writer.ToArray(), blockCount), Is.EqualTo(coefficients).AsCollection,
        $"attempt {attempt} with {blockCount} blocks did not survive the entropy coding");
    }
  }

  [Test]
  [Category("Unit")]
  public void TheTransformsAreInversesOfEachOther() {
    var random = new Random(74);
    var block = new double[64];
    var original = new double[64];

    for (var attempt = 0; attempt < 500; ++attempt) {
      for (var i = 0; i < 64; ++i)
        original[i] = block[i] = random.NextDouble() * 512d - 256d;

      ProResForwardDct.Transform(block);
      ProResInverseDct.Transform(block);

      for (var i = 0; i < 64; ++i)
        Assert.That(block[i], Is.EqualTo(original[i]).Within(1e-9), $"sample {i} of attempt {attempt}");
    }
  }

  // ============================================================================================
  // The frame the encoder lays down
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AFrameSaysWhatTheSpecificationRequiresItToSay() {
    var frame = _Encode(_Ramp(64, 48), 64, 48, "apcn");

    var frameSize = (int)BinaryPrimitives.ReadUInt32BigEndian(frame);
    var headerSize = BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(8));

    Assert.Multiple(() => {
      Assert.That(frameSize, Is.EqualTo(frame.Length));
      Assert.That(frame.AsSpan(4, 4).ToArray(), Is.EqualTo("icpf"u8.ToArray()).AsCollection);
      Assert.That(headerSize, Is.EqualTo(148));
      Assert.That(frame[11], Is.EqualTo(0));
      Assert.That(frame[20] >> 6, Is.EqualTo(2));
      Assert.That((frame[20] >> 2) & 3, Is.EqualTo(0));
      Assert.That(frame[25] & 0x0F, Is.EqualTo(0));
      Assert.That(frame[27], Is.EqualTo(3));
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(16)), Is.EqualTo(64));
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(18)), Is.EqualTo(48));
    });
  }

  [Test]
  [Category("Unit")]
  public void TheWeightMatricesInTheHeaderAreTheProfilesOwn() {
    foreach (var profile in ProResProfile.All) {
      var frame = _Encode(_Ramp(32, 32), 32, 32, profile.Tag.ToString());

      Assert.Multiple(() => {
        Assert.That(frame.AsSpan(28, 64).ToArray(), Is.EqualTo(profile.LumaMatrix).AsCollection, $"{profile.Name} luma");
        Assert.That(frame.AsSpan(92, 64).ToArray(), Is.EqualTo(profile.ChromaMatrix).AsCollection, $"{profile.Name} chroma");
      });
    }
  }

  [Test]
  [Category("Unit")]
  public void ThePictureHeaderAndSliceTableAccountForEveryByte() {
    var frame = _Encode(_Ramp(160, 80), 160, 80, "apch");
    var picture = frame.AsSpan(8 + 148);

    var pictureHeaderSize = picture[0] >> 3;
    var pictureSize = (int)BinaryPrimitives.ReadUInt32BigEndian(picture[1..]);
    var sliceCount = BinaryPrimitives.ReadUInt16BigEndian(picture[5..]);
    var log2SliceSize = (picture[7] >> 4) & 3;

    Assert.Multiple(() => {
      Assert.That(pictureHeaderSize, Is.EqualTo(8));
      Assert.That(log2SliceSize, Is.EqualTo(3));
      Assert.That(sliceCount, Is.EqualTo(10));
      Assert.That(pictureSize, Is.EqualTo(frame.Length - 8 - 148));
    });

    var total = 0;
    for (var i = 0; i < sliceCount; ++i)
      total += BinaryPrimitives.ReadUInt16BigEndian(picture[(pictureHeaderSize + i * 2)..]);

    Assert.That(pictureHeaderSize + sliceCount * 2 + total, Is.EqualTo(pictureSize));
  }

  [Test]
  [Category("Unit")]
  public void EverySliceStatesAQuantisationIndexTheSpecificationPermits() {
    var frame = _Encode(_Noise(96, 64), 96, 64, "apco");
    var picture = frame.AsSpan(8 + 148);
    var pictureHeaderSize = picture[0] >> 3;
    var sliceCount = BinaryPrimitives.ReadUInt16BigEndian(picture[5..]);

    var at = pictureHeaderSize + sliceCount * 2;
    for (var i = 0; i < sliceCount; ++i) {
      var size = BinaryPrimitives.ReadUInt16BigEndian(picture[(pictureHeaderSize + i * 2)..]);
      var slice = picture.Slice(at, size);

      Assert.That(slice[0] >> 3, Is.EqualTo(6), $"slice {i}: six bytes without alpha");
      Assert.That(slice[1], Is.InRange(1, 224), $"slice {i} states a reserved quantisation index");
      at += size;
    }
  }

  // ============================================================================================
  // What comes back out
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void EveryFourTwoTwoProfileCodesAPictureThisPackagesOwnDecoderReadsBack() {
    foreach (var profile in ProResProfile.FourTwoTwo)
      foreach (var (width, height) in new[] { (16, 16), (41, 25), (64, 48), (160, 82) }) {
        var planes = _RoundTrip(_Ramp(width, height), width, height, profile.Tag.ToString());

        Assert.That(planes.Height, Is.EqualTo(height), $"{profile.Name} at {width}x{height}");
        Assert.That(planes.BitDepth, Is.EqualTo(10));
        Assert.That(planes.Luma.Take(width), Is.All.InRange(4, 1019), $"{profile.Name} at {width}x{height}");
      }
  }

  [Test]
  [Category("Unit")]
  public void TheHighQualityProfileReconstructsASmoothPictureCloselyAndProxyLessSo() {
    var (width, height) = (128, 96);
    var source = _Ramp(width, height);

    var worst = ProResProfile.FourTwoTwo
      .Select(profile => _WorstLumaError(source, width, height, profile.Tag.ToString()))
      .ToArray();

    Assert.Multiple(() => {
      Assert.That(worst[3], Is.LessThanOrEqualTo(8), "422 HQ on a smooth ramp");
      Assert.That(worst[3], Is.LessThanOrEqualTo(worst[0]), "422 HQ should not be worse than 422 Proxy");
      Assert.That(worst[2], Is.LessThanOrEqualTo(worst[0]), "422 should not be worse than 422 Proxy");
    });
  }

  [Test]
  [Category("Unit")]
  public void APictureOfOneFlatColourComesBackAsThatColour() {
    const int WIDTH = 48;
    const int HEIGHT = 32;
    var flat = new ushort[WIDTH * HEIGHT];
    Array.Fill(flat, (ushort)600);
    var chroma = new ushort[WIDTH / 2 * HEIGHT];
    Array.Fill(chroma, (ushort)512);

    foreach (var profile in ProResProfile.FourTwoTwo) {
      var planes = _RoundTrip(_Planes(WIDTH, HEIGHT, flat, chroma, chroma), WIDTH, HEIGHT, profile.Tag.ToString());

      Assert.Multiple(() => {
        Assert.That(planes.Luma, Is.All.EqualTo(600), $"{profile.Name} luma");
        Assert.That(planes.Cb, Is.All.EqualTo(512), $"{profile.Name} blue difference");
        Assert.That(planes.Cr, Is.All.EqualTo(512), $"{profile.Name} red difference");
      });
    }
  }

  [Test]
  [Category("Unit")]
  public void TheStreamDescriptionCarriesASampleEntryNamingTheProfile() {
    var stream = ProResVideoEncoder.Create(_Stream(320, 240, "apch")).DescribeStream();
    var entry = stream.CodecPrivateData.ToArray();

    Assert.Multiple(() => {
      Assert.That(stream.Codec, Is.EqualTo(CodecTag.FromCharacters("apch")));
      Assert.That(entry, Has.Length.EqualTo(86));
      Assert.That(BinaryPrimitives.ReadUInt32BigEndian(entry), Is.EqualTo(86u));
      Assert.That(entry.AsSpan(4, 4).ToArray(), Is.EqualTo("apch"u8.ToArray()).AsCollection);
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(entry.AsSpan(32)), Is.EqualTo(320));
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(entry.AsSpan(34)), Is.EqualTo(240));
    });
  }

  [Test]
  [Category("Unit")]
  public void EveryPacketIsAKeyFrame() {
    var encoder = ProResVideoEncoder.Create(_Stream(32, 32, "apcn"));

    for (var i = 0; i < 3; ++i) {
      Assert.That(encoder.TryEncode(_Ramp(32, 32), i, out var packet), Is.True);
      Assert.That(packet.IsKeyFrame, Is.True);
    }

    Assert.That(encoder.Flush(), Is.Empty);
  }

  // ============================================================================================
  // Profile selection and refusals
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AllSixProfilesAreAcceptedByTheEncoderRegistryPredicate() {
    foreach (var profile in ProResProfile.All) {
      var stream = _Stream(64, 64, profile.Tag.ToString());
      Assert.That(ProResVideoEncoder.Accepts(stream), Is.True, profile.Name);
      Assert.That(ProResVideoEncoder.Create(stream).DescribeStream().Codec, Is.EqualTo(profile.Tag), profile.Name);
    }
  }

  [Test]
  [Category("Unit")]
  public void ACodeThatIsNotAProResProfileIsRefusedByName() {
    var refusal = Assert.Throws<NotSupportedException>(() => ProResVideoEncoder.Create(_Stream(64, 64, "avc1")));
    Assert.That(refusal!.Message, Does.Contain("avc1").And.Contain("apcn"));
  }

  [Test]
  [Category("Unit")]
  public void AStreamWithNoSizeOrNoPicturesIsRefused() {
    Assert.Multiple(() => {
      Assert.Throws<NotSupportedException>(() => ProResVideoEncoder.Create(_Stream(0, 64, "apcn")));
      Assert.Throws<NotSupportedException>(() => ProResVideoEncoder.Create(_Stream(64, 0, "apcn")));
      Assert.Throws<NotSupportedException>(() => ProResVideoEncoder.Create(new() {
        Index = 0, Kind = MediaStreamKind.Audio, Codec = CodecTag.FromCharacters("apcn"), Width = 64, Height = 64,
      }));
    });
  }

  [Test]
  [Category("Unit")]
  public void APictureOfAnotherSizeIsRefusedRatherThanResized() {
    var encoder = ProResVideoEncoder.Create(_Stream(64, 48, "apcn"));
    var refusal = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(_Ramp(48, 64), 0, out _));

    Assert.That(refusal!.Message, Does.Contain("64x48").And.Contain("48x64"));
  }

  [Test]
  [Category("Unit")]
  public void ASampleThatDoesNotFitInTenBitsIsRefusedRatherThanClipped() {
    var picture = _Ramp(32, 32);
    BinaryPrimitives.WriteUInt16LittleEndian(picture.PixelData.AsSpan(8), 4095);

    var encoder = ProResVideoEncoder.Create(_Stream(32, 32, "apcn"));
    var refusal = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(picture, 0, out _));

    Assert.That(refusal!.Message, Does.Contain("ten bits"));
  }

  // ============================================================================================
  // Helpers
  // ============================================================================================

  private static MediaStreamInfo _Stream(int width, int height, string codec) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters(codec),
    Width = width,
    Height = height,
  };

  private static byte[] _Encode(RawImage picture, int width, int height, string codec) {
    var encoder = ProResVideoEncoder.Create(_Stream(width, height, codec));
    Assert.That(encoder.TryEncode(picture, 0, out var packet), Is.True);

    return packet.Data.ToArray();
  }

  private static ProResPlanes _RoundTrip(RawImage picture, int width, int height, string codec) {
    var frame = _Encode(picture, width, height, codec);
    var decoder = ProResVideoDecoder.Create(_Stream(width, height, codec));

    return decoder.DecodePlanes(frame, out _);
  }

  private static int _WorstLumaError(RawImage picture, int width, int height, string codec) {
    var planes = _RoundTrip(picture, width, height, codec);
    var worst = 0;

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var wanted = BinaryPrimitives.ReadUInt16LittleEndian(picture.PixelData.AsSpan((y * width + x) * 2));
        var got = planes.Luma[y * planes.Width + x];
        worst = Math.Max(worst, Math.Abs(wanted - got));
      }

    return worst;
  }

  private static RawImage _Ramp(int width, int height) {
    var chromaWidth = (width + 1) / 2;
    var luma = new ushort[width * height];
    var cb = new ushort[chromaWidth * height];
    var cr = new ushort[chromaWidth * height];

    for (var y = 0; y < height; ++y) {
      for (var x = 0; x < width; ++x)
        luma[y * width + x] = (ushort)(64 + (x * 3 + y * 2) % 877);

      for (var x = 0; x < chromaWidth; ++x) {
        cb[y * chromaWidth + x] = (ushort)(400 + (x + y) % 200);
        cr[y * chromaWidth + x] = (ushort)(600 - (x * 2 + y) % 200);
      }
    }

    return _Planes(width, height, luma, cb, cr);
  }

  private static RawImage _Noise(int width, int height) {
    var random = new Random(224);
    var chromaWidth = (width + 1) / 2;
    var luma = new ushort[width * height];
    var cb = new ushort[chromaWidth * height];
    var cr = new ushort[chromaWidth * height];

    for (var i = 0; i < luma.Length; ++i)
      luma[i] = (ushort)random.Next(64, 941);

    for (var i = 0; i < cb.Length; ++i) {
      cb[i] = (ushort)random.Next(64, 961);
      cr[i] = (ushort)random.Next(64, 961);
    }

    return _Planes(width, height, luma, cb, cr);
  }

  private static RawImage _Planes(int width, int height, ushort[] luma, ushort[] cb, ushort[] cr) {
    var samples = new byte[(luma.Length + cb.Length + cr.Length) * 2];
    var at = 0;

    foreach (var plane in new[] { luma, cb, cr })
      foreach (var sample in plane) {
        BinaryPrimitives.WriteUInt16LittleEndian(samples.AsSpan(at), sample);
        at += 2;
      }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Yuv422P10,
      PixelData = samples,
    };
  }
}
