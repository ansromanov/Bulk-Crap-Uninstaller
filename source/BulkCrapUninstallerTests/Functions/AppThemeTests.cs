using System.Drawing;
using System.Drawing.Imaging;
using BulkCrapUninstaller.Functions.Tools;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BulkCrapUninstallerTests.Functions
{
    [TestClass]
    public class AppThemeTests
    {
        [TestMethod]
        public void GetReadableTextColor_UsesDarkTextOnLightStatusColors()
        {
            Assert.AreEqual(Color.FromArgb(35, 35, 35), AppTheme.GetReadableTextColor(Color.PaleGreen));
        }

        [TestMethod]
        public void GetReadableTextColor_UsesLightTextOnDarkStatusColors()
        {
            Assert.AreEqual(Color.FromArgb(235, 235, 235), AppTheme.GetReadableTextColor(Color.SlateBlue));
        }

        [TestMethod]
        public void DarkModeUsesDarkListSurfaceForUnhighlightedRows()
        {
            Assert.AreEqual(Color.FromArgb(45, 45, 48), AppTheme.GetDefaultListItemBackColor(true));
            Assert.AreEqual(Color.FromArgb(235, 235, 235), AppTheme.GetDefaultListItemForeColor(true));
        }

        [TestMethod]
        public void DarkModeMutesSpecialListColors()
        {
            var highlightedColor = AppTheme.GetListHighlightBackColor(Color.PaleGreen, true);

            Assert.IsTrue(highlightedColor.R < 120);
            Assert.IsTrue(highlightedColor.G < 120);
            Assert.AreEqual(Color.FromArgb(235, 235, 235), AppTheme.GetReadableTextColor(highlightedColor));
        }

        [TestMethod]
        public void LightModeKeepsOriginalSpecialListColors()
        {
            Assert.AreEqual(Color.PaleGreen, AppTheme.GetListHighlightBackColor(Color.PaleGreen, false));
        }

        [TestMethod]
        public void DarkModeLightensMonochromeToolbarIconsAndPreservesColorIcons()
        {
            using var source = new Bitmap(3, 1, PixelFormat.Format32bppArgb);
            source.SetPixel(0, 0, Color.Black);
            source.SetPixel(1, 0, Color.White);
            source.SetPixel(2, 0, Color.FromArgb(255, 20, 120, 220));

            using var result = (Bitmap)AppTheme.CreateDarkModeImage(source);

            Assert.AreEqual(Color.FromArgb(210, 215, 225), result.GetPixel(0, 0));
            Assert.AreEqual(0, result.GetPixel(1, 0).A);
            Assert.AreEqual(Color.FromArgb(255, 20, 120, 220), result.GetPixel(2, 0));
        }

        [TestMethod]
        public void LightModeRestoresSystemListColorsForUnhighlightedRows()
        {
            Assert.AreEqual(SystemColors.Window, AppTheme.GetDefaultListItemBackColor(false));
            Assert.AreEqual(SystemColors.WindowText, AppTheme.GetDefaultListItemForeColor(false));
        }
    }
}
