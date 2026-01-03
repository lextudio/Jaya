//
// Copyright (c) Rubal Walia. All rights reserved.
// Licensed under the 3-Clause BSD license. See LICENSE file in the project root for full license information.
//
using Avalonia.Controls;
using Jaya.Shared.Base;
using System.Text.Json.Serialization;

namespace Jaya.Ui.Models
{
    public class PaneConfigModel : ConfigModelBase
    {
        public bool IsPreviewOrDetailsPaneVisible => IsPreviewPaneVisible || IsDetailsPaneVisible;

        public GridLength NavigationPaneWidth
        {
            get => Get<GridLength>();
            private set => Set(value);
        }

        public GridLength PreviewOrDetailsPanePaneWidth
        {
            get => Get<GridLength>();
            private set => Set(value);
        }

        [JsonPropertyName("navigationPaneWidthPx")]
        public double NavigationPaneWidthPx
        {
            get => Get<double>();
            set
            {
                Set(value);
                SetPaneWidths();
            }
        }

        [JsonPropertyName("previewOrDetailsPanePaneWidthPx")]
        public double PreviewOrDetailsPanePaneWidthPx
        {
            get => Get<double>();
            set
            {
                Set(value);
                SetPaneWidths();
            }
        }

        [JsonPropertyName("isNavigationPaneVisible")]
        public bool IsNavigationPaneVisible
        {
            get => Get<bool>();
            set
            {
                Set(value);
                SetPaneWidths();
            }
        }

        [JsonPropertyName("isPreviewPaneVisible")]
        public bool IsPreviewPaneVisible
        {
            get => Get<bool>();
            set
            {
                if (value && IsDetailsPaneVisible)
                    IsDetailsPaneVisible = !value;

                Set(value);
                SetPaneWidths();
            }
        }

        [JsonPropertyName("isDetailsPaneVisible")]
        public bool IsDetailsPaneVisible
        {
            get => Get<bool>();
            set
            {
                if (value && IsPreviewPaneVisible)
                    IsPreviewPaneVisible = !value;

                Set(value);
                SetPaneWidths();
            }
        }

        [JsonPropertyName("isDetailsView")]
        public bool IsDetailsView
        {
            get => Get<bool>();
            set
            {
                if (value && IsThumbnailView)
                    IsThumbnailView = !value;

                if (value && IsListView)
                    IsListView = !value;

                if (value && IsTilesView)
                    IsTilesView = !value;

                if (value && IsContentView)
                    IsContentView = !value;

                Set(value);
            }
        }

        [JsonPropertyName("isThumbnailView")]
        public bool IsThumbnailView
        {
            get => Get<bool>();
            set
            {
                if (value && IsDetailsView)
                    IsDetailsView = !value;

                if (value && IsListView)
                    IsListView = !value;

                if (value && IsTilesView)
                    IsTilesView = !value;

                if (value && IsContentView)
                    IsContentView = !value;

                Set(value);
            }
        }

        [JsonPropertyName("isListView")]
        public bool IsListView
        {
            get => Get<bool>();
            set
            {
                if (value && IsThumbnailView)
                    IsThumbnailView = !value;

                if (value && IsDetailsView)
                    IsDetailsView = !value;

                if (value && IsTilesView)
                    IsTilesView = !value;

                if (value && IsContentView)
                    IsContentView = !value;

                Set(value);
            }
        }

        [JsonPropertyName("isTilesView")]
        public bool IsTilesView
        {
            get => Get<bool>();
            set
            {
                if (value && IsThumbnailView)
                    IsThumbnailView = !value;

                if (value && IsDetailsView)
                    IsDetailsView = !value;

                if (value && IsListView)
                    IsListView = !value;

                if (value && IsContentView)
                    IsContentView = !value;

                Set(value);
            }
        }

        [JsonPropertyName("isContentView")]
        public bool IsContentView
        {
            get => Get<bool>();
            set
            {
                if (value && IsThumbnailView)
                    IsThumbnailView = !value;

                if (value && IsDetailsView)
                    IsDetailsView = !value;

                if (value && IsListView)
                    IsListView = !value;

                if (value && IsTilesView)
                    IsTilesView = !value;

                Set(value);
            }
        }

        [JsonPropertyName("isStatusBarVisible")]
        public bool IsStatusBarVisible
        {
            get => Get<bool>();
            set => Set(value);
        }

        [JsonPropertyName("isRibbonVisible")]
        public bool IsRibbonVisible
        {
            get => Get<bool>();
            set => Set(value);
        }

        [JsonPropertyName("isRibbonCollapsed")]
        public bool IsRibbonCollapsed
        {
            get => Get<bool>();
            set => Set(value);
        }

        [JsonPropertyName("isMenuHeaderVisible")]
        public bool IsMenuHeaderVisible
        {
            get => Get<bool>();
            set => Set(value);
        }

        protected override ConfigModelBase Empty()
        {
            return new PaneConfigModel
            {
                NavigationPaneWidthPx = 220,
                PreviewOrDetailsPanePaneWidthPx = 240,
                IsNavigationPaneVisible = true,
                IsDetailsPaneVisible = false,
                IsPreviewPaneVisible = false,
                IsDetailsView = false,
                IsThumbnailView = true,
                IsStatusBarVisible = true,
                IsRibbonVisible = true,
                IsRibbonCollapsed = false,
                IsMenuHeaderVisible = false
            };
        }

        void SetPaneWidths()
        {
            RaisePropertyChanged(nameof(IsPreviewOrDetailsPaneVisible));

            if (IsPreviewOrDetailsPaneVisible)
                PreviewOrDetailsPanePaneWidth = new GridLength(PreviewOrDetailsPanePaneWidthPx, GridUnitType.Pixel);
            else
                PreviewOrDetailsPanePaneWidth = new GridLength(0, GridUnitType.Auto);

            if (IsNavigationPaneVisible)
                NavigationPaneWidth = new GridLength(NavigationPaneWidthPx, GridUnitType.Pixel);
            else
                NavigationPaneWidth = new GridLength(0, GridUnitType.Auto);
        }
    }
}
