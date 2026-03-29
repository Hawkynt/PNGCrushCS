using System;
using System.Linq;
using FileFormat.Core;

namespace FileFormat.Core.Tests;

[TestFixture]
public sealed class HeaderFieldMapperTests {

  private readonly record struct SimpleHeader(
    [property: HeaderField(0, 4)] int Magic,
    [property: HeaderField(4, 2)] short Width,
    [property: HeaderField(6, 2)] short Height
  );

  private readonly record struct HeaderWithNameOverride(
    [property: HeaderField(0, 4)] int Magic,
    [property: HeaderField(4, 16, Name = "Dim[8]")] byte Dimensions
  );

  [HeaderFiller("Padding", 6, 2)]
  private readonly record struct HeaderWithFiller(
    [property: HeaderField(0, 4)] int Magic,
    [property: HeaderField(4, 2)] short Version
  );

  [HeaderFiller("Reserved1", 8, 4)]
  [HeaderFiller("Reserved2", 12, 4)]
  private readonly record struct HeaderWithMultipleFillers(
    [property: HeaderField(0, 4)] int Magic,
    [property: HeaderField(4, 4)] int Flags
  );

  private readonly record struct EmptyHeader();

  private readonly record struct MixedHeader(
    [property: HeaderField(10, 2)] short Field3,
    [property: HeaderField(0, 4)] int Field1,
    [property: HeaderField(4, 2)] short Field2
  );

  private readonly record struct HeaderNoAttributes(
    int Width,
    int Height
  );

  [Test]
  public void GetFieldMap_SimpleHeader_ReturnsCorrectDescriptors() {
    var map = HeaderFieldMapper.GetFieldMap<SimpleHeader>();

    Assert.That(map, Has.Length.EqualTo(3));
    Assert.That(map[0].Name, Is.EqualTo("Magic"));
    Assert.That(map[0].Offset, Is.EqualTo(0));
    Assert.That(map[0].Size, Is.EqualTo(4));
    Assert.That(map[1].Name, Is.EqualTo("Width"));
    Assert.That(map[1].Offset, Is.EqualTo(4));
    Assert.That(map[1].Size, Is.EqualTo(2));
    Assert.That(map[2].Name, Is.EqualTo("Height"));
    Assert.That(map[2].Offset, Is.EqualTo(6));
    Assert.That(map[2].Size, Is.EqualTo(2));
  }

  [Test]
  public void GetFieldMap_NameOverride_UsesCustomName() {
    var map = HeaderFieldMapper.GetFieldMap<HeaderWithNameOverride>();

    Assert.That(map[1].Name, Is.EqualTo("Dim[8]"));
  }

  [Test]
  public void GetFieldMap_WithFiller_IncludesFillerEntry() {
    var map = HeaderFieldMapper.GetFieldMap<HeaderWithFiller>();

    Assert.That(map, Has.Length.EqualTo(3));
    var padding = map.Single(d => d.Name == "Padding");
    Assert.That(padding.Offset, Is.EqualTo(6));
    Assert.That(padding.Size, Is.EqualTo(2));
  }

  [Test]
  public void GetFieldMap_MultipleFillers_IncludesAll() {
    var map = HeaderFieldMapper.GetFieldMap<HeaderWithMultipleFillers>();

    Assert.That(map, Has.Length.EqualTo(4));
    Assert.That(map.Select(d => d.Name), Does.Contain("Reserved1"));
    Assert.That(map.Select(d => d.Name), Does.Contain("Reserved2"));
  }

  [Test]
  public void GetFieldMap_SortedByOffset_RegardlessOfDeclarationOrder() {
    var map = HeaderFieldMapper.GetFieldMap<MixedHeader>();

    Assert.That(map, Has.Length.EqualTo(3));
    Assert.That(map[0].Offset, Is.EqualTo(0));
    Assert.That(map[1].Offset, Is.EqualTo(4));
    Assert.That(map[2].Offset, Is.EqualTo(10));
  }

  [Test]
  public void GetFieldMap_EmptyHeader_ReturnsEmptyArray() {
    var map = HeaderFieldMapper.GetFieldMap<EmptyHeader>();

    Assert.That(map, Is.Empty);
  }

  [Test]
  public void GetFieldMap_NoAttributes_ReturnsEmptyArray() {
    var map = HeaderFieldMapper.GetFieldMap<HeaderNoAttributes>();

    Assert.That(map, Is.Empty);
  }

  [Test]
  public void GetFieldMap_CachesResult_ReturnsSameInstance() {
    var map1 = HeaderFieldMapper.GetFieldMap<SimpleHeader>();
    var map2 = HeaderFieldMapper.GetFieldMap<SimpleHeader>();

    Assert.That(map2, Is.SameAs(map1));
  }

  [Test]
  public void GetFieldMap_FillersSortedWithProperties() {
    var map = HeaderFieldMapper.GetFieldMap<HeaderWithFiller>();

    for (var i = 1; i < map.Length; ++i)
      Assert.That(map[i].Offset, Is.GreaterThanOrEqualTo(map[i - 1].Offset));
  }

  [Test]
  public void HeaderFieldDescriptor_RecordEquality_WorksCorrectly() {
    var a = new HeaderFieldDescriptor("Magic", 0, 4);
    var b = new HeaderFieldDescriptor("Magic", 0, 4);

    Assert.That(b, Is.EqualTo(a));
  }

  [Test]
  public void HeaderFieldAttribute_ExposesOffsetAndSize() {
    var attr = new HeaderFieldAttribute(10, 4);

    Assert.That(attr.Offset, Is.EqualTo(10));
    Assert.That(attr.Size, Is.EqualTo(4));
    Assert.That(attr.Name, Is.Null);
  }

  [Test]
  public void HeaderFieldAttribute_NameInitProperty_SetsCustomName() {
    var attr = new HeaderFieldAttribute(0, 2) { Name = "Custom" };

    Assert.That(attr.Name, Is.EqualTo("Custom"));
  }

  [Test]
  public void HeaderFillerAttribute_ExposesFields() {
    var attr = new HeaderFillerAttribute("Padding", 12, 8);

    Assert.That(attr.Name, Is.EqualTo("Padding"));
    Assert.That(attr.Offset, Is.EqualTo(12));
    Assert.That(attr.Size, Is.EqualTo(8));
  }
}
