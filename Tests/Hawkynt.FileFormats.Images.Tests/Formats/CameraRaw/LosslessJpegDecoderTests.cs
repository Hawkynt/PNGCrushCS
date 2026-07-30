using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.CameraRaw;

namespace FileFormat.CameraRaw.Tests;

[TestFixture]
public sealed class LosslessJpegDecoderTests {

  // --- Huffman table parsing tests ---

  [Test]
  [Category("Unit")]
  public void ParseDht_SingleTable_ParsesCounts() {
    var tables = new Dictionary<int, LosslessJpegDecoder.HuffmanTable>();
    // Build a minimal DHT segment: table class=0 id=0, 16 length bytes, then values
    // 1 code of length 1 = value 0, 1 code of length 2 = value 1
    var dht = new byte[1 + 16 + 2]; // tableInfo + 16 counts + 2 values
    dht[0] = 0x00; // class=0, id=0
    dht[1] = 1; // 1 code of length 1
    dht[2] = 1; // 1 code of length 2
    // lengths 3-16 = 0 (already zeroed)
    dht[17] = 0; // value for length-1 code
    dht[18] = 1; // value for length-2 code

    LosslessJpegDecoder._ParseDht(dht, 0, dht.Length, tables);

    Assert.That(tables.ContainsKey(0), Is.True);
    Assert.That(tables[0].ValueCount, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void ParseDht_TwoTables_BothParsed() {
    var tables = new Dictionary<int, LosslessJpegDecoder.HuffmanTable>();
    // Table 0: 1 code of length 1 = value 0
    var table0 = new byte[1 + 16 + 1];
    table0[0] = 0x00;
    table0[1] = 1;
    table0[17] = 0;

    // Table 1: 1 code of length 2 = value 5
    var table1 = new byte[1 + 16 + 1];
    table1[0] = 0x01;
    table1[2] = 1; // 1 code of length 2
    table1[17] = 5;

    var combined = new byte[table0.Length + table1.Length];
    Array.Copy(table0, 0, combined, 0, table0.Length);
    Array.Copy(table1, 0, combined, table0.Length, table1.Length);

    LosslessJpegDecoder._ParseDht(combined, 0, combined.Length, tables);

    Assert.That(tables.ContainsKey(0), Is.True);
    Assert.That(tables.ContainsKey(1), Is.True);
    Assert.That(tables[0].Values[0], Is.EqualTo(0));
    Assert.That(tables[1].Values[0], Is.EqualTo(5));
  }

  [Test]
  [Category("Unit")]
  public void ParseDht_MinMaxCodes_Correct() {
    var tables = new Dictionary<int, LosslessJpegDecoder.HuffmanTable>();
    // 2 codes of length 2: values 0 and 1
    // Code 00 -> 0, Code 01 -> 1
    var dht = new byte[1 + 16 + 2];
    dht[0] = 0x00;
    dht[2] = 2; // 2 codes of length 2
    dht[17] = 0;
    dht[18] = 1;

    LosslessJpegDecoder._ParseDht(dht, 0, dht.Length, tables);

    var ht = tables[0];
    Assert.That(ht.MinCode[2], Is.EqualTo(0));
    Assert.That(ht.MaxCode[2], Is.EqualTo(1));
    Assert.That(ht.MinCode[1], Is.EqualTo(-1)); // no codes of length 1
  }

  // --- Prediction mode tests ---

  [Test]
  [Category("Unit")]
  [TestCase(1, Description = "Predictor 1: Ra (left)")]
  [TestCase(2, Description = "Predictor 2: Rb (above)")]
  [TestCase(3, Description = "Predictor 3: Rc (above-left)")]
  [TestCase(4, Description = "Predictor 4: Ra + Rb - Rc")]
  [TestCase(5, Description = "Predictor 5: Ra + ((Rb - Rc) >> 1)")]
  [TestCase(6, Description = "Predictor 6: Rb + ((Ra - Rc) >> 1)")]
  [TestCase(7, Description = "Predictor 7: (Ra + Rb) / 2")]
  public void Decode_PredictionMode_ProducesCorrectSamples(int predictor) {
    // Build a minimal lossless JPEG with the specified predictor
    // 4x2 image, 8-bit precision, single component
    // All differences are 0, so all samples should equal the initial prediction value (128 for 8-bit)
    var jpeg = _BuildMinimalLosslessJpeg(4, 2, 8, predictor, 1, allZeroDifferences: true);
    var decoded = LosslessJpegDecoder.Decode(jpeg);

    Assert.That(decoded.Width, Is.EqualTo(4));
    Assert.That(decoded.Height, Is.EqualTo(2));
    Assert.That(decoded.Precision, Is.EqualTo(8));

    // With all-zero differences and predictor 1-7, the first pixel = 128 (2^(P-1))
    Assert.That(decoded.Samples[0], Is.EqualTo(128));
  }

  [Test]
  [Category("Unit")]
  public void Decode_Predictor0_AllSamplesMatchDifferences() {
    // Predictor 0 = no prediction; diff IS the value
    var jpeg = _BuildMinimalLosslessJpeg(2, 2, 8, 0, 1, allZeroDifferences: true);
    var decoded = LosslessJpegDecoder.Decode(jpeg);

    // With predictor 0 and all-zero differences, all samples should be 0
    // except the first one which uses halfRange prediction
    Assert.That(decoded.Width, Is.EqualTo(2));
    Assert.That(decoded.Height, Is.EqualTo(2));
  }

  // --- Precision tests ---

  [Test]
  [Category("Unit")]
  public void Decode_12BitPrecision_PrecisionFieldCorrect() {
    var jpeg = _BuildMinimalLosslessJpeg(2, 2, 12, 1, 1, allZeroDifferences: true);
    var decoded = LosslessJpegDecoder.Decode(jpeg);

    Assert.That(decoded.Precision, Is.EqualTo(12));
    // halfRange for 12-bit = 2048
    Assert.That(decoded.Samples[0], Is.EqualTo(2048));
  }

  [Test]
  [Category("Unit")]
  public void Decode_14BitPrecision_PrecisionFieldCorrect() {
    var jpeg = _BuildMinimalLosslessJpeg(2, 2, 14, 1, 1, allZeroDifferences: true);
    var decoded = LosslessJpegDecoder.Decode(jpeg);

    Assert.That(decoded.Precision, Is.EqualTo(14));
    Assert.That(decoded.Samples[0], Is.EqualTo(8192));
  }

  [Test]
  [Category("Unit")]
  public void Decode_16BitPrecision_PrecisionFieldCorrect() {
    var jpeg = _BuildMinimalLosslessJpeg(2, 2, 16, 1, 1, allZeroDifferences: true);
    var decoded = LosslessJpegDecoder.Decode(jpeg);

    Assert.That(decoded.Precision, Is.EqualTo(16));
    Assert.That(decoded.Samples[0], Is.EqualTo(32768));
  }

  // --- Multi-component tests ---

  [Test]
  [Category("Unit")]
  public void Decode_TwoComponents_ComponentCountCorrect() {
    var jpeg = _BuildMinimalLosslessJpeg(4, 2, 8, 1, 2, allZeroDifferences: true);
    var decoded = LosslessJpegDecoder.Decode(jpeg);

    Assert.That(decoded.ComponentCount, Is.EqualTo(2));
    Assert.That(decoded.Samples.Length, Is.EqualTo(4 * 2 * 2));
  }

  [Test]
  [Category("Unit")]
  public void Decode_FourComponents_ComponentCountCorrect() {
    var jpeg = _BuildMinimalLosslessJpeg(4, 2, 8, 1, 4, allZeroDifferences: true);
    var decoded = LosslessJpegDecoder.Decode(jpeg);

    Assert.That(decoded.ComponentCount, Is.EqualTo(4));
    Assert.That(decoded.Samples.Length, Is.EqualTo(4 * 2 * 4));
  }

  // --- Dimensions tests ---

  [Test]
  [Category("Unit")]
  public void Decode_Width_Correct() {
    var jpeg = _BuildMinimalLosslessJpeg(8, 4, 8, 1, 1, allZeroDifferences: true);
    var decoded = LosslessJpegDecoder.Decode(jpeg);

    Assert.That(decoded.Width, Is.EqualTo(8));
    Assert.That(decoded.Height, Is.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void Decode_SinglePixel_Correct() {
    var jpeg = _BuildMinimalLosslessJpeg(1, 1, 8, 1, 1, allZeroDifferences: true);
    var decoded = LosslessJpegDecoder.Decode(jpeg);

    Assert.That(decoded.Width, Is.EqualTo(1));
    Assert.That(decoded.Height, Is.EqualTo(1));
    Assert.That(decoded.Samples.Length, Is.EqualTo(1));
    Assert.That(decoded.Samples[0], Is.EqualTo(128));
  }

  // --- Input validation tests ---

  [Test]
  [Category("Unit")]
  public void Decode_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => LosslessJpegDecoder.Decode(null!));
  }

  [Test]
  [Category("Unit")]
  public void Decode_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => LosslessJpegDecoder.Decode(new byte[2]));
  }

  [Test]
  [Category("Unit")]
  public void Decode_MissingSoi_ThrowsInvalidDataException() {
    var data = new byte[] { 0x00, 0x00, 0xFF, 0xD9 };
    Assert.Throws<InvalidDataException>(() => LosslessJpegDecoder.Decode(data));
  }

  [Test]
  [Category("Unit")]
  public void Decode_MissingSof3_ThrowsInvalidDataException() {
    // SOI followed by EOI (no SOF3 or SOS)
    var data = new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 };
    Assert.Throws<InvalidDataException>(() => LosslessJpegDecoder.Decode(data));
  }

  // --- Restart marker test ---

  [Test]
  [Category("Unit")]
  public void Decode_WithRestartInterval_DimensionsCorrect() {
    var jpeg = _BuildMinimalLosslessJpegWithDri(4, 4, 8, 1, 1, restartInterval: 4);
    var decoded = LosslessJpegDecoder.Decode(jpeg);

    Assert.That(decoded.Width, Is.EqualTo(4));
    Assert.That(decoded.Height, Is.EqualTo(4));
    Assert.That(decoded.Samples.Length, Is.EqualTo(16));
  }

  // --- Canon CR2 slice reassembly tests ---

  [Test]
  [Category("Unit")]
  public void ReassembleCanonSlices_SingleComponent_OutputSizeCorrect() {
    var width = 8;
    var height = 4;
    var samples = new ushort[width * height];
    for (var i = 0; i < samples.Length; ++i)
      samples[i] = (ushort)i;

    var sliceInfo = new[] { 1, 1, 4 }; // 1 slice of width 4, 1 slice of width 4
    var result = LosslessJpegDecoder.ReassembleCanonSlices(samples, width, height, sliceInfo, 1);

    Assert.That(result.Length, Is.EqualTo(width * height));
  }

  [Test]
  [Category("Unit")]
  public void ReassembleCanonSlices_SmallSliceInfo_FallsBack() {
    var samples = new ushort[16];
    for (var i = 0; i < samples.Length; ++i)
      samples[i] = (ushort)(i * 100);

    // Slice info with fewer than 3 elements falls back to simple de-interleave
    var result = LosslessJpegDecoder.ReassembleCanonSlices(samples, 4, 4, [2], 1);

    Assert.That(result.Length, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void ReassembleCanonSlices_MultiComponent_DeinterleaveCorrect() {
    // 2 components, 4 pixels each
    var samples = new ushort[] { 10, 20, 30, 40, 50, 60, 70, 80 };
    var result = LosslessJpegDecoder.ReassembleCanonSlices(samples, 2, 2, [1], 2);

    // De-interleaved: takes every other sample (component 0)
    Assert.That(result.Length, Is.EqualTo(4));
    Assert.That(result[0], Is.EqualTo(10));
    Assert.That(result[1], Is.EqualTo(30));
    Assert.That(result[2], Is.EqualTo(50));
    Assert.That(result[3], Is.EqualTo(70));
  }

  // --- Round-trip with known values ---

  [Test]
  [Category("Integration")]
  public void Decode_ConstantImage_AllSamplesEqual() {
    // Build a lossless JPEG where all pixels are the same value (all diffs = 0)
    var jpeg = _BuildMinimalLosslessJpeg(8, 6, 8, 1, 1, allZeroDifferences: true);
    var decoded = LosslessJpegDecoder.Decode(jpeg);

    // With predictor 1 and all-zero differences, every pixel after the first
    // should inherit from left (and first column from above)
    // The very first pixel = 128 (halfRange for 8-bit)
    // With all diffs=0, the entire image should be 128
    for (var i = 0; i < decoded.Samples.Length; ++i)
      Assert.That(decoded.Samples[i], Is.EqualTo(128), $"Sample at index {i}");
  }

  [Test]
  [Category("Integration")]
  public void Decode_12Bit_AllSamplesEqualHalfRange() {
    var jpeg = _BuildMinimalLosslessJpeg(4, 4, 12, 1, 1, allZeroDifferences: true);
    var decoded = LosslessJpegDecoder.Decode(jpeg);

    for (var i = 0; i < decoded.Samples.Length; ++i)
      Assert.That(decoded.Samples[i], Is.EqualTo(2048), $"Sample at index {i}");
  }

  // --- Helper: build minimal lossless JPEG bitstream ---

  /// <summary>Build a minimal valid lossless JPEG bitstream for testing.</summary>
  private static byte[] _BuildMinimalLosslessJpeg(int width, int height, int precision, int predictor, int numComponents, bool allZeroDifferences) {
    using var ms = new MemoryStream();

    // SOI
    ms.WriteByte(0xFF);
    ms.WriteByte(0xD8);

    // DHT: define Huffman table(s) for each component
    // Simple table: code 0 (length 1) = category 0 (zero difference)
    for (var t = 0; t < numComponents; ++t) {
      ms.WriteByte(0xFF);
      ms.WriteByte(0xC4);
      var dhtLength = 2 + 1 + 16 + 1; // length field + table info + 16 counts + 1 value
      ms.WriteByte((byte)(dhtLength >> 8));
      ms.WriteByte((byte)(dhtLength & 0xFF));
      ms.WriteByte((byte)t); // class=0 (DC), table ID = t
      ms.WriteByte(1); // 1 code of length 1
      for (var i = 1; i < 16; ++i)
        ms.WriteByte(0); // no codes of other lengths
      ms.WriteByte(0); // value = category 0 (zero difference)
    }

    // SOF3: lossless frame header
    {
      ms.WriteByte(0xFF);
      ms.WriteByte(0xC3);
      var sof3Length = 2 + 1 + 2 + 2 + 1 + numComponents * 3;
      ms.WriteByte((byte)(sof3Length >> 8));
      ms.WriteByte((byte)(sof3Length & 0xFF));
      ms.WriteByte((byte)precision);
      ms.WriteByte((byte)(height >> 8));
      ms.WriteByte((byte)(height & 0xFF));
      ms.WriteByte((byte)(width >> 8));
      ms.WriteByte((byte)(width & 0xFF));
      ms.WriteByte((byte)numComponents);
      for (var c = 0; c < numComponents; ++c) {
        ms.WriteByte((byte)(c + 1)); // component ID
        ms.WriteByte(0x11); // 1x1 sampling
        ms.WriteByte(0); // quantization table (unused in lossless)
      }
    }

    // SOS: start of scan
    {
      ms.WriteByte(0xFF);
      ms.WriteByte(0xDA);
      var sosLength = 2 + 1 + numComponents * 2 + 3;
      ms.WriteByte((byte)(sosLength >> 8));
      ms.WriteByte((byte)(sosLength & 0xFF));
      ms.WriteByte((byte)numComponents);
      for (var c = 0; c < numComponents; ++c) {
        ms.WriteByte((byte)(c + 1)); // component selector
        ms.WriteByte((byte)(c << 4)); // DC table selector = component index
      }

      ms.WriteByte((byte)predictor); // Ss = predictor
      ms.WriteByte(0); // Se (unused)
      ms.WriteByte(0); // Ah=0, Al=0
    }

    // Entropy-coded segment
    if (allZeroDifferences) {
      // Category 0 = zero difference. With our Huffman table, code for category 0 is '0' (1 bit).
      // Each pixel needs 1 bit (the Huffman code for category 0).
      var totalSamples = width * height * numComponents;
      var totalBits = totalSamples; // 1 bit per sample
      var totalBytes = (totalBits + 7) / 8;
      // All zeros = all category-0 codes = all 0 bits
      for (var i = 0; i < totalBytes; ++i)
        ms.WriteByte(0x00);
    }

    // EOI
    ms.WriteByte(0xFF);
    ms.WriteByte(0xD9);

    return ms.ToArray();
  }

  /// <summary>Build a minimal lossless JPEG with a DRI (restart interval) marker.</summary>
  private static byte[] _BuildMinimalLosslessJpegWithDri(int width, int height, int precision, int predictor, int numComponents, int restartInterval) {
    using var ms = new MemoryStream();

    // SOI
    ms.WriteByte(0xFF);
    ms.WriteByte(0xD8);

    // DRI: define restart interval
    ms.WriteByte(0xFF);
    ms.WriteByte(0xDD);
    ms.WriteByte(0x00);
    ms.WriteByte(0x04); // length = 4
    ms.WriteByte((byte)(restartInterval >> 8));
    ms.WriteByte((byte)(restartInterval & 0xFF));

    // DHT
    for (var t = 0; t < numComponents; ++t) {
      ms.WriteByte(0xFF);
      ms.WriteByte(0xC4);
      var dhtLength = 2 + 1 + 16 + 1;
      ms.WriteByte((byte)(dhtLength >> 8));
      ms.WriteByte((byte)(dhtLength & 0xFF));
      ms.WriteByte((byte)t);
      ms.WriteByte(1);
      for (var i = 1; i < 16; ++i)
        ms.WriteByte(0);
      ms.WriteByte(0);
    }

    // SOF3
    {
      ms.WriteByte(0xFF);
      ms.WriteByte(0xC3);
      var sof3Length = 2 + 1 + 2 + 2 + 1 + numComponents * 3;
      ms.WriteByte((byte)(sof3Length >> 8));
      ms.WriteByte((byte)(sof3Length & 0xFF));
      ms.WriteByte((byte)precision);
      ms.WriteByte((byte)(height >> 8));
      ms.WriteByte((byte)(height & 0xFF));
      ms.WriteByte((byte)(width >> 8));
      ms.WriteByte((byte)(width & 0xFF));
      ms.WriteByte((byte)numComponents);
      for (var c = 0; c < numComponents; ++c) {
        ms.WriteByte((byte)(c + 1));
        ms.WriteByte(0x11);
        ms.WriteByte(0);
      }
    }

    // SOS
    {
      ms.WriteByte(0xFF);
      ms.WriteByte(0xDA);
      var sosLength = 2 + 1 + numComponents * 2 + 3;
      ms.WriteByte((byte)(sosLength >> 8));
      ms.WriteByte((byte)(sosLength & 0xFF));
      ms.WriteByte((byte)numComponents);
      for (var c = 0; c < numComponents; ++c) {
        ms.WriteByte((byte)(c + 1));
        ms.WriteByte((byte)(c << 4));
      }

      ms.WriteByte((byte)predictor);
      ms.WriteByte(0);
      ms.WriteByte(0);
    }

    // Entropy data with restart markers
    var totalSamples = width * height * numComponents;
    var samplesPerInterval = restartInterval * numComponents;
    var sampleIdx = 0;
    var rstIdx = 0;

    while (sampleIdx < totalSamples) {
      var intervalSamples = Math.Min(samplesPerInterval, totalSamples - sampleIdx);
      var intervalBits = intervalSamples;
      var intervalBytes = (intervalBits + 7) / 8;

      for (var i = 0; i < intervalBytes; ++i)
        ms.WriteByte(0x00);

      sampleIdx += intervalSamples;

      // Insert restart marker if more data follows
      if (sampleIdx < totalSamples) {
        ms.WriteByte(0xFF);
        ms.WriteByte((byte)(0xD0 + (rstIdx & 7)));
        ++rstIdx;
      }
    }

    // EOI
    ms.WriteByte(0xFF);
    ms.WriteByte(0xD9);

    return ms.ToArray();
  }
}
