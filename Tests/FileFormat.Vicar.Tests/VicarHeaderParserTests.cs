using System.Collections.Generic;
using FileFormat.Vicar;

namespace FileFormat.Vicar.Tests;

[TestFixture]
public sealed class VicarHeaderParserTests {

  [Test]
  [Category("Unit")]
  public void Parse_SimpleKeyValue() {
    var labels = VicarHeaderParser.Parse("FORMAT=BYTE");

    Assert.That(labels.ContainsKey("FORMAT"), Is.True);
    Assert.That(labels["FORMAT"], Is.EqualTo("BYTE"));
  }

  [Test]
  [Category("Unit")]
  public void Parse_QuotedValue() {
    var labels = VicarHeaderParser.Parse("TASK='SOME TASK NAME'");

    Assert.That(labels.ContainsKey("TASK"), Is.True);
    Assert.That(labels["TASK"], Is.EqualTo("SOME TASK NAME"));
  }

  [Test]
  [Category("Unit")]
  public void Parse_MultipleLabels() {
    var labels = VicarHeaderParser.Parse("LBLSIZE=128 FORMAT=BYTE NL=100 NS=200");

    Assert.That(labels, Has.Count.EqualTo(4));
    Assert.That(labels["LBLSIZE"], Is.EqualTo("128"));
    Assert.That(labels["FORMAT"], Is.EqualTo("BYTE"));
    Assert.That(labels["NL"], Is.EqualTo("100"));
    Assert.That(labels["NS"], Is.EqualTo("200"));
  }

  [Test]
  [Category("Unit")]
  public void Parse_MixedQuotedAndUnquoted() {
    var labels = VicarHeaderParser.Parse("FORMAT=BYTE TASK='MY TASK' NL=50");

    Assert.That(labels, Has.Count.EqualTo(3));
    Assert.That(labels["FORMAT"], Is.EqualTo("BYTE"));
    Assert.That(labels["TASK"], Is.EqualTo("MY TASK"));
    Assert.That(labels["NL"], Is.EqualTo("50"));
  }

  [Test]
  [Category("Unit")]
  public void Format_PadsToLblSize() {
    var labels = new Dictionary<string, string> {
      ["LBLSIZE"] = "64",
      ["FORMAT"] = "BYTE"
    };

    var result = VicarHeaderParser.Format(labels, 64);

    Assert.That(result.Length, Is.EqualTo(64));
    Assert.That(result, Does.StartWith("LBLSIZE=64"));
  }

  [Test]
  [Category("Unit")]
  public void Format_QuotesValuesWithSpaces() {
    var labels = new Dictionary<string, string> {
      ["TASK"] = "MY TASK"
    };

    var result = VicarHeaderParser.Format(labels, 64);

    Assert.That(result, Does.Contain("TASK='MY TASK'"));
  }
}
