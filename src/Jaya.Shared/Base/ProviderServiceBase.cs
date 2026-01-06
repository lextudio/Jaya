//
// Copyright (c) Rubal Walia. All rights reserved.
// Licensed under the 3-Clause BSD license. See LICENSE file in the project root for full license information.
//
using Jaya.Shared.Models;
using Serilog;
using Jaya.Shared.Services;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Jaya.Shared.Base
{
    public abstract class ProviderServiceBase : ModelBase, IProviderService
    {
        static readonly ILogger Logger = Log.ForContext(typeof(ProviderServiceBase)).ForContext("SourceContext", "Provider");

        IMemoryCacheService? _cache;
        IConfigurationService? _config;
        IPlatformService? _platform;

        public delegate void OnAccountAdded(AccountModelBase account);
        public event OnAccountAdded? AccountAdded;

        public delegate void OnAccountRemoved(AccountModelBase account);
        public event OnAccountRemoved? AccountRemoved;

        protected ProviderServiceBase()
        {
            // Default to disabled for security; enable filesystem provider by default.
            // Use ModelBase.Set directly to avoid triggering the IsEnabled setter (which persists)
            // before the derived class has an opportunity to set `Name`.
            Set<bool>(false, nameof(IsEnabled), raiseNotification: false);

            try
            {
                Logger.Information("Loading provider config for {ProviderType}", this.GetType().Name);

                // Compute the configuration key in the same way SetConfiguration/GetConfiguration would.
                var key = !string.IsNullOrEmpty(Name) ? Name : this.GetType().Name;

                // Use ConfigurationService.Get<T> (not GetOrDefault) so we can detect an absent persisted section (null).
                var cfg = ConfigurationService.Get<Jaya.Shared.Models.ProviderConfigModel>(key);
                if (cfg != null)
                {
                    // Use Set to avoid persistence during construction
                    Set<bool>(cfg.IsEnabled, nameof(IsEnabled), raiseNotification: false);
                    Logger.Debug("Loaded provider config: {ProviderType} IsEnabled={IsEnabled}", this.GetType().Name, cfg.IsEnabled);
                }
                else
                {
                    // No persisted config - enable FileSystem provider by default.
                    if (this.GetType().Name.Contains("FileSystem", StringComparison.OrdinalIgnoreCase))
                    {
                        Set<bool>(true, nameof(IsEnabled), raiseNotification: false);
                        Logger.Debug("No persisted config for {ProviderType}; enabling FileSystem provider by default", this.GetType().Name);
                    }
                    else
                    {
                        Logger.Debug("No persisted config for {ProviderType}; leaving disabled by default", this.GetType().Name);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Failed loading provider configuration for {ProviderType}", this.GetType().Name);
            }
        }

        #region properties

        IMemoryCacheService Cache
        {
            get
            {
                if (_cache == null)
                    _cache = ServiceLocator.Instance.GetService<IMemoryCacheService>()!;

                return _cache;
            }
        }

        protected string ConfigurationDirectory => ConfigurationService.ConfigurationDirectory;

        protected IConfigurationService ConfigurationService
        {
            get
            {
                if (_config == null)
                    _config = ServiceLocator.Instance.GetService<IConfigurationService>()!;

                return _config;
            }
        }

        protected IPlatformService Platform
        {
            get
            {
                if (_platform == null)
                    _platform = ServiceLocator.Instance.GetService<IPlatformService>()!;

                return _platform;
            }
        }

        public bool IsRootDrive
        {
            get;
            protected set;
        }

        public string Name { get; set; } = string.Empty;

        public string Description
        {
            get;
            protected set;
        } = string.Empty;

        public string ImagePath
        {
            get;
            protected set;
        } = string.Empty;

        public Type ConfigurationEditorType
        {
            get;
            protected set;
        } = typeof(object);

        public bool IsEnabled
        {
            get => Get<bool>();
            set
            {
                if (Set(value))
                {
                    try
                    {
                        if (string.IsNullOrEmpty(Name))
                        {
                            Logger.Debug("Skipping persistence for provider config because Name is not set yet: {ProviderType}", this.GetType().Name);
                        }
                        else
                        {
                            Logger.Debug("Persisting provider config: {ProviderType} IsEnabled={IsEnabled}", this.GetType().Name, value);
                            var cfg = new Jaya.Shared.Models.ProviderConfigModel { IsEnabled = value };
                            SetConfiguration(cfg);
                            Logger.Debug("Persisted provider config for {ProviderType}", this.GetType().Name);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning(ex, "Failed persisting provider configuration for {ProviderType}", this.GetType().Name);
                    }
                }
            }
        }

        #endregion

        protected (string Name, string? Extension) SplitName(string fileName)
        {
            var nameParts = fileName.Split('.');
            if (nameParts.Length == 1)
            return (nameParts[0], null);

            var extensionBuilder = new StringBuilder();
            for (var index = 1; index < nameParts.Length - 1; index++)
                extensionBuilder.AppendFormat("{0}.", nameParts[index].ToLower());
            extensionBuilder.Append(nameParts[nameParts.Length - 1]);

            return (nameParts[0], extensionBuilder.ToString());
        }

        protected void OpenBrowser(string url)
        {
            Platform.OpenBrowser(url);
        }

        protected DirectoryModel? GetFromCache(AccountModelBase account, DirectoryModel? directory)
        {
            var hash = account.GetHashCode();
            if (directory != null)
                hash += directory.GetHashCode();

            if (Cache.TryGetValue(hash, out DirectoryModel? dir))
                return dir;

            return null;
        }

        protected void AddToCache(AccountModelBase account, DirectoryModel directory)
        {
            if (directory == null)
                throw new ArgumentNullException(nameof(directory));

            var hash = account.GetHashCode();
            if (!string.IsNullOrEmpty(directory.Path))
                hash += directory.Path.GetHashCode();

            Cache.Set(hash, directory);
        }

        public T GetConfiguration<T>() where T : ConfigModelBase
        {
            // Use the provider Name property as the configuration key when available.
            // If Name is not set yet (derived constructor hasn't run), fall back to the provider
            // type name so configuration is still loaded/saved per-provider.
            var key = !string.IsNullOrEmpty(Name) ? Name : this.GetType().Name;
            return ConfigurationService.GetOrDefault<T>(key);
        }

        protected void SetConfiguration<T>(T configuration) where T : ConfigModelBase
        {
            var key = !string.IsNullOrEmpty(Name) ? Name : this.GetType().Name;
            ConfigurationService.Set<T>(configuration, key);
        }

        public async Task<AccountModelBase?> AddAccount(AccountModelBase? account)
        {
            account = await AddAccountAsync(account);
            if (account != null)
            {
                var handler = AccountAdded;
                handler?.Invoke(account);
            }

            return account;
        }

        public async Task<bool> RemoveAccount(AccountModelBase account)
        {
            var isRemoved = await RemoveAccountAsync(account);
            if (isRemoved)
            {
                var handler = AccountRemoved;
                handler?.Invoke(account);
            }

            return isRemoved;
        }

        protected abstract Task<AccountModelBase?> AddAccountAsync(AccountModelBase? account = null);

        protected abstract Task<bool> RemoveAccountAsync(AccountModelBase account);

        public abstract Task<IEnumerable<AccountModelBase>> GetAccountsAsync();

        public abstract Task<DirectoryModel?> GetDirectoryAsync(AccountModelBase account, DirectoryModel? directory = null);

        public abstract Task FormatAsync(AccountModelBase account, DirectoryModel? directory = null);

        public override string ToString()
        {
            return Name;
        }

        public override int GetHashCode()
        {
            return Name.GetHashCode();
        }
    }
}
