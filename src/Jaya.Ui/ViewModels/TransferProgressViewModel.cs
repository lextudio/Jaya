//
// Copyright (c) Rubal Walia. All rights reserved.
// Licensed under the 3-Clause BSD license. See LICENSE file in the project root for full license information.
//
using Jaya.Shared;
using Jaya.Shared.Base;
using Jaya.Shared.Services;
using System;
using System.Threading;
using System.Windows.Input;

namespace Jaya.Ui.ViewModels
{
    public class TransferProgressViewModel : ViewModelBase
    {
        public event EventHandler? Finished;

        ICommand? _cancelCommand;
        CancellationTokenSource? _cancellation;

        public string Title
        {
            get => Get<string>();
            set => Set(value);
        }

        public string HeaderText
        {
            get => Get<string>();
            set => Set(value);
        }

        public string StatusText
        {
            get => Get<string>();
            set => Set(value);
        }

        public string CurrentItemPath
        {
            get => Get<string>();
            set => Set(value);
        }

        public string TargetPath
        {
            get => Get<string>();
            set => Set(value);
        }

        public int TotalItems
        {
            get => Get<int>();
            set => Set(value);
        }

        public int ProcessedItems
        {
            get => Get<int>();
            set => Set(value);
        }

        public bool IsIndeterminate
        {
            get => Get<bool>();
            set => Set(value);
        }

        public bool IsCompleted
        {
            get => Get<bool>();
            set => Set(value);
        }

        public bool IsCanceled
        {
            get => Get<bool>();
            set => Set(value);
        }

        public bool CanCancel
        {
            get => Get<bool>();
            set => Set(value);
        }

        public ICommand CancelCommand
        {
            get
            {
                if (_cancelCommand == null)
                    _cancelCommand = new RelayCommand(Cancel);
                return _cancelCommand;
            }
        }

        public void AttachCancellation(CancellationTokenSource cancellation)
        {
            _cancellation = cancellation;
            CanCancel = true;
        }

        public void Update(TransferProgressReport report)
        {
            if (report == null)
                return;

            Title = report.Mode == TransferMode.Move ? "Moving items" : "Copying items";
            TargetPath = report.TargetPath ?? TargetPath;
            TotalItems = report.TotalItems;
            ProcessedItems = report.ProcessedItems;
            IsIndeterminate = report.TotalItems <= 0;

            switch (report.Stage)
            {
                case TransferProgressStage.Started:
                    HeaderText = BuildHeader(report);
                    StatusText = "Preparing items...";
                    break;
                case TransferProgressStage.ItemStarted:
                    HeaderText = BuildHeader(report);
                    if (!string.IsNullOrWhiteSpace(report.CurrentItemPath))
                        CurrentItemPath = report.CurrentItemPath;
                    StatusText = report.Message ?? "Transferring...";
                    break;
                case TransferProgressStage.ItemCompleted:
                    HeaderText = BuildHeader(report);
                    if (!string.IsNullOrWhiteSpace(report.Message))
                        StatusText = report.Message;
                    break;
                case TransferProgressStage.Completed:
                    HeaderText = BuildHeader(report);
                    StatusText = "Completed";
                    IsCompleted = true;
                    CanCancel = false;
                    Finished?.Invoke(this, EventArgs.Empty);
                    break;
                case TransferProgressStage.Canceled:
                    HeaderText = BuildHeader(report);
                    StatusText = "Canceled";
                    IsCanceled = true;
                    CanCancel = false;
                    Finished?.Invoke(this, EventArgs.Empty);
                    break;
                case TransferProgressStage.Failed:
                    HeaderText = BuildHeader(report);
                    StatusText = report.Message ?? "Failed";
                    CanCancel = false;
                    break;
            }
        }

        string BuildHeader(TransferProgressReport report)
        {
            var verb = report.Mode == TransferMode.Move ? "Moving" : "Copying";
            if (report.TotalItems <= 0)
                return $"{verb} items...";

            return $"{verb} {report.ProcessedItems} of {report.TotalItems} items";
        }

        void Cancel()
        {
            if (_cancellation == null || _cancellation.IsCancellationRequested)
                return;

            try
            {
                _cancellation.Cancel();
                StatusText = "Canceling...";
                CanCancel = false;
            }
            catch
            {
            }
        }
    }
}
