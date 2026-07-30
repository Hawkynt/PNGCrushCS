using System;
using FileFormat.Jbig;

namespace FileFormat.Jbig.Tests;

[TestFixture]
public sealed class ArithmeticCoderTests {

  [Test]
  [Category("Unit")]
  public void QeTable_Has113Entries() {
    Assert.That(ArithmeticCoder.QeTable, Has.Length.EqualTo(113));
  }

  [Test]
  [Category("Unit")]
  public void QeTable_FirstEntry_IsEquiprobable() {
    var first = ArithmeticCoder.QeTable[0];
    Assert.That(first.Qe, Is.EqualTo(0x5A1D));
    Assert.That(first.Nmps, Is.EqualTo(1));
    Assert.That(first.Nlps, Is.EqualTo(1));
    Assert.That(first.Switch, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void QeTable_LastEntry_HasSelfReferenceForNmps() {
    var last = ArithmeticCoder.QeTable[112];
    Assert.That(last.Nmps, Is.EqualTo(112));
  }

  [Test]
  [Category("Unit")]
  public void QeTable_AllNmpsInRange() {
    for (var i = 0; i < ArithmeticCoder.QeTable.Length; ++i) {
      var entry = ArithmeticCoder.QeTable[i];
      Assert.That(entry.Nmps, Is.GreaterThanOrEqualTo(0).And.LessThan(113),
        $"State {i}: NMPS={entry.Nmps} out of range");
    }
  }

  [Test]
  [Category("Unit")]
  public void QeTable_AllNlpsInRange() {
    for (var i = 0; i < ArithmeticCoder.QeTable.Length; ++i) {
      var entry = ArithmeticCoder.QeTable[i];
      Assert.That(entry.Nlps, Is.GreaterThanOrEqualTo(0).And.LessThan(113),
        $"State {i}: NLPS={entry.Nlps} out of range");
    }
  }

  [Test]
  [Category("Unit")]
  public void QeTable_AllQePositive() {
    for (var i = 0; i < ArithmeticCoder.QeTable.Length; ++i)
      Assert.That(ArithmeticCoder.QeTable[i].Qe, Is.GreaterThan(0),
        $"State {i}: Qe must be positive");
  }

  [Test]
  [Category("Unit")]
  public void Encoder_EncodeSingleMpsBit_ProducesOutput() {
    var encoder = new ArithmeticCoder.Encoder();
    var states = new int[1];
    var mps = new int[1];

    encoder.EncodeBit(0, 0, states, mps);

    var output = encoder.Flush();

    Assert.That(output.Length, Is.GreaterThan(0));
  }

  [Test]
  [Category("Unit")]
  public void Encoder_EncodeSingleLpsBit_ProducesOutput() {
    var encoder = new ArithmeticCoder.Encoder();
    var states = new int[1];
    var mps = new int[1];

    encoder.EncodeBit(1, 0, states, mps);

    var output = encoder.Flush();

    Assert.That(output.Length, Is.GreaterThan(0));
  }

  [Test]
  [Category("Unit")]
  public void Encoder_MpsBit_AdvancesState() {
    var encoder = new ArithmeticCoder.Encoder();
    var states = new int[1];
    var mps = new int[1];

    encoder.EncodeBit(0, 0, states, mps);

    Assert.That(states[0], Is.EqualTo(ArithmeticCoder.QeTable[0].Nmps));
  }

  [Test]
  [Category("Unit")]
  public void Encoder_LpsBit_AdvancesStateToNlps() {
    var encoder = new ArithmeticCoder.Encoder();
    var states = new int[1];
    var mps = new int[1];

    encoder.EncodeBit(1, 0, states, mps);

    Assert.That(states[0], Is.EqualTo(ArithmeticCoder.QeTable[0].Nlps));
  }

  [Test]
  [Category("Unit")]
  public void EncodeDecode_RoundTrip_SingleBit0() {
    var encoder = new ArithmeticCoder.Encoder();
    var encStates = new int[1];
    var encMps = new int[1];

    encoder.EncodeBit(0, 0, encStates, encMps);
    var encoded = encoder.Flush();

    var decoder = new ArithmeticCoder.Decoder(encoded, 0, encoded.Length);
    var decStates = new int[1];
    var decMps = new int[1];

    var result = decoder.DecodeBit(0, decStates, decMps);

    Assert.That(result, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void EncodeDecode_RoundTrip_SingleBit1() {
    var encoder = new ArithmeticCoder.Encoder();
    var encStates = new int[1];
    var encMps = new int[1];

    encoder.EncodeBit(1, 0, encStates, encMps);
    var encoded = encoder.Flush();

    var decoder = new ArithmeticCoder.Decoder(encoded, 0, encoded.Length);
    var decStates = new int[1];
    var decMps = new int[1];

    var result = decoder.DecodeBit(0, decStates, decMps);

    Assert.That(result, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void EncodeDecode_RoundTrip_MultipleBits() {
    var bits = new[] { 0, 1, 1, 0, 1, 0, 0, 1, 1, 1, 0, 0 };

    var encoder = new ArithmeticCoder.Encoder();
    var encStates = new int[1];
    var encMps = new int[1];

    foreach (var bit in bits)
      encoder.EncodeBit(bit, 0, encStates, encMps);

    var encoded = encoder.Flush();

    var decoder = new ArithmeticCoder.Decoder(encoded, 0, encoded.Length);
    var decStates = new int[1];
    var decMps = new int[1];

    var decoded = new int[bits.Length];
    for (var i = 0; i < bits.Length; ++i)
      decoded[i] = decoder.DecodeBit(0, decStates, decMps);

    Assert.That(decoded, Is.EqualTo(bits));
  }

  [Test]
  [Category("Unit")]
  public void EncodeDecode_RoundTrip_MultipleContexts() {
    var encoder = new ArithmeticCoder.Encoder();
    var encStates = new int[4];
    var encMps = new int[4];

    encoder.EncodeBit(0, 0, encStates, encMps);
    encoder.EncodeBit(1, 1, encStates, encMps);
    encoder.EncodeBit(1, 2, encStates, encMps);
    encoder.EncodeBit(0, 3, encStates, encMps);

    var encoded = encoder.Flush();

    var decoder = new ArithmeticCoder.Decoder(encoded, 0, encoded.Length);
    var decStates = new int[4];
    var decMps = new int[4];

    Assert.That(decoder.DecodeBit(0, decStates, decMps), Is.EqualTo(0));
    Assert.That(decoder.DecodeBit(1, decStates, decMps), Is.EqualTo(1));
    Assert.That(decoder.DecodeBit(2, decStates, decMps), Is.EqualTo(1));
    Assert.That(decoder.DecodeBit(3, decStates, decMps), Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void EncodeDecode_RoundTrip_ManyIdenticalBits() {
    var encoder = new ArithmeticCoder.Encoder();
    var encStates = new int[1];
    var encMps = new int[1];

    for (var i = 0; i < 100; ++i)
      encoder.EncodeBit(0, 0, encStates, encMps);

    var encoded = encoder.Flush();

    var decoder = new ArithmeticCoder.Decoder(encoded, 0, encoded.Length);
    var decStates = new int[1];
    var decMps = new int[1];

    for (var i = 0; i < 100; ++i)
      Assert.That(decoder.DecodeBit(0, decStates, decMps), Is.EqualTo(0), $"Bit {i} mismatch");
  }

  [Test]
  [Category("Unit")]
  public void EncodeDecode_RoundTrip_AllOnes() {
    var encoder = new ArithmeticCoder.Encoder();
    var encStates = new int[1];
    var encMps = new int[1];

    for (var i = 0; i < 50; ++i)
      encoder.EncodeBit(1, 0, encStates, encMps);

    var encoded = encoder.Flush();

    var decoder = new ArithmeticCoder.Decoder(encoded, 0, encoded.Length);
    var decStates = new int[1];
    var decMps = new int[1];

    for (var i = 0; i < 50; ++i)
      Assert.That(decoder.DecodeBit(0, decStates, decMps), Is.EqualTo(1), $"Bit {i} mismatch");
  }

  [Test]
  [Category("Unit")]
  public void Encoder_FlushOnEmpty_ProducesValidOutput() {
    var encoder = new ArithmeticCoder.Encoder();
    var output = encoder.Flush();

    Assert.That(output, Is.Not.Null);
  }

  [Test]
  [Category("Unit")]
  public void QeTable_SwitchStates_HaveExpectedPattern() {
    // States 0, 6, 14, 47, 73, 99 should have Switch=true
    Assert.That(ArithmeticCoder.QeTable[0].Switch, Is.True);
    Assert.That(ArithmeticCoder.QeTable[6].Switch, Is.True);
    Assert.That(ArithmeticCoder.QeTable[14].Switch, Is.True);
    Assert.That(ArithmeticCoder.QeTable[47].Switch, Is.True);
    Assert.That(ArithmeticCoder.QeTable[73].Switch, Is.True);
    Assert.That(ArithmeticCoder.QeTable[99].Switch, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void QeTable_NonSwitchStates_SwitchIsFalse() {
    Assert.That(ArithmeticCoder.QeTable[1].Switch, Is.False);
    Assert.That(ArithmeticCoder.QeTable[46].Switch, Is.False);
    Assert.That(ArithmeticCoder.QeTable[112].Switch, Is.False);
  }
}
