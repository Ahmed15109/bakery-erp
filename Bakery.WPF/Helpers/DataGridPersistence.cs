using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Bakery.Application.Interfaces;

namespace Bakery.WPF.Helpers
{
    public static class DataGridPersistence
    {
        public static readonly DependencyProperty PersistenceIdProperty =
            DependencyProperty.RegisterAttached("PersistenceId", typeof(string), typeof(DataGridPersistence), new PropertyMetadata(null, OnPersistenceIdChanged));

        public static string GetPersistenceId(DependencyObject obj) => (string)obj.GetValue(PersistenceIdProperty);
        public static void SetPersistenceId(DependencyObject obj, string value) => obj.SetValue(PersistenceIdProperty, value);

        private static string _settingsPath = ApplicationPathDefaults.GridSettingsFile;

        private static Dictionary<string, GridSettings> _allSettings = new();
        private static DispatcherTimer _saveTimer;

        static DataGridPersistence()
        {
            LoadAllSettings();
            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _saveTimer.Tick += (s, e) => { _saveTimer.Stop(); SaveAllSettings(); };
        }

        public static void Configure(IApplicationPathService applicationPaths)
        {
            ArgumentNullException.ThrowIfNull(applicationPaths);
            _settingsPath = applicationPaths.GridSettingsFile;
            LoadAllSettings();
        }

        private static void OnPersistenceIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not DataGrid grid || e.NewValue is not string id) return;

            grid.Loaded += (s, ev) => RestoreSettings(grid, id);
            
            // Track changes
            grid.ColumnReordered += (s, ev) => RequestSave();
            
            // Width changes are trickier. We can hook into the LayoutUpdated of the grid
            // or listen to the property changes of columns if we want to be very precise.
            // For ERP, listening to ColumnReordered and periodic LayoutUpdated is usually enough.
            grid.LayoutUpdated += (s, ev) => 
            {
                if (grid.IsLoaded) UpdateSettings(grid, id);
            };
        }

        private static void UpdateSettings(DataGrid grid, string id)
        {
            var columns = grid.Columns.Select(c => new ColumnSettings
            {
                Header = c.Header?.ToString() ?? "",
                DisplayIndex = c.DisplayIndex,
                Width = c.Width.IsAbsolute ? c.Width.Value : -1,
                Visibility = c.Visibility
            }).ToList();

            var current = new GridSettings { Columns = columns };
            
            if (!_allSettings.ContainsKey(id) || !SettingsEqual(_allSettings[id], current))
            {
                _allSettings[id] = current;
                RequestSave();
            }
        }

        private static bool SettingsEqual(GridSettings a, GridSettings b)
        {
            if (a.Columns.Count != b.Columns.Count) return false;
            for (int i = 0; i < a.Columns.Count; i++)
            {
                if (a.Columns[i].Header != b.Columns[i].Header ||
                    a.Columns[i].DisplayIndex != b.Columns[i].DisplayIndex ||
                    Math.Abs(a.Columns[i].Width - b.Columns[i].Width) > 0.1 ||
                    a.Columns[i].Visibility != b.Columns[i].Visibility)
                    return false;
            }
            return true;
        }

        private static void RestoreSettings(DataGrid grid, string id)
        {
            if (!_allSettings.TryGetValue(id, out var settings)) return;

            foreach (var colSettings in settings.Columns)
            {
                var column = grid.Columns.FirstOrDefault(c => c.Header?.ToString() == colSettings.Header);
                if (column != null)
                {
                    if (colSettings.Width > 0)
                        column.Width = new DataGridLength(colSettings.Width);
                    
                    column.DisplayIndex = Math.Min(colSettings.DisplayIndex, grid.Columns.Count - 1);
                    column.Visibility = colSettings.Visibility;
                }
            }
        }

        private static void RequestSave()
        {
            if (!_saveTimer.IsEnabled) _saveTimer.Start();
        }

        private static void LoadAllSettings()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    var json = File.ReadAllText(_settingsPath);
                    _allSettings = JsonSerializer.Deserialize<Dictionary<string, GridSettings>>(json) ?? new();
                }
            }
            catch { _allSettings = new(); }
        }

        private static void SaveAllSettings()
        {
            try
            {
                var dir = Path.GetDirectoryName(_settingsPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir!);
                
                var json = JsonSerializer.Serialize(_allSettings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settingsPath, json);
            }
            catch { /* Ignore errors in persistence */ }
        }
    }

    public class GridSettings
    {
        public List<ColumnSettings> Columns { get; set; } = new();
    }

    public class ColumnSettings
    {
        public string Header { get; set; } = "";
        public int DisplayIndex { get; set; }
        public double Width { get; set; }
        public Visibility Visibility { get; set; }
    }
}
