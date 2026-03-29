using System;
using FileFormat.Flif.Codec;

namespace FileFormat.Flif.Tests;

[TestFixture]
public sealed class FlifRangeCoderTests {

  [Test]
  [Category("Unit")]
  public void EncodeDecode_SingleBitZero_RoundTrips() {
    var encoder = new FlifRangeEncoder();
    encoder.EncodeBit(0, 2048);
    var data = encoder.Finish();

    var decoder = new FlifRangeDecoder(data, 0);
    Assert.That(decoder.DecodeBit(2048), Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void EncodeDecode_SingleBitOne_RoundTrips() {
    var encoder = new FlifRangeEncoder();
    encoder.EncodeBit(1, 2048);
    var data = encoder.Finish();

    var decoder = new FlifRangeDecoder(data, 0);
    Assert.That(decoder.DecodeBit(2048), Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void EncodeDecode_MultipleBits_RoundTrips() {
    var encoder = new FlifRangeEncoder();
    var bits = new[] { 0, 1, 0, 1, 1, 0, 0, 1 };
    foreach (var b in bits)
      encoder.EncodeBit(b, 2048);
    var data = encoder.Finish();

    var decoder = new FlifRangeDecoder(data, 0);
    for (var i = 0; i < bits.Length; ++i)
      Assert.That(decoder.DecodeBit(2048), Is.EqualTo(bits[i]), $"Bit {i}");
  }

  [Test]
  [Category("Unit")]
  public void EncodeDecode_VariousChances_RoundTrips() {
    var encoder = new FlifRangeEncoder();
    var pairs = new[] {
      (bit: 0, chance: 1000),
      (bit: 1, chance: 3000),
      (bit: 0, chance: 100),
      (bit: 1, chance: 4000),
      (bit: 0, chance: 2048),
      (bit: 1, chance: 2048),
    };
    foreach (var (bit, chance) in pairs)
      encoder.EncodeBit(bit, chance);
    var data = encoder.Finish();

    var decoder = new FlifRangeDecoder(data, 0);
    for (var i = 0; i < pairs.Length; ++i)
      Assert.That(decoder.DecodeBit(pairs[i].chance), Is.EqualTo(pairs[i].bit), $"Pair {i}");
  }

  [Test]
  [Category("Unit")]
  public void EncodeDecode_Equiprobable_RoundTrips() {
    var encoder = new FlifRangeEncoder();
    var bits = new[] { 0, 1, 1, 0, 1, 0, 0, 0, 1, 1 };
    foreach (var b in bits)
      encoder.EncodeEquiprobable(b);
    var data = encoder.Finish();

    var decoder = new FlifRangeDecoder(data, 0);
    for (var i = 0; i < bits.Length; ++i)
      Assert.That(decoder.DecodeEquiprobable(), Is.EqualTo(bits[i]), $"Bit {i}");
  }

  [Test]
  [Category("Unit")]
  public void EncodeDecode_Uniform_RoundTrips() {
    var encoder = new FlifRangeEncoder();
    encoder.EncodeUniform(42, 255);
    encoder.EncodeUniform(0, 255);
    encoder.EncodeUniform(255, 255);
    encoder.EncodeUniform(7, 8);
    var data = encoder.Finish();

    var decoder = new FlifRangeDecoder(data, 0);
    Assert.That(decoder.DecodeUniform(255), Is.EqualTo(42));
    Assert.That(decoder.DecodeUniform(255), Is.EqualTo(0));
    Assert.That(decoder.DecodeUniform(255), Is.EqualTo(255));
    Assert.That(decoder.DecodeUniform(8), Is.EqualTo(7));
  }

  [Test]
  [Category("Unit")]
  public void EncodeDecode_500RandomBits_RoundTrips() {
    var rng = new Random(12345);
    var encoder = new FlifRangeEncoder();
    var bits = new int[500];
    var chances = new int[500];
    for (var i = 0; i < bits.Length; ++i) {
      bits[i] = rng.Next(2);
      chances[i] = rng.Next(1, 4096);
      encoder.EncodeBit(bits[i], chances[i]);
    }
    var data = encoder.Finish();

    var decoder = new FlifRangeDecoder(data, 0);
    for (var i = 0; i < bits.Length; ++i)
      Assert.That(decoder.DecodeBit(chances[i]), Is.EqualTo(bits[i]), $"Bit {i}");
  }
}

[TestFixture]
public sealed class FlifNearZeroCoderTests {

  [Test]
  [Category("Unit")]
  public void EncodeDecode_SingleValue_RoundTrips() {
    var encoder = new FlifRangeEncoder();
    var ctx = new FlifNearZeroContext();
    ctx.Encode(encoder, 42, 0, 255);
    var data = encoder.Finish();

    var decoder = new FlifRangeDecoder(data, 0);
    var ctx2 = new FlifNearZeroContext();
    Assert.That(ctx2.Decode(decoder, 0, 255), Is.EqualTo(42));
  }

  [Test]
  [Category("Unit")]
  public void EncodeDecode_MultipleValues_RoundTrips() {
    var encoder = new FlifRangeEncoder();
    var ctx = new FlifNearZeroContext();
    var values = new[] { 0, 128, 255, 64, 192, 1, 254 };
    foreach (var v in values)
      ctx.Encode(encoder, v, 0, 255);
    var data = encoder.Finish();

    var decoder = new FlifRangeDecoder(data, 0);
    var ctx2 = new FlifNearZeroContext();
    for (var i = 0; i < values.Length; ++i)
      Assert.That(ctx2.Decode(decoder, 0, 255), Is.EqualTo(values[i]), $"Value {i}");
  }

  [Test]
  [Category("Unit")]
  public void EncodeDecode_NegativeRange_RoundTrips() {
    var encoder = new FlifRangeEncoder();
    var ctx = new FlifNearZeroContext();
    var values = new[] { -50, 0, 50, -128, 127 };
    foreach (var v in values)
      ctx.Encode(encoder, v, -128, 127);
    var data = encoder.Finish();

    var decoder = new FlifRangeDecoder(data, 0);
    var ctx2 = new FlifNearZeroContext();
    for (var i = 0; i < values.Length; ++i)
      Assert.That(ctx2.Decode(decoder, -128, 127), Is.EqualTo(values[i]), $"Value {i}");
  }
}

[TestFixture]
public sealed class FlifManiacForestTests {

  [Test]
  [Category("Unit")]
  public void TreeSerialization_LeafTree_RoundTrips() {
    var encoder = new FlifRangeEncoder();
    var forest = new FlifManiacForest(1);
    forest.WriteTrees(encoder, 0, 255);
    var data = encoder.Finish();

    var decoder = new FlifRangeDecoder(data, 0);
    var forest2 = new FlifManiacForest(1);
    forest2.ReadTrees(decoder, 0, 255);

    Assert.That(forest2.GetTree(0).IsLeaf, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void MedianPredictor_ReturnsMedian() {
    Assert.That(FlifManiacForest.MedianPredictor(10, 20, 15), Is.EqualTo(15));
    Assert.That(FlifManiacForest.MedianPredictor(10, 10, 10), Is.EqualTo(10));
    Assert.That(FlifManiacForest.MedianPredictor(0, 0, 0), Is.EqualTo(0));
  }
}

[TestFixture]
public sealed class FlifChannelCoderTests {

  [Test]
  [Category("Unit")]
  public void EncodeDecode_SingleChannel_RoundTrips() {
    var encoder = new FlifRangeEncoder();
    var forest = new FlifManiacForest(1);
    var channelEncoder = new FlifChannelEncoder(encoder, forest, 1, 0, 255);
    var channels = new[] { new[] { 0, 64, 128, 255 } };
    channelEncoder.EncodeNonInterlaced(channels, 2, 2);
    var data = encoder.Finish();

    var decoder = new FlifRangeDecoder(data, 0);
    var forest2 = new FlifManiacForest(1);
    forest2.ReadTrees(decoder, 0, 255);
    var channelDecoder = new FlifChannelDecoder(decoder, forest2, 1, 0, 255);
    var decoded = channelDecoder.DecodeNonInterlaced(2, 2);

    Assert.That(decoded[0], Is.EqualTo(channels[0]));
  }

  [Test]
  [Category("Unit")]
  public void EncodeDecode_ThreeChannels_RoundTrips() {
    var encoder = new FlifRangeEncoder();
    var forest = new FlifManiacForest(3);
    var channelEncoder = new FlifChannelEncoder(encoder, forest, 3, 0, 255);
    var channels = new[] {
      new[] { 0, 34, 68, 102 },
      new[] { 17, 51, 85, 119 },
      new[] { 34, 68, 102, 136 },
    };
    channelEncoder.EncodeNonInterlaced(channels, 2, 2);
    var data = encoder.Finish();

    var decoder = new FlifRangeDecoder(data, 0);
    var forest2 = new FlifManiacForest(3);
    forest2.ReadTrees(decoder, 0, 255);
    var channelDecoder = new FlifChannelDecoder(decoder, forest2, 3, 0, 255);
    var decoded = channelDecoder.DecodeNonInterlaced(2, 2);

    for (var c = 0; c < 3; ++c)
      Assert.That(decoded[c], Is.EqualTo(channels[c]), $"Channel {c}");
  }

  [Test]
  [Category("Unit")]
  public void EncodeDecode_FourChannels_RoundTrips() {
    var encoder = new FlifRangeEncoder();
    var forest = new FlifManiacForest(4);
    var channelEncoder = new FlifChannelEncoder(encoder, forest, 4, 0, 255);
    var channels = new[] {
      new[] { 0, 34, 68, 102 },
      new[] { 17, 51, 85, 119 },
      new[] { 34, 68, 102, 136 },
      new[] { 255, 200, 128, 64 },
    };
    channelEncoder.EncodeNonInterlaced(channels, 2, 2);
    var data = encoder.Finish();

    var decoder = new FlifRangeDecoder(data, 0);
    var forest2 = new FlifManiacForest(4);
    forest2.ReadTrees(decoder, 0, 255);
    var channelDecoder = new FlifChannelDecoder(decoder, forest2, 4, 0, 255);
    var decoded = channelDecoder.DecodeNonInterlaced(2, 2);

    for (var c = 0; c < 4; ++c)
      Assert.That(decoded[c], Is.EqualTo(channels[c]), $"Channel {c}");
  }

  [Test]
  [Category("Unit")]
  public void EncodeDecode_NegativeValues_RoundTrips() {
    var encoder = new FlifRangeEncoder();
    var forest = new FlifManiacForest(3);
    var channelEncoder = new FlifChannelEncoder(encoder, forest, 3, -255, 510);
    var channels = new[] {
      new[] { 50, 100, 150, 200 },
      new[] { -100, 50, -50, 100 },
      new[] { -50, -25, 0, 75 },
    };
    channelEncoder.EncodeNonInterlaced(channels, 2, 2);
    var data = encoder.Finish();

    var decoder = new FlifRangeDecoder(data, 0);
    var forest2 = new FlifManiacForest(3);
    forest2.ReadTrees(decoder, -255, 510);
    var channelDecoder = new FlifChannelDecoder(decoder, forest2, 3, -255, 510);
    var decoded = channelDecoder.DecodeNonInterlaced(2, 2);

    for (var c = 0; c < 3; ++c)
      Assert.That(decoded[c], Is.EqualTo(channels[c]), $"Channel {c}");
  }

  [Test]
  [Category("Unit")]
  public void EncodeDecode_4x4_RoundTrips() {
    var encoder = new FlifRangeEncoder();
    var forest = new FlifManiacForest(3);
    var channelEncoder = new FlifChannelEncoder(encoder, forest, 3, 0, 255);
    var channels = new int[3][];
    for (var c = 0; c < 3; ++c) {
      channels[c] = new int[16];
      for (var i = 0; i < 16; ++i)
        channels[c][i] = (i * 17 + c * 37) % 256;
    }
    channelEncoder.EncodeNonInterlaced(channels, 4, 4);
    var data = encoder.Finish();

    var decoder = new FlifRangeDecoder(data, 0);
    var forest2 = new FlifManiacForest(3);
    forest2.ReadTrees(decoder, 0, 255);
    var channelDecoder = new FlifChannelDecoder(decoder, forest2, 3, 0, 255);
    var decoded = channelDecoder.DecodeNonInterlaced(4, 4);

    for (var c = 0; c < 3; ++c)
      Assert.That(decoded[c], Is.EqualTo(channels[c]), $"Channel {c}");
  }
}

[TestFixture]
public sealed class FlifTransformTests {

  [Test]
  [Category("Unit")]
  public void YCoCg_ApplyReverse_IsIdentity() {
    var channels = new[] {
      new[] { 0, 100, 200, 50 },
      new[] { 50, 150, 100, 25 },
      new[] { 100, 200, 50, 75 },
    };

    var origChannels = new int[3][];
    for (var c = 0; c < 3; ++c)
      origChannels[c] = (int[])channels[c].Clone();

    var transform = new FlifYCoCgTransform();
    transform.Apply(channels, 2, 2);
    transform.Reverse(channels, 2, 2);

    for (var c = 0; c < 3; ++c)
      Assert.That(channels[c], Is.EqualTo(origChannels[c]), $"Channel {c}");
  }

  [Test]
  [Category("Integration")]
  public void TransformChain_EncodeDecodeReverse_PreservesData() {
    var channels = new[] {
      new[] { 0, 34, 68, 102 },
      new[] { 17, 51, 85, 119 },
      new[] { 34, 68, 102, 136 },
    };

    var origChannels = new int[3][];
    for (var c = 0; c < 3; ++c)
      origChannels[c] = (int[])channels[c].Clone();

    var ycoCg = new FlifYCoCgTransform();
    ycoCg.Apply(channels, 2, 2);

    var compact = FlifChannelCompactTransform.Create(3);
    compact.Apply(channels, 2, 2);

    var bounds = FlifBoundsTransform.Create(3);
    bounds.Apply(channels, 2, 2);

    var encodeMin = int.MaxValue;
    var encodeMax = int.MinValue;
    for (var c = 0; c < 3; ++c)
      for (var i = 0; i < 4; ++i) {
        encodeMin = Math.Min(encodeMin, channels[c][i]);
        encodeMax = Math.Max(encodeMax, channels[c][i]);
      }

    var encoder = new FlifRangeEncoder();
    ycoCg.WriteTransform(encoder);
    compact.WriteTransform(encoder);
    bounds.WriteTransform(encoder);
    FlifTransform.WriteEndOfChain(encoder);

    var forest = new FlifManiacForest(3);
    var channelEncoder = new FlifChannelEncoder(encoder, forest, 3, encodeMin, encodeMax);
    channelEncoder.EncodeNonInterlaced(channels, 2, 2);
    var data = encoder.Finish();

    var decoder = new FlifRangeDecoder(data, 0);
    var transforms = new System.Collections.Generic.List<FlifTransform>();
    while (true) {
      var t = FlifTransform.ReadTransform(decoder, 3);
      if (t == null)
        break;
      transforms.Add(t);
    }

    var forest2 = new FlifManiacForest(3);
    forest2.ReadTrees(decoder, encodeMin, encodeMax);
    var channelDecoder = new FlifChannelDecoder(decoder, forest2, 3, encodeMin, encodeMax);
    var decoded = channelDecoder.DecodeNonInterlaced(2, 2);

    for (var i = transforms.Count - 1; i >= 0; --i)
      transforms[i].Reverse(decoded, 2, 2);

    for (var c = 0; c < 3; ++c)
      Assert.That(decoded[c], Is.EqualTo(origChannels[c]), $"Channel {c}");
  }
}
