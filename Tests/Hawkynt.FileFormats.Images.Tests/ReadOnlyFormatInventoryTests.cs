using System.Linq;

namespace Hawkynt.FileFormats.Images.Tests;

[TestFixture]
public sealed class ReadOnlyFormatInventoryTests {
  [Test]
  [Category("Unit")]
  public void Inventory_ReadOnlyFormats() {
    var readOnly = FormatRegistry.AllFormats
      .Where(entry => entry.SupportsRead && !entry.SupportsWrite)
      .Select(entry => entry.Format.ToString())
      .OrderBy(name => name)
      .ToArray();

    Assert.Fail($"READ_ONLY_FORMATS: {string.Join(", ", readOnly)}");
  }
}
