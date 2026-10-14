using System;
using System.Collections.Generic;
using System.Linq;
using ArcGIS.Desktop.Mapping;

namespace DomainAppointerPro.DomainAppointer
{
  /// <summary>
  /// Resolves feature layers by URI first (stable), then by name.
  /// </summary>
  internal static class LayerResolver
  {
    public static FeatureLayer Find(Map map, string layerUri, string layerName)
    {
      if (map == null)
        return null;

      var layers = map.GetLayersAsFlattenedList().OfType<FeatureLayer>().ToList();

      if (!string.IsNullOrEmpty(layerUri))
      {
        var byUri = layers.FirstOrDefault(l =>
          string.Equals(l.URI, layerUri, StringComparison.OrdinalIgnoreCase));
        if (byUri != null)
          return byUri;
      }

      if (string.IsNullOrEmpty(layerName))
        return null;

      return layers.FirstOrDefault(l =>
        string.Equals(l.Name, layerName, StringComparison.OrdinalIgnoreCase));
    }

    public static List<LayerItem> ListFeatureLayers(Map map)
    {
      var items = new List<LayerItem>();
      if (map == null)
        return items;

      foreach (var fl in map.GetLayersAsFlattenedList().OfType<FeatureLayer>())
      {
        items.Add(new LayerItem
        {
          Name = fl.Name ?? "",
          Uri = fl.URI ?? ""
        });
      }

      var nameCounts = items
        .GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

      foreach (var item in items)
      {
        bool duplicate = nameCounts.TryGetValue(item.Name, out int count) && count > 1;
        item.Display = duplicate && !string.IsNullOrEmpty(item.Uri)
          ? $"{item.Name}  [{item.Uri}]"
          : item.Name;
      }

      return items
        .OrderBy(i => i.Display, StringComparer.OrdinalIgnoreCase)
        .ToList();
    }
  }

  public class LayerItem
  {
    public string Name { get; set; }
    public string Uri { get; set; }
    public string Display { get; set; }

    public override string ToString() => Display ?? Name ?? "";

    public override bool Equals(object obj)
    {
      if (obj is not LayerItem other)
        return false;
      if (!string.IsNullOrEmpty(Uri) || !string.IsNullOrEmpty(other.Uri))
        return string.Equals(Uri, other.Uri, StringComparison.OrdinalIgnoreCase);
      return string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase);
    }

    public override int GetHashCode()
    {
      if (!string.IsNullOrEmpty(Uri))
        return StringComparer.OrdinalIgnoreCase.GetHashCode(Uri);
      return Name == null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(Name);
    }
  }
}
