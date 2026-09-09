using System;
using FileFormat.Core;
using FileFormat.WebP.Vp8;

namespace FileFormat.WebP.Tests;

[TestFixture]
public sealed class Vp8AdvancedEncoderTests {

  private static RawImage _Pattern(int width, int height, bool alpha = false) {
    var stride = alpha ? 4 : 3;
    var pixels = new byte[width * height * stride];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var offset = (y * width + x) * stride;
        pixels[offset] = (byte)((x * 17 + y * 11 + x * y * 3) & 255);
        pixels[offset + 1] = (byte)((x * 5 + y * 23 + (x ^ y) * 7) & 255);
        pixels[offset + 2] = (byte)((x * 29 + y * 3 + x * y) & 255);
        if (alpha)
          pixels[offset + 3] = (byte)(32 + (x * 19 + y * 13) % 224);
      }

    return new() {
      Width = width,
      Height = height,
      Format = alpha ? PixelFormat.Rgba32 : PixelFormat.Rgb24,
      PixelData = pixels,
    };
  }

  [TestCase(1, 0)]
  [TestCase(2, 1)]
  [TestCase(4, 2)]
  [TestCase(8, 3)]
  public void TokenPartitions_AreSignalledWithRfc6386PartitionCode(int partitions, int expectedCode) {
    var source = _Pattern(32, 128);
    var vp8 = Vp8Encoder.Encode(source, 70, partitions, threadTokenPartitions: false);

    Assert.That(_ReadTokenPartitionCode(vp8), Is.EqualTo(expectedCode));
    Assert.That(_CountTokenPartitions(vp8), Is.EqualTo(partitions));

    var decoded = Vp8Decoder.Decode(vp8, source.Width, source.Height);
    Assert.Multiple(() => {
      Assert.That(decoded, Has.Length.EqualTo(source.Width * source.Height * 3));
      Assert.That(_Psnr(source.PixelData, decoded), Is.GreaterThan(15.0));
    });
  }

  [TestCase(2)]
  [TestCase(4)]
  [TestCase(8)]
  public void TokenPartitions_ThreadedAndSerialEmissionAreByteIdentical(int partitions) {
    var source = _Pattern(48, 144);
    var serial = Vp8Encoder.Encode(source, 72, partitions, threadTokenPartitions: false);
    var threaded = Vp8Encoder.Encode(source, 72, partitions, threadTokenPartitions: true);

    Assert.That(threaded, Is.EqualTo(serial));
  }

  [TestCase(2)]
  [TestCase(4)]
  [TestCase(8)]
  public void TokenPartitionSizeTable_ContainsValid24BitLittleEndianLengths(int partitions) {
    var source = _Pattern(32, 128);
    var vp8 = Vp8Encoder.Encode(source, 68, partitions, threadTokenPartitions: true);

    var firstPartitionLength = _FirstPartitionLength(vp8);
    var tableOffset = 10 + firstPartitionLength;
    var payloadOffset = tableOffset + 3 * (partitions - 1);
    var remaining = vp8.Length - payloadOffset;

    Assert.That(remaining, Is.Positive);
    for (var i = 0; i < partitions - 1; ++i) {
      var offset = tableOffset + i * 3;
      var length = vp8[offset] | vp8[offset + 1] << 8 | vp8[offset + 2] << 16;
      Assert.That(length, Is.Positive, $"token partition {i} should carry at least its arithmetic-coder terminator");
      Assert.That(length, Is.LessThan(1 << 24));
      remaining -= length;
      Assert.That(remaining, Is.Positive, "the final token partition must remain inside the VP8 payload");
    }
  }

  [Test]
  public void MultiPass_TargetSizeImprovesOnInitialQuality() {
    var source = _Pattern(64, 64);
    var low = WebPLossyEncoder.EncodeWithResult(source, new() { Quality = 25, Passes = 1 });
    var high = WebPLossyEncoder.EncodeWithResult(source, new() { Quality = 90, Passes = 1 });
    Assume.That(high.EncodedSizeBytes, Is.GreaterThan(low.EncodedSizeBytes));

    var target = low.EncodedSizeBytes + (high.EncodedSizeBytes - low.EncodedSizeBytes) / 3;
    var initial = WebPLossyEncoder.EncodeWithResult(source, new() { Quality = 75, Passes = 1 });
    var controlled = WebPLossyEncoder.EncodeWithResult(source, new() {
      Quality = 75,
      Passes = 8,
      TargetSizeBytes = target,
      TokenPartitions = 4,
      UseTokenPartitionThreading = true,
    });

    var initialError = Math.Abs(initial.EncodedSizeBytes - target);
    var controlledError = Math.Abs(controlled.EncodedSizeBytes - target);
    TestContext.Out.WriteLine(
      $"target={target}, initial={initial.EncodedSizeBytes}@q{initial.Quality}, controlled={controlled.EncodedSizeBytes}@q{controlled.Quality}, passes={controlled.PassesUsed}");

    Assert.Multiple(() => {
      Assert.That(controlled.PassesUsed, Is.InRange(2, 8));
      Assert.That(controlledError, Is.LessThanOrEqualTo(initialError));
      Assert.That(_CountTokenPartitions(controlled.File.ImageData), Is.EqualTo(4));
    });
  }

  [Test]
  public void MultiPass_TargetPsnrConvergesTowardRequestedQualityMetric() {
    var source = _Pattern(64, 64);
    var low = WebPLossyEncoder.EncodeWithResult(source, new() { Quality = 30, Passes = 1 });
    var high = WebPLossyEncoder.EncodeWithResult(source, new() { Quality = 90, Passes = 1 });
    var lowPsnr = _Psnr(source.PixelData, Vp8Decoder.Decode(low.File.ImageData, source.Width, source.Height));
    var highPsnr = _Psnr(source.PixelData, Vp8Decoder.Decode(high.File.ImageData, source.Width, source.Height));
    Assume.That(highPsnr, Is.GreaterThan(lowPsnr + 0.25));

    var target = lowPsnr + (highPsnr - lowPsnr) * 0.45;
    var initial = WebPLossyEncoder.EncodeWithResult(source, new() {
      Quality = 75,
      Passes = 1,
      TargetPsnr = target,
    });
    var controlled = WebPLossyEncoder.EncodeWithResult(source, new() {
      Quality = 75,
      Passes = 8,
      TargetPsnr = target,
      TokenPartitions = 2,
    });

    Assert.That(initial.Psnr, Is.Not.Null);
    Assert.That(controlled.Psnr, Is.Not.Null);
    var initialError = Math.Abs(initial.Psnr!.Value - target);
    var controlledError = Math.Abs(controlled.Psnr!.Value - target);
    TestContext.Out.WriteLine(
      $"target={target:F3} dB, initial={initial.Psnr:F3}@q{initial.Quality}, controlled={controlled.Psnr:F3}@q{controlled.Quality}, passes={controlled.PassesUsed}");

    Assert.Multiple(() => {
      Assert.That(controlled.PassesUsed, Is.InRange(2, 8));
      Assert.That(controlledError, Is.LessThanOrEqualTo(initialError + 0.05));
      Assert.That(controlled.Psnr!.Value, Is.GreaterThanOrEqualTo(target - 0.75));
    });
  }

  [Test]
  public void AdvancedLossyEncoding_PreservesAlphaWithTokenPartitions() {
    var source = _Pattern(32, 64, alpha: true);
    var encoded = WebPLossyEncoder.Encode(source, new() {
      Quality = 75,
      TokenPartitions = 4,
      UseTokenPartitionThreading = true,
    });

    var bytes = WebPWriter.ToBytes(encoded);
    var decoded = WebPFile.ToRawImage(WebPFile.FromBytes(bytes));

    Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Rgba32));
    for (var i = 0; i < source.Width * source.Height; ++i)
      Assert.That(decoded.PixelData[i * 4 + 3], Is.EqualTo(source.PixelData[i * 4 + 3]), $"alpha pixel {i}");
  }

  [Test]
  public void AdvancedOptions_RejectInvalidRateControlAndPartitionSettings() {
    var source = _Pattern(16, 16);

    Assert.Multiple(() => {
      Assert.That(
        () => WebPLossyEncoder.Encode(source, new() { TokenPartitions = 3 }),
        Throws.TypeOf<ArgumentOutOfRangeException>());
      Assert.That(
        () => WebPLossyEncoder.Encode(source, new() { Passes = 0 }),
        Throws.TypeOf<ArgumentOutOfRangeException>());
      Assert.That(
        () => WebPLossyEncoder.Encode(source, new() { Passes = 11 }),
        Throws.TypeOf<ArgumentOutOfRangeException>());
      Assert.That(
        () => WebPLossyEncoder.Encode(source, new() { TargetSizeBytes = 1000, TargetPsnr = 30 }),
        Throws.TypeOf<ArgumentException>());
      Assert.That(
        () => WebPLossyEncoder.Encode(source, new() { MinQuality = 80, MaxQuality = 20 }),
        Throws.TypeOf<ArgumentException>());
    });
  }

  private static int _FirstPartitionLength(byte[] vp8)
    => vp8[0] >> 5 | vp8[1] << 3 | vp8[2] << 11;

  private static int _ReadTokenPartitionCode(byte[] vp8) {
    var firstPartitionLength = _FirstPartitionLength(vp8);
    var firstPartition = new byte[firstPartitionLength];
    Buffer.BlockCopy(vp8, 10, firstPartition, 0, firstPartitionLength);
    var reader = new Vp8Partition();
    reader.Init(firstPartition);

    reader.ReadBit(Vp8Partition.UniformProb); // colorspace
    reader.ReadBit(Vp8Partition.UniformProb); // clamp
    var useSegments = reader.ReadBit(Vp8Partition.UniformProb);
    Assert.That(useSegments, Is.False, "test helper assumes the encoder's no-segmentation path");
    reader.ReadBit(Vp8Partition.UniformProb); // filter type
    reader.ReadUint(Vp8Partition.UniformProb, 6); // level
    reader.ReadUint(Vp8Partition.UniformProb, 3); // sharpness
    var useFilterDeltas = reader.ReadBit(Vp8Partition.UniformProb);
    Assert.That(useFilterDeltas, Is.False, "test helper assumes mode/ref LF deltas are disabled");
    return (int)reader.ReadUint(Vp8Partition.UniformProb, 2);
  }

  private static int _CountTokenPartitions(byte[] vp8) => 1 << _ReadTokenPartitionCode(vp8);

  private static double _Psnr(byte[] expected, byte[] actual) {
    Assert.That(actual, Has.Length.EqualTo(expected.Length));
    double sse = 0;
    for (var i = 0; i < expected.Length; ++i) {
      var delta = expected[i] - actual[i];
      sse += delta * delta;
    }
    if (sse == 0)
      return double.PositiveInfinity;
    return 10 * Math.Log10(255.0 * 255.0 / (sse / expected.Length));
  }
}
