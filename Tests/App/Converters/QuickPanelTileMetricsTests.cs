using System.Globalization;
using System.Windows;
using SwiftList.App.Converters;

namespace SwiftList.App.Tests.Converters;

// How wide a tile is, and how big the picture in it is, for a list of a given width.
[TestClass]
public sealed class QuickPanelTileMetricsTests
{
    private static double Slot(double listWidth) => (double)new QuickPanelTileMetrics().Convert(
        listWidth, typeof(double), null!, CultureInfo.InvariantCulture);

    private static double Icon(double listWidth) => (double)new QuickPanelTileMetrics().Convert(
        listWidth, typeof(double), "Icon", CultureInfo.InvariantCulture);

    private static int Columns(double listWidth) => (int)(listWidth / Slot(listWidth));

    // Every width, at every plausible panel size: the tiles divide the row rather than being handed out
    // in fixed lumps, so what is left over is never enough for another one. An empty strip at the end of
    // a row is the one thing that reads as a mistake, and is worth tiles a few pixels smaller.
    [TestMethod]
    public void NoWidthEverLeavesRoomForAnotherTileAtTheEndOfTheRow()
    {
        for (var width = 200.0; width < 3000; width += 7.3)
        {
            var leftover = width - Columns(width) * Slot(width);
            Assert.IsLessThan(Slot(width), leftover, $"a {width:F0}-wide list wastes {leftover:F0} of it");
        }
    }

    // The whole point: a wide panel spends the space on bigger thumbnails, not just more of them.
    [TestMethod]
    public void AnOrdinaryPanel_DividesItsWidthByFive()
    {
        Assert.AreEqual(160, Slot(800));
        Assert.AreEqual(5, Columns(800));
    }

    // Past where a picture can use the width, the row takes another tile instead of padding five.
    [TestMethod]
    public void AVeryWidePanel_TakesMoreThanFive()
    {
        Assert.IsGreaterThan(QuickPanelTileMetrics.Columns, Columns(2000));
        Assert.IsLessThanOrEqualTo(QuickPanelTileMetrics.MaxSlot, Slot(2000));
    }

    // And below where a tile is worth looking at, fewer than five -- still dividing the width, so the
    // row is full at four rather than five unreadable ones with a gap after them.
    [TestMethod]
    public void ANarrowPanel_TakesFewerThanFive()
    {
        Assert.AreEqual(4, Columns(380));
        Assert.IsGreaterThanOrEqualTo(92, Slot(380));
    }

    [TestMethod]
    public void TheIconLeavesRoomForTheNameUnderIt()
        => Assert.IsLessThan(Slot(800), Icon(800), "a picture filling the slot would push the name out of the tile");

    [TestMethod]
    public void AListThatHasNotBeenMeasuredYet_SetsNothing()
    {
        Assert.AreEqual(DependencyProperty.UnsetValue, new QuickPanelTileMetrics().Convert(
            double.NaN, typeof(double), null!, CultureInfo.InvariantCulture));
        Assert.AreEqual(DependencyProperty.UnsetValue, new QuickPanelTileMetrics().Convert(
            0.0, typeof(double), null!, CultureInfo.InvariantCulture));
    }
}
