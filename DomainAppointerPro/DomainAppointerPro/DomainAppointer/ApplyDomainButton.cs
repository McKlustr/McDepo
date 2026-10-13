using System;
using System.Threading.Tasks;
using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Mapping;

namespace DomainAppointerPro.DomainAppointer
{
  /// <summary>
  /// Shared button class for Domain_00 ... Domain_19 and Domain_Null.
  /// Slot is determined from the button ID in Config.daml.
  /// </summary>
  internal class ApplyDomainButton : Button
  {
    protected override void OnUpdate()
    {
      // Enable only when a map is active, settings exist, and the slot is mapped
      // (Null is always considered "mapped" so the clear action stays available once configured).
      bool enabled = false;
      try
      {
        if (MapView.Active != null && DomainSettings.Current.IsConfigured)
        {
          string slot = ExtractSlotFromId(this.ID);
          if (!string.IsNullOrEmpty(slot))
          {
            bool isNull = string.Equals(slot, "Null", StringComparison.OrdinalIgnoreCase);
            string code = DomainSettings.Current.GetCodeForSlot(slot);
            enabled = isNull || !string.IsNullOrEmpty(code);
          }
        }
      }
      catch
      {
        enabled = false;
      }

      this.Enabled = enabled;
    }

    protected override async void OnClick()
    {
      string slot = ExtractSlotFromId(this.ID);
      if (string.IsNullOrEmpty(slot))
        return;

      await ApplyDomainService.ApplyAsync(slot);
    }

    private static string ExtractSlotFromId(string buttonId)
    {
      if (string.IsNullOrEmpty(buttonId)) return null;

      // IDs look like: DomainAppointerPro_Domain_00  or  DomainAppointerPro_Domain_Null
      const string prefix = "DomainAppointerPro_Domain_";
      int idx = buttonId.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
      if (idx >= 0)
      {
        return buttonId.Substring(idx + prefix.Length);
      }

      // fallback: last token after underscore
      int lastUnderscore = buttonId.LastIndexOf('_');
      if (lastUnderscore >= 0 && lastUnderscore < buttonId.Length - 1)
        return buttonId.Substring(lastUnderscore + 1);

      return null;
    }
  }
}
