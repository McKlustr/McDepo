using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;

namespace DomainAppointerPro.DomainAppointer
{
  internal class ConfigDockpaneViewModel : DockPane
  {
    private const string _dockPaneID = "DomainAppointerPro_ConfigPane";

    protected ConfigDockpaneViewModel()
    {
      // Explicitly host the WPF view so the dockpane always shows content
      this.Content = new ConfigDockpaneView { DataContext = this };

      RefreshLayersCommand = new RelayCommand(() => { _ = RefreshLayersAsync(); });
      SaveCommand = new RelayCommand(() => _ = SaveSettingsAsync());
      ClearSlotCommand = new RelayCommand<string>(ClearSlot);
    }

    public static void Show()
    {
      DockPane pane = FrameworkApplication.DockPaneManager.Find(_dockPaneID);
      if (pane == null) return;
      pane.Activate();
    }

    #region Properties

    private ObservableCollection<string> _layerNames = new ObservableCollection<string>();
    public ObservableCollection<string> LayerNames
    {
      get => _layerNames;
      set { _layerNames = value; OnPropertyChanged(); }
    }

    private string _selectedLayerName;
    public string SelectedLayerName
    {
      get => _selectedLayerName;
      set
      {
        _selectedLayerName = value;
        OnPropertyChanged();
        _ = LoadFieldsForLayerAsync();
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
        _ = LoadDomainCodesAsync();
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
        _ = LoadFromSettingsAsync();
    }

    protected override async Task InitializeAsync()
    {
      await base.InitializeAsync();
      await LoadFromSettingsAsync();
    }

    private async Task LoadFromSettingsAsync()
    {
      await RefreshLayersAsync();

      var s = DomainSettings.Current;
      if (!string.IsNullOrEmpty(s.LayerName) && LayerNames.Contains(s.LayerName))
        SelectedLayerName = s.LayerName;
    }

    private async Task RefreshLayersAsync()
    {
      await QueuedTask.Run(() =>
      {
        var names = new List<string>();
        var map = MapView.Active?.Map;
        if (map != null)
        {
          foreach (var fl in map.GetLayersAsFlattenedList().OfType<FeatureLayer>())
          {
            names.Add(fl.Name);
          }
        }

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
          LayerNames.Clear();
          foreach (var n in names.OrderBy(x => x))
            LayerNames.Add(n);
          StatusMessage = names.Count == 0
            ? "No feature layers found in the active map."
            : $"{names.Count} feature layer(s) available.";
        });
      });
    }

    private async Task LoadFieldsForLayerAsync()
    {
      FieldNames.Clear();
      AvailableCodes.Clear();
      if (string.IsNullOrEmpty(SelectedLayerName)) return;

      await QueuedTask.Run(() =>
      {
        var map = MapView.Active?.Map;
        if (map == null) return;

        var fl = map.GetLayersAsFlattenedList()
                    .OfType<FeatureLayer>()
                    .FirstOrDefault(l => string.Equals(l.Name, SelectedLayerName, StringComparison.OrdinalIgnoreCase));
        if (fl == null) return;

        var fieldsWithDomain = new List<string>();
        try
        {
          using (var table = fl.GetTable())
          {
            if (table == null) return;
            fieldsWithDomain.AddRange(DomainLookup.GetCodedDomainFieldNames(table));
          }
        }
        catch { }

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
          FieldNames.Clear();
          foreach (var f in fieldsWithDomain.OrderBy(x => x))
            FieldNames.Add(f);

          // restore previous field if still valid
          var prev = DomainSettings.Current.FieldName;
          if (!string.IsNullOrEmpty(prev) && FieldNames.Contains(prev))
            SelectedFieldName = prev;
          else if (FieldNames.Count > 0)
            SelectedFieldName = FieldNames[0];

          StatusMessage = fieldsWithDomain.Count == 0
            ? "Selected layer has no coded-value domain fields."
            : $"{fieldsWithDomain.Count} domain field(s) found.";
        });
      });
    }

    private async Task LoadDomainCodesAsync()
    {
      AvailableCodes.Clear();
      if (string.IsNullOrEmpty(SelectedLayerName) || string.IsNullOrEmpty(SelectedFieldName)) return;

      await QueuedTask.Run(() =>
      {
        var codes = new List<DomainCodeItem>();
        try
        {
          var map = MapView.Active?.Map;
          var fl = map?.GetLayersAsFlattenedList()
                       .OfType<FeatureLayer>()
                       .FirstOrDefault(l => string.Equals(l.Name, SelectedLayerName, StringComparison.OrdinalIgnoreCase));
          if (fl == null) return;

          using (var table = fl.GetTable())
          {
            codes.AddRange(DomainLookup.GetCodedValues(table, SelectedFieldName));
          }
        }
        catch { }

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
          AvailableCodes.Clear();
          AvailableCodes.Add(DomainCodeItem.Unmapped);
          foreach (var c in codes)
            AvailableCodes.Add(c);

          BuildSlotMappings();
          StatusMessage = codes.Count == 0
            ? "No coded values found for this field (including subtype domains)."
            : $"Domain has {codes.Count} coded value(s) (including subtype domains). Map them to slots below.";
        });
      });
    }

    private void BuildSlotMappings()
    {
      SlotMappings.Clear();
      var s = DomainSettings.Current;

      // Null first
      SlotMappings.Add(new SlotMappingItem
      {
        Slot = "Null",
        SelectedCode = s.GetCodeForSlot("Null") ?? "",
        AvailableCodes = AvailableCodes
      });

      for (int i = 0; i <= 19; i++)
      {
        string key = i.ToString("00");
        SlotMappings.Add(new SlotMappingItem
        {
          Slot = key,
          SelectedCode = s.GetCodeForSlot(key) ?? "",
          AvailableCodes = AvailableCodes
        });
      }
    }

    private async Task SaveSettingsAsync()
    {
      if (string.IsNullOrEmpty(SelectedLayerName) || string.IsNullOrEmpty(SelectedFieldName))
      {
        StatusMessage = "Select a layer and field before saving.";
        return;
      }

      var s = DomainSettings.Current;
      s.LayerName = SelectedLayerName;
      s.FieldName = SelectedFieldName;

      // Capture URI on MCT before writing settings
      await QueuedTask.Run(() =>
      {
        var fl = MapView.Active?.Map?.GetLayersAsFlattenedList()
                      .OfType<FeatureLayer>()
                      .FirstOrDefault(l => string.Equals(l.Name, SelectedLayerName, StringComparison.OrdinalIgnoreCase));
        if (fl != null)
          s.LayerUri = fl.URI;
        else
          s.LayerUri = "";
      });

      s.SlotToCode.Clear();
      s.SlotToDescription.Clear();

      foreach (var item in SlotMappings)
      {
        s.SlotToCode[item.Slot] = item.SelectedCode ?? "";
        var match = AvailableCodes.FirstOrDefault(c => c.Code == item.SelectedCode);
        s.SlotToDescription[item.Slot] = match?.Description ?? item.SelectedCode ?? "";
      }

      DomainSettings.Save();
      StatusMessage = "Settings saved. You can now use the Domain 00-19 and Null buttons (or assign keyboard shortcuts).";
    }

    private void ClearSlot(string slot)
    {
      var item = SlotMappings.FirstOrDefault(x => x.Slot == slot);
      if (item != null)
        item.SelectedCode = "";
    }

    protected void OnPropertyChanged([CallerMemberName] string name = null)
    {
      NotifyPropertyChanged(name);
    }
  }

  public class DomainCodeItem
  {
    public static DomainCodeItem Unmapped { get; } = new DomainCodeItem
    {
      Code = "",
      Description = "(unmapped)"
    };

    public string Code { get; set; }
    public string Description { get; set; }
    public string Display
    {
      get
      {
        if (string.IsNullOrEmpty(Code))
          return string.IsNullOrEmpty(Description) ? "(unmapped)" : Description;
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

  // Simple RelayCommand
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

    public bool CanExecute(object parameter) => _canExecute == null || _canExecute((T)parameter);
    public void Execute(object parameter) => _execute((T)parameter);
  }
}
