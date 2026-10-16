using System.Drawing;
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
        public void LightModeRestoresSystemListColorsForUnhighlightedRows()
        {
            Assert.AreEqual(SystemColors.Window, AppTheme.GetDefaultListItemBackColor(false));
            Assert.AreEqual(SystemColors.WindowText, AppTheme.GetDefaultListItemForeColor(false));
        }
    }
}
