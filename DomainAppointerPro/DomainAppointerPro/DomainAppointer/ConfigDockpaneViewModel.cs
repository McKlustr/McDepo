using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using MessageBox = ArcGIS.Desktop.Framework.Dialogs.MessageBox;

namespace DomainAppointerPro.DomainAppointer
{
  internal class ConfigDockpaneViewModel : DockPane
  {
    private const string _dockPaneID = "DomainAppointerPro_ConfigPane";

    private Task _loadSettingsTask;
    private bool _suppressLayerLoad;
    private int _loadGen;

    protected ConfigDockpaneViewModel()
    {
      // Explicitly host the WPF view so the dockpane always shows content
      this.Content = new ConfigDockpaneView { DataContext = this };

      RefreshLayersCommand = new RelayCommand(() => { _ = RunSafe(() => RefreshLayersAsync(preserveSelection: true)); });
      SaveCommand = new RelayCommand(() => { _ = RunSafe(SaveSettingsAsync); });
      ClearSlotCommand = new RelayCommand<string>(ClearSlot);
    }

    public static void Show()
    {
      DockPane pane = FrameworkApplication.DockPaneManager.Find(_dockPaneID);
      if (pane == null) return;
      pane.Activate();
    }

    #region Properties

    private ObservableCollection<LayerItem> _layerItems = new ObservableCollection<LayerItem>();
    public ObservableCollection<LayerItem> LayerItems
    {
      get => _layerItems;
      set { _layerItems = value; OnPropertyChanged(); }
    }

    private LayerItem _selectedLayer;
    public LayerItem SelectedLayer
    {
      get => _selectedLayer;
      set
      {
        _selectedLayer = value;
        OnPropertyChanged();
        if (!_suppressLayerLoad)
          _ = RunSafe(LoadFieldsForLayerAsync);
      }
    }

    private ObservableCollection<string> _fieldNames = new ObservableCollection<string>();
    public ObservableCollection<string> FieldNames
    {
      get => _fieldNames;
      set { _fieldNames = value; OnPropertyChanged(); }
    }

    private string _selectedFieldName;
    public string SelectedFieldName
    {
      get => _selectedFieldName;
      set
      {
        _selectedFieldName = value;
        OnPropertyChanged();
        _ = RunSafe(LoadDomainCodesAsync);
      }
    }

    private ObservableCollection<DomainCodeItem> _availableCodes = new ObservableCollection<DomainCodeItem>();
    public ObservableCollection<DomainCodeItem> AvailableCodes
    {
      get => _availableCodes;
      set { _availableCodes = value; OnPropertyChanged(); }
    }

    private ObservableCollection<SlotMappingItem> _slotMappings = new ObservableCollection<SlotMappingItem>();
    public ObservableCollection<SlotMappingItem> SlotMappings
    {
      get => _slotMappings;
      set { _slotMappings = value; OnPropertyChanged(); }
    }

    private string _statusMessage = "Select a feature layer that has a coded domain field.";
    public string StatusMessage
    {
      get => _statusMessage;
      set { _statusMessage = value; OnPropertyChanged(); }
    }

    public ICommand RefreshLayersCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand ClearSlotCommand { get; }

    #endregion

    protected override void OnShow(bool isVisible)
    {
      if (isVisible)
        _ = RunSafe(OnShowAsync);
    }

    protected override async Task InitializeAsync()
    {
      await base.InitializeAsync();
      await EnsureSettingsLoadedAsync();
    }

    private async Task OnShowAsync()
    {
      bool alreadyLoaded = _loadSettingsTask != null && _loadSettingsTask.IsCompleted;
      await EnsureSettingsLoadedAsync();
      if (alreadyLoaded)
        await RefreshLayersAsync(preserveSelection: true);
    }

    private Task EnsureSettingsLoadedAsync()
    {
      return _loadSettingsTask ?? (_loadSettingsTask = LoadFromSettingsAsync());
    }

    private async Task LoadFromSettingsAsync()
    {
      await RefreshLayersAsync(preserveSelection: false);

      var s = DomainSettings.Current;
      LayerItem match = null;
      if (!string.IsNullOrEmpty(s.LayerUri))
      {
        match = LayerItems.FirstOrDefault(i =>
          string.Equals(i.Uri, s.LayerUri, StringComparison.OrdinalIgnoreCase));
      }
      if (match == null && !string.IsNullOrEmpty(s.LayerName))
      {
        match = LayerItems.FirstOrDefault(i =>
          string.Equals(i.Name, s.LayerName, StringComparison.OrdinalIgnoreCase));
      }

      if (match != null)
        SelectedLayer = match;
    }

    private async Task RefreshLayersAsync(bool preserveSelection)
    {
      string keepUri = preserveSelection ? SelectedLayer?.Uri : null;
      string keepName = preserveSelection ? SelectedLayer?.Name : null;

      List<LayerItem> items = await QueuedTask.Run(() =>
        LayerResolver.ListFeatureLayers(MapView.Active?.Map));

      _suppressLayerLoad = true;
      try
      {
        LayerItems.Clear();
        foreach (var item in items)
          LayerItems.Add(item);

        LayerItem match = null;
        if (!string.IsNullOrEmpty(keepUri))
        {
          match = LayerItems.FirstOrDefault(i =>
            string.Equals(i.Uri, keepUri, StringComparison.OrdinalIgnoreCase));
        }
        if (match == null && !string.IsNullOrEmpty(keepName))
        {
          match = LayerItems.FirstOrDefault(i =>
            string.Equals(i.Name, keepName, StringComparison.OrdinalIgnoreCase));
        }

        SelectedLayer = match;
      }
      finally
      {
        _suppressLayerLoad = false;
      }

      if (SelectedLayer == null)
      {
        FieldNames.Clear();
        AvailableCodes.Clear();
        SlotMappings.Clear();
      }

      StatusMessage = LayerItems.Count == 0
        ? "No feature layers found in the active map."
        : $"{LayerItems.Count} feature layer(s) available.";
    }

    private async Task LoadFieldsForLayerAsync()
    {
      int gen = Interlocked.Increment(ref _loadGen);
      FieldNames.Clear();
      AvailableCodes.Clear();

      var layer = SelectedLayer;
      if (layer == null)
        return;

      var fieldsWithDomain = new List<string>();
      await QueuedTask.Run(() =>
      {
        var map = MapView.Active?.Map;
        if (map == null) return;

        var fl = LayerResolver.Find(map, layer.Uri, layer.Name);
        if (fl == null) return;

        try
        {
          using (var table = fl.GetTable())
          {
            if (table == null) return;
            fieldsWithDomain.AddRange(DomainLookup.GetCodedDomainFieldNames(table));
          }
        }
        catch { }
      });

      if (gen != _loadGen)
        return;

      FieldNames.Clear();
      foreach (var f in fieldsWithDomain.OrderBy(x => x))
        FieldNames.Add(f);

      var prev = DomainSettings.Current.FieldName;
      string matchField = FieldNames.FirstOrDefault(f =>
        string.Equals(f, prev, StringComparison.OrdinalIgnoreCase));
      if (matchField != null)
        SelectedFieldName = matchField;
      else if (FieldNames.Count > 0)
        SelectedFieldName = FieldNames[0];
      else
        SelectedFieldName = null;

      StatusMessage = fieldsWithDomain.Count == 0
        ? "Selected layer has no coded-value domain fields."
        : $"{fieldsWithDomain.Count} domain field(s) found.";
    }

    private async Task LoadDomainCodesAsync()
    {
      int gen = Interlocked.Increment(ref _loadGen);
      AvailableCodes.Clear();

      var layer = SelectedLayer;
      if (layer == null || string.IsNullOrEmpty(SelectedFieldName))
      {
        BuildSlotMappings();
        return;
      }

      var codes = new List<DomainCodeItem>();
      string missingLayer = null;
      await QueuedTask.Run(() =>
      {
        try
        {
          var map = MapView.Active?.Map;
          var fl = LayerResolver.Find(map, layer.Uri, layer.Name);
          if (fl == null)
          {
            missingLayer = layer.Name;
            return;
          }

          using (var table = fl.GetTable())
          {
            codes.AddRange(DomainLookup.GetCodedValues(table, SelectedFieldName));
          }
        }
        catch { }
      });

      if (gen != _loadGen)
        return;

      AvailableCodes.Clear();
      AvailableCodes.Add(DomainCodeItem.Unmapped);
      foreach (var c in codes)
        AvailableCodes.Add(c);

      BuildSlotMappings();

      if (!string.IsNullOrEmpty(missingLayer))
      {
        StatusMessage = $"Configured layer \"{missingLayer}\" was not found in the active map.";
      }
      else
      {
        StatusMessage = codes.Count == 0
          ? "No coded values found for this field (including subtype domains)."
          : $"Domain has {codes.Count} coded value(s) (including subtype domains). Map them to slots below.";
      }
    }

    private void BuildSlotMappings()
    {
      SlotMappings.Clear();
      bool restore = ShouldRestoreSavedMappings();
      var s = DomainSettings.Current;

      SlotMappings.Add(new SlotMappingItem
      {
        Slot = "Null",
        SelectedCode = SlotCodeForUi(restore, s, "Null"),
        AvailableCodes = AvailableCodes
      });

      for (int i = 0; i <= 19; i++)
      {
        string key = i.ToString("00");
        SlotMappings.Add(new SlotMappingItem
        {
          Slot = key,
          SelectedCode = SlotCodeForUi(restore, s, key),
          AvailableCodes = AvailableCodes
        });
      }
    }

    private bool ShouldRestoreSavedMappings()
    {
      var s = DomainSettings.Current;
      var layer = SelectedLayer;
      if (layer == null || string.IsNullOrEmpty(SelectedFieldName) || string.IsNullOrEmpty(s.FieldName))
        return false;

      if (!string.Equals(SelectedFieldName, s.FieldName, StringComparison.OrdinalIgnoreCase))
        return false;

      if (!string.IsNullOrEmpty(s.LayerUri) && !string.IsNullOrEmpty(layer.Uri))
        return string.Equals(s.LayerUri, layer.Uri, StringComparison.OrdinalIgnoreCase);

      return string.Equals(s.LayerName, layer.Name, StringComparison.OrdinalIgnoreCase);
    }

    private static string SlotCodeForUi(bool restore, DomainSettings settings, string slot)
    {
      if (!restore)
        return DomainCodeItem.UnmappedCode;

      string saved = settings.GetCodeForSlot(slot);
      return string.IsNullOrEmpty(saved) ? DomainCodeItem.UnmappedCode : saved;
    }

    private async Task SaveSettingsAsync()
    {
      if (SelectedLayer == null || string.IsNullOrEmpty(SelectedFieldName))
      {
        StatusMessage = "Select a layer and field before saving.";
        return;
      }

      var s = DomainSettings.Current;
      s.LayerName = SelectedLayer.Name ?? "";
      s.LayerUri = SelectedLayer.Uri ?? "";
      s.FieldName = SelectedFieldName;

      await QueuedTask.Run(() =>
      {
        var fl = LayerResolver.Find(MapView.Active?.Map, s.LayerUri, s.LayerName);
        if (fl != null)
        {
          s.LayerUri = fl.URI ?? s.LayerUri;
          s.LayerName = fl.Name ?? s.LayerName;
        }
      });

      s.SlotToCode.Clear();
      s.SlotToDescription.Clear();

      foreach (var item in SlotMappings)
      {
        string code = DomainCodeItem.Normalize(item.SelectedCode);
        s.SlotToCode[item.Slot] = code;
        var match = AvailableCodes.FirstOrDefault(c =>
          !DomainCodeItem.IsUnmapped(c.Code) && c.Code == code);
        s.SlotToDescription[item.Slot] = match?.Description ?? code;
      }

      if (!DomainSettings.Save(out string error))
      {
        StatusMessage = $"Save failed: {error}";
        MessageBox.Show(
          $"Could not save Domain Appointer settings:\n{error}",
          "Domain Appointer",
          MessageBoxButton.OK,
          MessageBoxImage.Error);
        return;
      }

      StatusMessage = "Settings saved. You can now use the Domain 00-19 and Null buttons (or assign keyboard shortcuts).";
    }

    private void ClearSlot(string slot)
    {
      var item = SlotMappings.FirstOrDefault(x => x.Slot == slot);
      if (item != null)
        item.SelectedCode = DomainCodeItem.UnmappedCode;
    }

    private async Task RunSafe(Func<Task> work)
    {
      try
      {
        await work();
      }
      catch (Exception ex)
      {
        StatusMessage = ex.Message;
      }
    }

    protected void OnPropertyChanged([CallerMemberName] string name = null)
    {
      NotifyPropertyChanged(name);
    }
  }

  public class DomainCodeItem
  {
    // Non-empty sentinel so WPF ComboBox SelectedValue can bind (empty string does not).
    public const string UnmappedCode = "__unmapped__";

    public static DomainCodeItem Unmapped { get; } = new DomainCodeItem
    {
      Code = UnmappedCode,
      Description = "(unmapped)"
    };

    public static bool IsUnmapped(string code) =>
      string.IsNullOrEmpty(code) ||
      string.Equals(code, UnmappedCode, StringComparison.Ordinal);

    public static string Normalize(string code) =>
      IsUnmapped(code) ? "" : code;

    public string Code { get; set; }
    public string Description { get; set; }
    public string Display
    {
      get
      {
        if (IsUnmapped(Code))
          return "(unmapped)";
        if (string.IsNullOrEmpty(Description))
          return Code;
        return $"{Code}  –  {Description}";
      }
    }
    public override string ToString() => Display;
  }

  public class SlotMappingItem : INotifyPropertyChanged
  {
    public string Slot { get; set; }

    private string _selectedCode;
    public string SelectedCode
    {
      get => _selectedCode;
      set { _selectedCode = value; OnPropertyChanged(); }
    }

    public ObservableCollection<DomainCodeItem> AvailableCodes { get; set; }

    public event PropertyChangedEventHandler PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string name = null)
    {
      PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
  }

  public class RelayCommand : ICommand
  {
    private readonly Action _execute;
    private readonly Func<bool> _canExecute;

    public RelayCommand(Action execute, Func<bool> canExecute = null)
    {
      _execute = execute ?? throw new ArgumentNullException(nameof(execute));
      _canExecute = canExecute;
    }

    public event EventHandler CanExecuteChanged
    {
      add { CommandManager.RequerySuggested += value; }
      remove { CommandManager.RequerySuggested -= value; }
    }

    public bool CanExecute(object parameter) => _canExecute == null || _canExecute();
    public void Execute(object parameter) => _execute();
  }

  public class RelayCommand<T> : ICommand
  {
    private readonly Action<T> _execute;
    private readonly Func<T, bool> _canExecute;

    public RelayCommand(Action<T> execute, Func<T, bool> canExecute = null)
    {
      _execute = execute ?? throw new ArgumentNullException(nameof(execute));
      _canExecute = canExecute;
    }

    public event EventHandler CanExecuteChanged
    {
      add { CommandManager.RequerySuggested += value; }
      remove { CommandManager.RequerySuggested -= value; }
    }

    public bool CanExecute(object parameter)
    {
      if (_canExecute == null)
        return true;
      if (parameter is T typed)
        return _canExecute(typed);
      if (parameter == null)
        return _canExecute(default);
      return false;
    }

    public void Execute(object parameter)
    {
      if (parameter is T typed)
        _execute(typed);
      else if (parameter == null)
        _execute(default);
    }
  }
}
