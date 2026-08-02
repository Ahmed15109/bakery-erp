using Bakery.Shared.Helpers;
using FluentAssertions;

namespace Bakery.IntegrationTests;

public sealed class InventoryMovementNoteLocalizationTests
{
    [Theory]
    [InlineData("Produced from Production", "تم الإنتاج من أمر إنتاج")]
    [InlineData("Produced from Production PRD-1042", "تم الإنتاج من أمر إنتاج PRD-1042")]
    [InlineData("Consumed for Production PRD-1042", "تم استهلاك الصنف للإنتاج PRD-1042")]
    [InlineData("Reversal of Produced from Production PRD-1042", "حركة عكسية: تم الإنتاج من أمر إنتاج PRD-1042")]
    [InlineData("Stock count variance session #2", "تسوية جرد رقم 2")]
    [InlineData("Purchases", "مشتريات")]
    [InlineData("Sales", "مبيعات")]
    public void LocalizeInventoryMovementNote_TranslatesLegacySystemNotes(string source, string expected)
    {
        Loc.LocalizeInventoryMovementNote(source).Should().Be(expected);
    }

    [Theory]
    [InlineData("ملاحظة يدوية")]
    [InlineData("Sales target correction")]
    [InlineData("PRD-1042")]
    public void LocalizeInventoryMovementNote_PreservesUserAndReferenceValues(string note)
    {
        Loc.LocalizeInventoryMovementNote(note).Should().Be(note);
    }

    [Fact]
    public void InventoryMovementNoteFactories_PreserveDynamicReferences()
    {
        Loc.InventoryNoteProducedFromProduction("PRD-55")
            .Should().Be("تم الإنتاج من أمر إنتاج PRD-55");
        Loc.InventoryNoteConsumedForProduction("PRD-55")
            .Should().Be("تم استهلاك الصنف للإنتاج PRD-55");
        Loc.InventoryNoteStockCountVariance(27)
            .Should().Be("تسوية جرد رقم 27");
        Loc.InventoryNoteReversal("تم استهلاك الصنف للإنتاج PRD-55")
            .Should().Be("حركة عكسية: تم استهلاك الصنف للإنتاج PRD-55");
    }
}
