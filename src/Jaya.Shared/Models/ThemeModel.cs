using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Jaya.Shared.Base;
using System;
using System.Collections.Generic;

namespace Jaya.Shared.Models
{
    public class ThemeModel : ModelBase
    {
        // Parameterless constructor required for System.Text.Json deserialization
        public ThemeModel()
        {
            Styles = new List<IStyle>();
        }
        public ThemeModel(string name, params IStyle[] themeStyles)
            : this(name, ThemeVariant.Light, themeStyles)
        {
        }

        public ThemeModel(string name, ThemeVariant variant, params IStyle[] themeStyles)
        {
            Name = name;
            Variant = variant;

            Styles = new List<IStyle>(themeStyles ?? Array.Empty<IStyle>());
        }

        public ThemeModel(string name, params Uri[] themeStyleUris)
            : this(name, ThemeVariant.Light, BuildStyles(themeStyleUris))
        {
        }

        public ThemeModel(string name, ThemeVariant variant, params Uri[] themeStyleUris)
            : this(name, variant, BuildStyles(themeStyleUris))
        {
        }

        static IStyle[] BuildStyles(Uri[] themeStyleUris)
        {
            if (themeStyleUris == null || themeStyleUris.Length == 0)
                return Array.Empty<IStyle>();

            var styles = new List<IStyle>();
            foreach (var styleUri in themeStyleUris)
            {
                if (styleUri == null)
                    continue;

                var style = new StyleInclude(styleUri) { Source = styleUri };
                styles.Add(style);
            }

            return styles.ToArray();
        }

        [System.Text.Json.Serialization.JsonInclude]
        public string Name
        {
            get => Get<string>();
            private set => Set(value);
        }

        [System.Text.Json.Serialization.JsonInclude]
        public ThemeVariant Variant
        {
            get => Get<ThemeVariant>();
            private set => Set(value);
        }

        [System.Text.Json.Serialization.JsonInclude]
        public IList<IStyle> Styles
        {
            get => Get<IList<IStyle>>();
            private set => Set(value);
        }

        public override string ToString()
        {
            return Name;
        }
    }
}
