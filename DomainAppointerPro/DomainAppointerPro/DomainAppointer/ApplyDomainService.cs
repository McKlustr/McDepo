using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ArcGIS.Core.Data;
using ArcGIS.Desktop.Editing;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;

namespace DomainAppointerPro.DomainAppointer
{
  /// <summary>
  /// Applies the domain code mapped to a slot (00-19 or Null) onto the current selection
  /// of the configured feature layer / field.
  /// </summary>
  internal static class ApplyDomainService
  {
    public static async Task ApplyAsync(string slot)
    {
      if (string.IsNullOrEmpty(slot))
        return;

      var settings = DomainSettings.Current;
      if (!settings.IsConfigured)
      {
        System.Windows.MessageBox.Show(
          "Domain Appointer is not configured.\nOpen Domain Appointer, choose a layer + field, map the slots, and click Save Configuration.",
          "Domain Appointer",
          System.Windows.MessageBoxButton.OK,
          System.Windows.MessageBoxImage.Information);
        return;
      }

      string code = settings.GetCodeForSlot(slot);
      // Empty string for a non-Null slot means "not mapped"  do nothing.
      // For Null slot, empty/null means clear the value.
      bool isNullSlot = string.Equals(slot, "Null", StringComparison.OrdinalIgnoreCase);
      if (!isNullSlot && string.IsNullOrEmpty(code))
      {
        System.Windows.MessageBox.Show(
          $"Slot {slot} is not mapped to any domain code.\nConfigure it in Domain Appointer and save.",
          "Domain Appointer",
          System.Windows.MessageBoxButton.OK,
          System.Windows.MessageBoxImage.Information);
        return;
      }

      await QueuedTask.Run(() =>
      {
        var map = MapView.Active?.Map;
        if (map == null)
          return;

        FeatureLayer fl = FindLayer(map, settings);
        if (fl == null)
        {
          System.Windows.Application.Current.Dispatcher.Invoke(() =>
            System.Windows.MessageBox.Show(
              $"Configured layer \"{settings.LayerName}\" was not found in the active map.",
              "Domain Appointer",
              System.Windows.MessageBoxButton.OK,
              System.Windows.MessageBoxImage.Warning));
          return;
        }

        var selection = fl.GetSelection();
        if (selection == null || selection.GetCount() == 0)
        {
          System.Windows.Application.Current.Dispatcher.Invoke(() =>
            System.Windows.MessageBox.Show(
              "No features selected on the configured layer.",
              "Domain Appointer",
              System.Windows.MessageBoxButton.OK,
              System.Windows.MessageBoxImage.Information));
          return;
        }

        // Resolve the field and decide the typed value to write
        object valueToWrite = null;
        Field field = null;
        try
        {
          using (var table = fl.GetTable())
          {
            if (table == null) return;
            var def = table.GetDefinition();
            field = def.GetFields().FirstOrDefault(f =>
              string.Equals(f.Name, settings.FieldName, StringComparison.OrdinalIgnoreCase));
            if (field == null)
            {
              System.Windows.Application.Current.Dispatcher.Invoke(() =>
                System.Windows.MessageBox.Show(
                  $"Field \"{settings.FieldName}\" not found on layer \"{settings.LayerName}\".",
                  "Domain Appointer",
                  System.Windows.MessageBoxButton.OK,
                  System.Windows.MessageBoxImage.Warning));
              return;
            }

            if (isNullSlot || string.IsNullOrEmpty(code))
            {
              valueToWrite = null; // clears the attribute
            }
            else
            {
              valueToWrite = ConvertToFieldType(code, field.FieldType);
            }
          }
        }
        catch (Exception ex)
        {
          System.Windows.Application.Current.Dispatcher.Invoke(() =>
            System.Windows.MessageBox.Show(
              $"Failed to resolve field/value:\n{ex.Message}",
              "Domain Appointer",
              System.Windows.MessageBoxButton.OK,
              System.Windows.MessageBoxImage.Error));
          return;
        }

        var oidSet = selection.GetObjectIDs();
        if (oidSet == null || oidSet.Count == 0)
          return;

        // Skip features that already have this value. Pro rejects an EditOperation
        // with "No edits producing database changes were found" when nothing changes.
        var oidsToEdit = OidsNeedingChange(fl, settings.FieldName, oidSet, valueToWrite);
        if (oidsToEdit.Count == 0)
          return;

        string opName = isNullSlot
          ? $"Domain Appointer: clear {settings.FieldName}"
          : $"Domain Appointer: set {settings.FieldName} = {code}";

        var editOp = new EditOperation
        {
          Name = opName,
          SelectModifiedFeatures = false,
          SelectNewFeatures = false
        };

        var inspector = new ArcGIS.Desktop.Editing.Attributes.Inspector();
        inspector.Load(fl, oidsToEdit);

        if (!inspector.Any())
          return;

        inspector[settings.FieldName] = valueToWrite;
        editOp.Modify(inspector);

        bool success = editOp.Execute();
        if (!success)
        {
          string err = editOp.ErrorMessage ?? "";
          if (err.IndexOf("No edits producing database changes", StringComparison.OrdinalIgnoreCase) >= 0)
            return;

          if (!string.IsNullOrEmpty(err))
          {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
              System.Windows.MessageBox.Show(
                $"Edit failed:\n{err}",
                "Domain Appointer",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error));
          }
        }
      });
    }

    private static List<long> OidsNeedingChange(FeatureLayer fl, string fieldName, IReadOnlyList<long> oidSet, object valueToWrite)
    {
      var needed = new List<long>();
      try
      {
        using (var table = fl.GetTable())
        {
          if (table == null) return needed;
          var filter = new QueryFilter { ObjectIDs = oidSet };
          using (var cursor = table.Search(filter, true))
          {
            while (cursor.MoveNext())
            {
              using (var row = cursor.Current)
              {
                if (!ValuesEqual(row[fieldName], valueToWrite))
                  needed.Add(row.GetObjectID());
              }
            }
          }
        }
      }
      catch
      {
        return oidSet.ToList();
      }
      return needed;
    }

    private static bool ValuesEqual(object current, object target)
    {
      if (current == null || current is DBNull)
        return target == null || target is DBNull;
      if (target == null || target is DBNull)
        return false;

      if (current.Equals(target))
        return true;

      return string.Equals(
        Convert.ToString(current),
        Convert.ToString(target),
        StringComparison.Ordinal);
    }

    private static FeatureLayer FindLayer(Map map, DomainSettings settings)
    {
      var layers = map.GetLayersAsFlattenedList().OfType<FeatureLayer>().ToList();

      // Prefer URI match when available
      if (!string.IsNullOrEmpty(settings.LayerUri))
      {
        var byUri = layers.FirstOrDefault(l =>
          string.Equals(l.URI, settings.LayerUri, StringComparison.OrdinalIgnoreCase));
        if (byUri != null)
          return byUri;
      }

      // Fall back to name
      return layers.FirstOrDefault(l =>
        string.Equals(l.Name, settings.LayerName, StringComparison.OrdinalIgnoreCase));
    }

    private static object ConvertToFieldType(string code, FieldType fieldType)
    {
      if (string.IsNullOrEmpty(code))
        return null;

      switch (fieldType)
      {
        case FieldType.String:
        case FieldType.GUID:
        case FieldType.GlobalID:
          return code;

        case FieldType.Integer:
        case FieldType.SmallInteger:
          if (int.TryParse(code, out int i))
            return i;
          break;

        case FieldType.BigInteger:
          if (long.TryParse(code, out long l))
            return l;
          break;

        case FieldType.Single:
          if (float.TryParse(code, out float f))
            return f;
          break;

        case FieldType.Double:
          if (double.TryParse(code, out double d))
            return d;
          break;

        case FieldType.Date:
        case FieldType.DateOnly:
        case FieldType.TimeOnly:
        case FieldType.TimestampOffset:
          if (DateTime.TryParse(code, out DateTime dt))
            return dt;
          break;

        default:
          // Blob, Raster, Geometry, XML, etc.  leave as string and let the geodatabase reject if invalid
          return code;
      }

      // Fallback: keep original string so the edit still attempts (domain validation may still pass)
      return code;
    }
  }
}
