using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Contracts;

namespace DomainAppointerPro.DomainAppointer
{
  internal class LauncherButton : Button
  {
    protected override void OnClick()
    {
      // Open the configuration dockpane
      var pane = FrameworkApplication.DockPaneManager.Find("DomainAppointerPro_ConfigPane");
      if (pane == null) return;

      pane.Activate();
    }
  }
}
