using System;
using System.Collections.Generic;
using System.Linq;
using ArcGIS.Core.Data;

namespace DomainAppointerPro.DomainAppointer
{
  /// <summary>
  /// Resolves coded-value domains on a table, including domains assigned per subtype.
  /// </summary>
  internal static class DomainLookup
  {
    public static List<string> GetCodedDomainFieldNames(Table table)
    {
      var names = new List<string>();
      if (table == null) return names;

      var def = table.GetDefinition();
      var subtypes = GetSubtypesSafe(def);

      foreach (var field in def.GetFields())
      {
        if (!IsAttributeField(field))
          continue;

        if (HasCodedValueDomain(field, subtypes))
          names.Add(field.Name);
      }

      return names;
    }

    public static List<DomainCodeItem> GetCodedValues(Table table, string fieldName)
    {
      var items = new Dictionary<string, DomainCodeItem>(StringComparer.Ordinal);
      if (table == null || string.IsNullOrEmpty(fieldName))
        return new List<DomainCodeItem>();

      var def = table.GetDefinition();
      var field = def.GetFields().FirstOrDefault(f =>
        string.Equals(f.Name, fieldName, StringComparison.OrdinalIgnoreCase));
      if (field == null)
        return new List<DomainCodeItem>();

      AddCodedValues(field.GetDomain() as CodedValueDomain, items);

      foreach (var subtype in GetSubtypesSafe(def))
      {
        try
        {
          AddCodedValues(field.GetDomain(subtype) as CodedValueDomain, items);
        }
        catch
        {
          // Shapefiles / tables without per-subtype domains
        }
      }

      return items.Values.OrderBy(x => x.Code, StringComparer.Ordinal).ToList();
    }

    private static bool HasCodedValueDomain(Field field, IReadOnlyList<Subtype> subtypes)
    {
      try
      {
        if (field.GetDomain() is CodedValueDomain)
          return true;
      }
      catch
      {
        // ignore and check subtypes
      }

      foreach (var subtype in subtypes)
      {
        try
        {
          if (field.GetDomain(subtype) is CodedValueDomain)
            return true;
        }
        catch
        {
          // ignore this subtype
        }
      }

      return false;
    }

    private static void AddCodedValues(CodedValueDomain domain, Dictionary<string, DomainCodeItem> into)
    {
      if (domain == null) return;

      foreach (var kvp in domain.GetCodedValuePairs())
      {
        string code = kvp.Key?.ToString() ?? "";
        if (string.IsNullOrEmpty(code))
          continue;

        if (!into.ContainsKey(code))
        {
          into[code] = new DomainCodeItem
          {
            Code = code,
            Description = kvp.Value ?? ""
          };
        }
      }
    }

    private static IReadOnlyList<Subtype> GetSubtypesSafe(TableDefinition def)
    {
      if (def == null)
        return Array.Empty<Subtype>();

      try
      {
        string subtypeField = def.GetSubtypeField();
        if (string.IsNullOrEmpty(subtypeField))
          return Array.Empty<Subtype>();

        return def.GetSubtypes() ?? (IReadOnlyList<Subtype>)Array.Empty<Subtype>();
      }
      catch (NotSupportedException)
      {
        // shapefiles and some table types do not support subtypes
        return Array.Empty<Subtype>();
      }
      catch
      {
        return Array.Empty<Subtype>();
      }
    }

    private static bool IsAttributeField(Field field)
    {
      switch (field.FieldType)
      {
        case FieldType.OID:
        case FieldType.Geometry:
        case FieldType.Blob:
        case FieldType.Raster:
        case FieldType.XML:
          return false;
        default:
          return true;
      }
    }
  }
}
