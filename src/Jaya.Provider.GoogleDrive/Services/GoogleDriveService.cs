//
// Copyright (c) Rubal Walia. All rights reserved.
// Licensed under the 3-Clause BSD license. See LICENSE file in the project root for full license information.
//
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Drive.v3.Data;
using Google.Apis.Oauth2.v2;
using Google.Apis.Oauth2.v2.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Jaya.Provider.GoogleDrive.Models;
using Jaya.Provider.GoogleDrive.Views;
using Jaya.Shared.Base;
using Jaya.Shared.Models;
using Jaya.Shared.Services;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Jaya.Provider.GoogleDrive.Services
{
    public class GoogleDriveService : ProviderServiceBase, IProviderService
    {
        const string CLIENT_ID = "538742722606-equtrav33c2tqaq2io7h19mkf4ch6jbp.apps.googleusercontent.com";
        const string CLIENT_SECRET = "UGprjYfFkb--RHnGbgAnm_Aj";

        const string MIME_TYPE_FILE = "application/vnd.google-apps.file";
        const string MIME_TYPE_DIRECTORY = "application/vnd.google-apps.folder";

        ConfigModel? _config;
        UserCredential? _credential;
        IDataStore? _dataStore;

        /// <summary>
        /// Refer pages https://www.daimto.com/google-drive-authentication-c/ and https://www.daimto.com/google-drive-api-c/ for examples.
        /// </summary>
        public GoogleDriveService()
        {
            Name = "Google Drive";
            ImagePath = "avares://Jaya.Provider.GoogleDrive/Assets/Images/GoogleDrive-32.png";
            Description = "View your Google Drive accounts, inspect their contents and play with directories & files stored within them.";
            ConfigurationEditorType = typeof(ConfigurationView);
        }

        #region properties

        IDataStore DataStore
        {
            get
            {
                if (_dataStore == null)
                    _dataStore = new FileDataStore(ConfigurationDirectory, true);

                return _dataStore!;
            }
        }

        internal ConfigModel Config
        {
            get
            {
                if (_config == null)
                {
                    _config = GetConfiguration<ConfigModel>();
                    _config.PropertyChanged += (sender, e) =>
                    {
                        if (string.Equals(e.PropertyName, nameof(ConfigModel.Accounts), StringComparison.Ordinal))
                            return;

                        SetConfiguration(_config);
                    };
                }

                return _config!;
            }
        }

        #endregion

        BaseClientService.Initializer GetServiceInitializer(UserCredential credentials)
        {
            return new BaseClientService.Initializer
            {
                HttpClientInitializer = credentials
            };
        }

        async Task<UserCredential?> GetCredential()
        {
            if (_credential != null && !_credential.Token.IsStale)
                return _credential;

            var scopes = new string[]
            {
                DriveService.Scope.Drive,
                DriveService.Scope.DriveMetadataReadonly,
                Oauth2Service.Scope.UserinfoProfile,
                Oauth2Service.Scope.UserinfoEmail
            };
            var secret = new ClientSecrets
            {
                ClientId = CLIENT_ID,
                ClientSecret = CLIENT_SECRET
            };

            _credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(secret, scopes, Environment.UserName, CancellationToken.None, DataStore);
            if (_credential == null)
                return null;

            if (_credential.Token.IsStale)
            {
                var isRefreshed = await _credential.RefreshTokenAsync(CancellationToken.None);
                if (!isRefreshed)
                    return null;
            }

            return _credential;
        }

        public override async Task<DirectoryModel?> GetDirectoryAsync(AccountModelBase account, DirectoryModel? directory = null)
        {
            var model = GetFromCache(account, directory);
            if (model != null)
                return model;
            else
                model = new DirectoryModel();

            model.Name = directory?.Name ?? string.Empty;
            model.Path = directory?.Path ?? string.Empty;
            model.Directories = new List<DirectoryModel>();
            model.Files = new List<FileModel>();

            var credentials = await GetCredential();
            if (credentials == null)
                return null;

            var parent = directory == null || directory.Id == null ? "root" : directory.Id;

            var query = new StringBuilder();
            if (directory == null || directory.Id == null)
                query.Append(" and 'root' in parents");
            else
                query.Append(" and '' in parents");

            FileList entries;
            using (var client = new DriveService(GetServiceInitializer(credentials)))
            {
                var request = client.Files.List();
                request.Q = string.Format("trashed = false and '{0}' in parents", parent);
                request.PageSize = Config.PageSize;
                request.Fields = "nextPageToken, files(id, name, mimeType, parents, createdTime, modifiedTime, fileExtension, size)";

                entries = await request.ExecuteAsync();

                while(entries.Files != null)
                {
                    foreach (var entry in entries.Files)
                    {
                        var parents = entry.Parents;
                        var mime = entry.MimeType ?? string.Empty;
                        if (mime.Equals(MIME_TYPE_DIRECTORY))
                        {
                            var dir = new DirectoryModel();
                            dir.Id = entry.Id;
                            dir.Name = entry.Name ?? string.Empty;
                            dir.Path = entry.Name ?? string.Empty;
                            dir.Size = entry.Size;
                            dir.Created = entry.CreatedTimeDateTimeOffset?.DateTime;
                            dir.Modified = entry.ModifiedTimeDateTimeOffset?.DateTime;
                            if (dir != null)
                                model.Directories.Add(dir);
                        }
                        else
                        {
                            var nameParts = SplitName(entry.Name ?? string.Empty);

                            var file = new FileModel();
                            file.Id = entry.Id;
                            file.Name = nameParts.Name;
                            file.Extension = nameParts.Extension ?? string.Empty;
                            file.Path = entry.Name ?? string.Empty;
                            file.Size = entry.Size;
                            file.Created = entry.CreatedTimeDateTimeOffset?.DateTime;
                            file.Modified = entry.ModifiedTimeDateTimeOffset?.DateTime;
                            if (file != null)
                                model.Files.Add(file);
                        }
                    }

                    if (entries.NextPageToken == null)
                        break;

                    request.PageToken = entries.NextPageToken;
                    entries = await request.ExecuteAsync();
                }
            }
           

            AddToCache(account, model);
            return model;
        }

        protected override async Task<AccountModelBase?> AddAccountAsync(AccountModelBase? account = null)
        {
            var credentials = await GetCredential();
            if (credentials == null)
                return null;

            Userinfo userInfo;
            using (var authService = new Oauth2Service(GetServiceInitializer(credentials)))
            {
                userInfo = await authService.Userinfo.Get().ExecuteAsync();
            }

            var config = GetConfiguration<ConfigModel>();

            var provider = new AccountModel(userInfo.Id, userInfo.Name)
            {
                Email = userInfo.Email
            };

            config.Accounts.Add(provider);
            SetConfiguration(config);

            return provider;
        }

        protected override async Task<bool> RemoveAccountAsync(AccountModelBase account)
        {
            var config = GetConfiguration<ConfigModel>();

            var acc = account as AccountModel;
            var isRemoved = false;
            if (acc != null)
            {
                isRemoved = config.Accounts.Remove(acc);
                if (isRemoved)
                    SetConfiguration(config);
            }

            return await Task.Run(() => isRemoved);
        }

        public override async Task<IEnumerable<AccountModelBase>> GetAccountsAsync()
        {
            var config = GetConfiguration<ConfigModel>();
            return await Task.Run(() => config.Accounts);
        }

        public override Task FormatAsync(AccountModelBase account, DirectoryModel? directory = null)
        {
            throw new NotImplementedException();
        }
    }
}
