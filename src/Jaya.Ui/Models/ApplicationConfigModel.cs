//
// Copyright (c) Rubal Walia. All rights reserved.
// Licensed under the 3-Clause BSD license. See LICENSE file in the project root for full license information.
//
using Jaya.Shared;
using Jaya.Shared.Base;
using Jaya.Shared.Models;
using System.Text.Json.Serialization;
using System;
using System.Collections.Generic;

namespace Jaya.Ui.Models
{
    public class ApplicationConfigModel : ConfigModelBase
    {

        [JsonPropertyName("isItemCheckBoxVisible")]
        public bool IsItemCheckBoxVisible
        {
            get => Get<bool>();
            set => Set(value);
        }

        [JsonPropertyName("isFileNameExtensionVisible")]
        public bool IsFileNameExtensionVisible
        {
            get => Get<bool>();
            set => Set(value);
        }

        [JsonPropertyName("isHiddenItemVisible")]
        public bool IsHiddenItemVisible
        {
            get => Get<bool>();
            set => Set(value);
        }

        [JsonPropertyName("detailsViewSortSettings")]
        public Dictionary<string, DirectorySortSetting> DetailsViewSortSettings
        {
            get
            {
                var value = Get<Dictionary<string, DirectorySortSetting>>();
                if (value == null)
                {
                    value = new Dictionary<string, DirectorySortSetting>();
                    Set(value, nameof(DetailsViewSortSettings), false);
                }

                return value;
            }
            set => Set(value);
        }

        [JsonPropertyName("detailsViewSortDefault")]
        public DirectorySortSetting? DetailsViewSortDefault
        {
            get => Get<DirectorySortSetting?>();
            set => Set(value);
        }

        [JsonPropertyName("widthPx")]
        public double WidthPx
        {
            get => Get<double>();
            set => Set(value);
        }

        [JsonPropertyName("heightPx")]
        public double HeightPx
        {
            get => Get<double>();
            set => Set(value);
        }


        [System.Text.Json.Serialization.JsonIgnore]
        public ThemeModel Theme
        {
            get => Get<ThemeModel>();
            set
            {
                if (Set(value))
                {
                    ThemeManager.Instance.ApplyTheme(value);
                    Set(value.Name, nameof(ThemeName), false);
                }
            }
        }

        [JsonPropertyName("preferIterm")]
        public bool PreferIterm
        {
            get => Get<bool>();
            set => Set(value);
        }

        [JsonPropertyName("themeName")]
        public string ThemeName
        {
            get => Get<string>();
            set
            {
                Set(value);
                SetTheme(value);
            }
        }

        void SetTheme(string name)
        {
            foreach (var theme in ThemeManager.Instance.Themes)
            {
                if (theme.Name.Equals(name, StringComparison.InvariantCulture))
                {
                    Theme = theme;
                    break;
                }
            }
        }

        protected override ConfigModelBase Empty()
        {
            return new ApplicationConfigModel
            {
                WidthPx = 1280,
                HeightPx = 720,
                ThemeName = "Light",
                PreferIterm = false,
                DetailsViewSortSettings = new Dictionary<string, DirectorySortSetting>(),
                DetailsViewSortDefault = null
            };
        }
    }
}
