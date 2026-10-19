using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using ArcGIS.Core.Data;
using ArcGIS.Desktop.Editing;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using MessageBox = ArcGIS.Desktop.Framework.Dialogs.MessageBox;

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
        MessageBox.Show(
          "Domain Appointer is not configured.\nOpen Domain Appointer, choose a layer + field, map the slots, and click Save Configuration.",
          "Domain Appointer",
          MessageBoxButton.OK,
          MessageBoxImage.Information);
        return;
      }

      string code = settings.GetCodeForSlot(slot);
      // Empty string for a non-Null slot means "not mapped" — do nothing.
      // Null with a mapped code writes that code (e.g. N/A on a non-nullable field).
      // Null with no mapping clears the attribute (database null).
      bool isNullSlot = string.Equals(slot, "Null", StringComparison.OrdinalIgnoreCase);
      if (!isNullSlot && string.IsNullOrEmpty(code))
      {
        MessageBox.Show(
          $"Slot {slot} is not mapped to any domain code.\nConfigure it in Domain Appointer and save.",
          "Domain Appointer",
          MessageBoxButton.OK,
          MessageBoxImage.Information);
        return;
      }

      ApplyOutcome outcome;
      try
      {
        outcome = await QueuedTask.Run(() => ApplyOnMct(settings, code));
      }
      catch (Exception ex)
      {
        MessageBox.Show(
          $"Domain Appointer failed:\n{ex.Message}",
          "Domain Appointer",
          MessageBoxButton.OK,
          MessageBoxImage.Error);
        return;
      }

      if (!string.IsNullOrEmpty(outcome.Error))
      {
        MessageBox.Show(outcome.Error, "Domain Appointer", MessageBoxButton.OK, MessageBoxImage.Error);
      }
      else if (!string.IsNullOrEmpty(outcome.Warning))
      {
        MessageBox.Show(outcome.Warning, "Domain Appointer", MessageBoxButton.OK, MessageBoxImage.Warning);
      }
      else if (!string.IsNullOrEmpty(outcome.Info))
      {
        MessageBox.Show(outcome.Info, "Domain Appointer", MessageBoxButton.OK, MessageBoxImage.Information);
      }
    }

    private static ApplyOutcome ApplyOnMct(DomainSettings settings, string code, bool isNullSlot)
    {
      var outcome = new ApplyOutcome();

      var map = MapView.Active?.Map;
      if (map == null)
      {
        outcome.Warning = "No active map.";
        return outcome;
      }

      FeatureLayer fl = LayerResolver.Find(map, settings.LayerUri, settings.LayerName);
      if (fl == null)
      {
        outcome.Warning = $"Configured layer \"{settings.LayerName}\" was not found in the active map.";
        return outcome;
      }

      var selection = fl.GetSelection();
      if (selection == null || selection.GetCount() == 0)
      {
        outcome.Info = "No features selected on the configured layer.";
        return outcome;
      }

      object valueToWrite = null;
      Field field = null;
      try
      {
        using (var table = fl.GetTable())
        {
          if (table == null)
          {
            outcome.Warning = "The configured layer has no table.";
            return outcome;
          }

          var def = table.GetDefinition();
          field = def.GetFields().FirstOrDefault(f =>
            string.Equals(f.Name, settings.FieldName, StringComparison.OrdinalIgnoreCase));
          if (field == null)
          {
            outcome.Warning = $"Field \"{settings.FieldName}\" not found on layer \"{settings.LayerName}\".";
            return outcome;
          }

          if (string.IsNullOrEmpty(code))
            valueToWrite = null;
          else
            valueToWrite = ConvertToFieldType(code, field.FieldType);
        }
      }
      catch (Exception ex)
      {
        outcome.Error = $"Failed to resolve field/value:\n{ex.Message}";
        return outcome;
      }

      var oidSet = selection.GetObjectIDs();
      if (oidSet == null || oidSet.Count == 0)
      {
        outcome.Info = "No features selected on the configured layer.";
        return outcome;
      }

      string fieldName = field?.Name ?? settings.FieldName;

      // Skip features that already have this value. Pro rejects an EditOperation
      // with "No edits producing database changes were found" when nothing changes.
      var oidsToEdit = OidsNeedingChange(fl, fieldName, oidSet, valueToWrite);
      if (oidsToEdit.Count == 0)
        return outcome;

      string opName = string.IsNullOrEmpty(code)
        ? $"Domain Appointer: clear {fieldName}"
        : $"Domain Appointer: set {fieldName} = {code}";

      var editOp = new EditOperation
      {
        Name = opName,
        SelectModifiedFeatures = false,
        SelectNewFeatures = false
      };

      var inspector = new ArcGIS.Desktop.Editing.Attributes.Inspector();
      inspector.Load(fl, oidsToEdit);

      if (!inspector.Any())
      {
        outcome.Warning = "Could not load the selected features for editing.";
        return outcome;
      }

      inspector[fieldName] = valueToWrite;
      editOp.Modify(inspector);

      bool success = editOp.Execute();
      if (!success)
      {
        string err = editOp.ErrorMessage ?? "";
        if (err.IndexOf("No edits producing database changes", StringComparison.OrdinalIgnoreCase) >= 0)
          return outcome;

        if (!string.IsNullOrEmpty(err))
          outcome.Error = $"Edit failed:\n{err}";
      }

      return outcome;
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
          return code;
      }

      return code;
    }

    private sealed class ApplyOutcome
    {
      public string Info { get; set; }
      public string Warning { get; set; }
      public string Error { get; set; }
    }
  }
}
