using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using ArcGIS.Core.CIM;
using ArcGIS.Core.Data;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Core;
using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;

namespace DomainAppointerPro
{
  internal class Module1 : Module
  {
    private static Module1 _this = null;

    public static Module1 Current => _this ?? (_this = (Module1)FrameworkApplication.FindModule("DomainAppointerPro_Module"));

    protected override bool Initialize()
    {
      DomainAppointer.DomainSettings.Load();
      return base.Initialize();
    }

    protected override bool CanUnload()
    {
      return true;
    }
  }
}
