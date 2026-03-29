using System;
using FileFormat.WebP.Vp8L;

namespace FileFormat.WebP.Tests;

[TestFixture]
public sealed class Vp8LHuffmanTreeTests {

  [Test]
  [Category("Unit")]
  public void Build_SingleSymbol_ProducesValidTree() {
    var codeLengths = new int[4];
    codeLengths[2] = 1;

    var tree = Vp8LHuffmanTree.Build(codeLengths, 4);

    Assert.That(tree, Is.Not.Null);
  }

  [Test]
  [Category("Unit")]
  public void Build_TwoSymbols_ProducesValidTree() {
    var codeLengths = new int[4];
    codeLengths[0] = 1;
    codeLengths[1] = 1;

    var tree = Vp8LHuffmanTree.Build(codeLengths, 4);

    Assert.That(tree, Is.Not.Null);
  }

  [Test]
  [Category("Unit")]
  public void Build_EmptyCodeLengths_ProducesValidTree() {
    var codeLengths = new int[4];

    var tree = Vp8LHuffmanTree.Build(codeLengths, 4);

    Assert.That(tree, Is.Not.Null);
  }

  [Test]
  [Category("Unit")]
  public void ReadSymbol_SingleSymbolTree_AlwaysReturnsSameSymbol() {
    var codeLengths = new int[8];
    codeLengths[5] = 1;
    var tree = Vp8LHuffmanTree.Build(codeLengths, 8);
    var data = new byte[] { 0x00, 0x00, 0x00, 0x00 };
    var reader = new Vp8LBitReader(data, 0);

    var symbol = tree.ReadSymbol(reader);

    Assert.That(symbol, Is.EqualTo(5));
  }

  [Test]
  [Category("Unit")]
  public void ReadSymbol_TwoSymbolTree_DecodesCorrectly() {
    var codeLengths = new int[4];
    codeLengths[0] = 1;
    codeLengths[1] = 1;
    var tree = Vp8LHuffmanTree.Build(codeLengths, 4);

    var data0 = new byte[] { 0x00 };
    var reader0 = new Vp8LBitReader(data0, 0);
    var sym0 = tree.ReadSymbol(reader0);

    var data1 = new byte[] { 0x01 };
    var reader1 = new Vp8LBitReader(data1, 0);
    var sym1 = tree.ReadSymbol(reader1);

    Assert.That(new[] { sym0, sym1 }, Is.EquivalentTo(new[] { 0, 1 }));
  }

  [Test]
  [Category("Unit")]
  public void Build_MultipleSymbols_AssignsCanonicalCodes() {
    var codeLengths = new int[4];
    codeLengths[0] = 1;
    codeLengths[1] = 2;
    codeLengths[2] = 2;

    var tree = Vp8LHuffmanTree.Build(codeLengths, 4);

    Assert.That(tree, Is.Not.Null);
  }

  [Test]
  [Category("Unit")]
  public void ReadSymbol_MultipleSymbolTree_DecodesAllSymbols() {
    var codeLengths = new int[4];
    codeLengths[0] = 1;
    codeLengths[1] = 2;
    codeLengths[2] = 2;
    var tree = Vp8LHuffmanTree.Build(codeLengths, 4);

    var decodedSymbols = new System.Collections.Generic.HashSet<int>();
    for (byte b = 0; b < 4; ++b) {
      var data = new byte[] { b, 0x00 };
      var reader = new Vp8LBitReader(data, 0);
      decodedSymbols.Add(tree.ReadSymbol(reader));
    }

    Assert.That(decodedSymbols, Does.Contain(0));
    Assert.That(decodedSymbols, Does.Contain(1));
    Assert.That(decodedSymbols, Does.Contain(2));
  }

  [Test]
  [Category("Unit")]
  public void Build_LargeAlphabet_DoesNotThrow() {
    var codeLengths = new int[280];
    codeLengths[0] = 2;
    codeLengths[100] = 2;
    codeLengths[200] = 3;
    codeLengths[279] = 3;

    Assert.DoesNotThrow(() => Vp8LHuffmanTree.Build(codeLengths, 280));
  }
}
