using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using Jaya.Shared.Base;
using Jaya.Shared.Models;
using System;
using System.Collections.Generic;

namespace Jaya.Shared
{
    public sealed class ThemeManager : ModelBase
    {
        static readonly object _syncLock;
        static ThemeManager _instance;
        readonly List<ThemeModel> _themes;
        readonly List<Window> _windows;
        readonly Dictionary<Window, List<IStyle>> _windowStyles;
        ThemeModel _selectedTheme;

        static ThemeManager()
        {
            _syncLock = new object();
        }

        private ThemeManager()
        {
            _windows = new List<Window>();
            _windowStyles = new Dictionary<Window, List<IStyle>>();

            _themes = new List<ThemeModel>
            {
                new ThemeModel("Light", ThemeVariant.Light,
                    new Uri("avares://Avalonia.Themes.Fluent/FluentTheme.xaml"),
                    new Uri("avares://Jaya.Shared/Styles/Accents/BaseLight.axaml")),
                new ThemeModel("Dark", ThemeVariant.Dark,
                    new Uri("avares://Avalonia.Themes.Fluent/FluentTheme.xaml"),
                    new Uri("avares://Jaya.Shared/Styles/Accents/BaseDark.axaml"))
            };

            ApplyTheme(_themes[0]);
        }

        public static ThemeManager Instance
        {
            get
            {
                lock (_syncLock)
                {
                    if (_instance == null)
                        _instance = new ThemeManager();

                    return _instance;
                }
            }
        }

        public IEnumerable<ThemeModel> Themes => _themes;

        public ThemeModel SelectedTheme => _selectedTheme;

        public void ApplyTheme(ThemeModel value)
        {
            if (Design.IsDesignMode || value == null || value.Styles.Count == 0)
                return;

            if (_selectedTheme == value)
                return;

            var previousTheme = _selectedTheme;
            _selectedTheme = value;

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                Application.Current.RequestedThemeVariant = value.Variant;

                var currentAppStyles = new List<IStyle>();
                currentAppStyles.AddRange(Application.Current.Styles);

                var removalCount = Math.Min(2, currentAppStyles.Count);
                if (removalCount > 0)
                    currentAppStyles.RemoveRange(0, removalCount);

                currentAppStyles.InsertRange(0, SelectedTheme.Styles);

                Application.Current.Styles.Clear();
                Application.Current.Styles.AddRange(currentAppStyles);

                if (previousTheme != null)
                {
                    foreach (var window in _windows)
                    {
                        if (_windowStyles.TryGetValue(window, out var attached))
                        {
                            foreach (var style in attached)
                                window.Styles.Remove(style);
                        }

                        var newStyles = CloneStyles(SelectedTheme);
                        _windowStyles[window] = newStyles;

                        foreach (var style in newStyles)
                            window.Styles.Add(style);
                    }
                }
            }, Avalonia.Threading.DispatcherPriority.Background);
        }

        public void EnableTheme(Window window)
        {
            if (Design.IsDesignMode)
            {
                if (SelectedTheme != null && SelectedTheme.Styles.Count > 0)
                {
                    var styles = CloneStyles(SelectedTheme);
                    foreach (var style in styles)
                        window.Styles.Add(style);
                }
            }

            window.Opened += (sender, e) =>
            {
                _windows.Add(window);

                if (SelectedTheme != null && SelectedTheme.Styles.Count > 0)
                {
                    var styles = CloneStyles(SelectedTheme);
                    _windowStyles[window] = styles;

                    foreach (var style in styles)
                        window.Styles.Add(style);
                }
            };

            window.Closing += (sender, e) =>
            {
                if (_windowStyles.TryGetValue(window, out var attached))
                {
                    foreach (var style in attached)
                        window.Styles.Remove(style);

                    _windowStyles.Remove(window);
                }

                _windows.Remove(window);
            };
        }

        static List<IStyle> CloneStyles(ThemeModel theme)
        {
            var clones = new List<IStyle>();

            foreach (var style in theme.Styles)
            {
                if (style is Avalonia.Markup.Xaml.Styling.StyleInclude include)
                {
                    var clone = new Avalonia.Markup.Xaml.Styling.StyleInclude(include.Source)
                    {
                        Source = include.Source
                    };
                    clones.Add(clone);
                }
                else
                {
                    clones.Add(style);
                }
            }

            return clones;
        }
    }
}
