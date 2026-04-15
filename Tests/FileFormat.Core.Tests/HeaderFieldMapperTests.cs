using System;
using System.Linq;
using FileFormat.Core;

namespace FileFormat.Core.Tests;

[TestFixture]
public sealed class HeaderFieldMapperTests {

  private readonly record struct SimpleHeader(
    [property: Field(0, 4)] int Magic,
    [property: Field(4, 2)] short Width,
    [property: Field(6, 2)] short Height
  );

  private readonly record struct HeaderWithNameOverride(
    [property: Field(0, 4)] int Magic,
    [property: Field(4, 16, Name = "Dim[8]")] byte Dimensions
  );

  [Filler(6, 2)]
  private readonly record struct HeaderWithFiller(
    [property: Field(0, 4)] int Magic,
    [property: Field(4, 2)] short Version
  );

  [Filler(8, 4)]
  [Filler(12, 4)]
  private readonly record struct HeaderWithMultipleFillers(
    [property: Field(0, 4)] int Magic,
    [property: Field(4, 4)] int Flags
  );

  private readonly record struct EmptyHeader();

  private readonly record struct MixedHeader(
    [property: Field(10, 2)] short Field3,
    [property: Field(0, 4)] int Field1,
    [property: Field(4, 2)] short Field2
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
    var padding = map.Single(d => d.Name == "(padding)");
    Assert.That(padding.Offset, Is.EqualTo(6));
    Assert.That(padding.Size, Is.EqualTo(2));
  }

  [Test]
  public void GetFieldMap_MultipleFillers_IncludesAll() {
    var map = HeaderFieldMapper.GetFieldMap<HeaderWithMultipleFillers>();

    Assert.That(map, Has.Length.EqualTo(4));
    Assert.That(map.Count(d => d.Name == "(padding)"), Is.EqualTo(2));
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
  public void FieldAttribute_ExposesOffsetAndSize() {
    var attr = new FieldAttribute(10, 4);

    Assert.That(attr.Offset, Is.EqualTo(10));
    Assert.That(attr.Size, Is.EqualTo(4));
    Assert.That(attr.Name, Is.Null);
  }

  [Test]
  public void FieldAttribute_NameInitProperty_SetsCustomName() {
    var attr = new FieldAttribute(0, 2) { Name = "Custom" };

    Assert.That(attr.Name, Is.EqualTo("Custom"));
  }

  [Test]
  public void FillerAttribute_ExposesFields() {
    var attr = new FillerAttribute(12, 8);

    Assert.That(attr.Offset, Is.EqualTo(12));
    Assert.That(attr.Size, Is.EqualTo(8));
  }
}
