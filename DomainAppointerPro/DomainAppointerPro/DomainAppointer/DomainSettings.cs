using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DomainAppointerPro.DomainAppointer
{
  /// <summary>
  /// Persistent settings: target layer URI/name, field name, and the 00-19 + Null mappings.
  /// </summary>
  public class DomainSettings
  {
    public string LayerName { get; set; } = "";
    public string LayerUri { get; set; } = "";
    public string FieldName { get; set; } = "";
    public string DomainName { get; set; } = "";

    // Slot key is "00".."19" or "Null". Value is the domain code (object) as string, or empty if unmapped.
    public Dictionary<string, string> SlotToCode { get; set; } = new Dictionary<string, string>();

    // Optional friendly description for each slot (for tooltips)
    public Dictionary<string, string> SlotToDescription { get; set; } = new Dictionary<string, string>();

    private static readonly string SettingsFolder =
      Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DomainAppointerPro");

    private static readonly string SettingsPath = Path.Combine(SettingsFolder, "settings.json");

    public static DomainSettings Current { get; private set; } = new DomainSettings();

    public static void Load()
    {
      try
      {
        if (File.Exists(SettingsPath))
        {
          var json = File.ReadAllText(SettingsPath);
          Current = JsonSerializer.Deserialize<DomainSettings>(json) ?? new DomainSettings();
        }
        else
        {
          Current = new DomainSettings();
        }
      }
      catch
      {
        Current = new DomainSettings();
      }

      EnsureSlots();
    }

    /// <summary>
    /// Guarantees keys 00-19 and Null exist so UI and buttons never hit missing entries.
    /// </summary>
    private static void EnsureSlots()
    {
      if (Current.SlotToCode == null)
        Current.SlotToCode = new Dictionary<string, string>();
      if (Current.SlotToDescription == null)
        Current.SlotToDescription = new Dictionary<string, string>();

      for (int i = 0; i <= 19; i++)
      {
        string key = i.ToString("00");
        if (!Current.SlotToCode.ContainsKey(key))
          Current.SlotToCode[key] = "";
        if (!Current.SlotToDescription.ContainsKey(key))
          Current.SlotToDescription[key] = "";
      }
      if (!Current.SlotToCode.ContainsKey("Null"))
        Current.SlotToCode["Null"] = "";
      if (!Current.SlotToDescription.ContainsKey("Null"))
        Current.SlotToDescription["Null"] = "Null / Clear";
    }

    public static void Save()
    {
      try
      {
        if (!Directory.Exists(SettingsFolder))
          Directory.CreateDirectory(SettingsFolder);

        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(Current, options);
        File.WriteAllText(SettingsPath, json);
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"DomainSettings.Save failed: {ex.Message}");
      }
    }

    public string GetCodeForSlot(string slot)
    {
      if (SlotToCode != null && SlotToCode.TryGetValue(slot, out string code))
        return code;
      return null;
    }

    public string GetDescriptionForSlot(string slot)
    {
      if (SlotToDescription != null && SlotToDescription.TryGetValue(slot, out string desc))
        return desc;
      return slot;
    }

    public bool IsConfigured =>
      !string.IsNullOrWhiteSpace(LayerName) &&
      !string.IsNullOrWhiteSpace(FieldName);
  }
}
