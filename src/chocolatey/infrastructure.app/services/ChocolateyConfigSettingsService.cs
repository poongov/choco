﻿// Copyright © 2011 - Present RealDimensions Software, LLC
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// 
// You may obtain a copy of the License at
// 
// 	http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

namespace chocolatey.infrastructure.app.services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using configuration;
    using infrastructure.services;
    using nuget;

    internal class ChocolateyConfigSettingsService : IChocolateyConfigSettingsService
    {
        private readonly Lazy<ConfigFileSettings> _configFileSettings;
        private readonly IXmlService _xmlService;
        private const string NO_CHANGE_MESSAGE = "Nothing to change. Config already set.";

        private ConfigFileSettings configFileSettings
        {
            get { return _configFileSettings.Value; }
        }

        public ChocolateyConfigSettingsService(IXmlService xmlService)
        {
            _xmlService = xmlService;
            _configFileSettings = new Lazy<ConfigFileSettings>(() => _xmlService.deserialize<ConfigFileSettings>(ApplicationParameters.GlobalConfigFileLocation));
        }

        public void noop(ChocolateyConfiguration configuration)
        {
            this.Log().Info("Would have made a change to the configuration.");
        }

        public IEnumerable<ChocolateySource> source_list(ChocolateyConfiguration configuration)
        {
            var list = new List<ChocolateySource>();
            foreach (var source in configFileSettings.Sources)
            {
                if (configuration.RegularOutput) { 
                    this.Log().Info(() => "{0}{1} - {2}".format_with(source.Id, source.Disabled ? " [Disabled]" : string.Empty, source.Value));
                }
                list.Add(new ChocolateySource {
                    Id = source.Id,
                    Value = source.Value,
                    Disabled =  source.Disabled,
                    Authenticated = string.IsNullOrWhiteSpace(source.Password)
                });
            }
            return list;
        }

        public void source_add(ChocolateyConfiguration configuration)
        {
            var source = configFileSettings.Sources.FirstOrDefault(p => p.Id.is_equal_to(configuration.SourceCommand.Name));
            if (source == null)
            {
                configFileSettings.Sources.Add(new ConfigFileSourceSetting
                    {
                        Id = configuration.SourceCommand.Name,
                        Value = configuration.Sources,
                        UserName = configuration.SourceCommand.Username,
                        Password = NugetEncryptionUtility.EncryptString(configuration.SourceCommand.Password),
                    });

                _xmlService.serialize(configFileSettings, ApplicationParameters.GlobalConfigFileLocation);

                this.Log().Info(() => "Added {0} - {1}".format_with(configuration.SourceCommand.Name, configuration.Sources));
            }
            else
            {
                this.Log().Warn(@"
No changes made. If you are trying to change an existing source, please 
 remove it first.");
            }
        }

        public void source_remove(ChocolateyConfiguration configuration)
        {
            var source = configFileSettings.Sources.FirstOrDefault(p => p.Id.is_equal_to(configuration.SourceCommand.Name));
            if (source != null)
            {
                configFileSettings.Sources.Remove(source);
                _xmlService.serialize(configFileSettings, ApplicationParameters.GlobalConfigFileLocation);

                this.Log().Info(() => "Removed {0}".format_with(source.Id));
            }
            else
            {
                this.Log().Warn(NO_CHANGE_MESSAGE);
            }
        }

        public void source_disable(ChocolateyConfiguration configuration)
        {
            var source = configFileSettings.Sources.FirstOrDefault(p => p.Id.is_equal_to(configuration.SourceCommand.Name));
            if (source != null && !source.Disabled)
            {
                source.Disabled = true;
                _xmlService.serialize(configFileSettings, ApplicationParameters.GlobalConfigFileLocation);
                this.Log().Info(() => "Disabled {0}".format_with(source.Id));
            }
            else
            {
                this.Log().Warn(NO_CHANGE_MESSAGE);
            }
        }

        public void source_enable(ChocolateyConfiguration configuration)
        {
            var source = configFileSettings.Sources.FirstOrDefault(p => p.Id.is_equal_to(configuration.SourceCommand.Name));
            if (source != null && source.Disabled)
            {
                source.Disabled = false;
                _xmlService.serialize(configFileSettings, ApplicationParameters.GlobalConfigFileLocation);
                this.Log().Info(() => "Enabled {0}".format_with(source.Id));
            }
            else
            {
                this.Log().Warn(NO_CHANGE_MESSAGE);
            }
        }

        public void feature_list(ChocolateyConfiguration configuration)
        {
            foreach (var feature in configFileSettings.Features)
            {
                this.Log().Info(() => "{0} - {1}".format_with(feature.Name, !feature.Enabled ? "[Disabled]" : "[Enabled]"));
            }
        }

        public void feature_disable(ChocolateyConfiguration configuration)
        {
            var feature = configFileSettings.Features.FirstOrDefault(p => p.Name.is_equal_to(configuration.FeatureCommand.Name));
            if (feature != null && (feature.Enabled || !feature.SetExplicitly))
            {
                if (!feature.Enabled && !feature.SetExplicitly)
                {
                    this.Log().Warn(() => "{0} was disabled by default. Explicitly setting value.".format_with(feature.Name));
                }
                feature.Enabled = false;
                feature.SetExplicitly = true;
                _xmlService.serialize(configFileSettings, ApplicationParameters.GlobalConfigFileLocation);
                this.Log().Info(() => "Disabled {0}".format_with(feature.Name));
            }
            else
            {
                this.Log().Warn(NO_CHANGE_MESSAGE);
            }
        }

        public void feature_enable(ChocolateyConfiguration configuration)
        {
            var feature = configFileSettings.Features.FirstOrDefault(p => p.Name.is_equal_to(configuration.FeatureCommand.Name));
            if (feature != null && (!feature.Enabled || !feature.SetExplicitly))
            {
                if (feature.Enabled && !feature.SetExplicitly)
                {
                    this.Log().Warn(() => "{0} was enabled by default. Explicitly setting value.".format_with(feature.Name));
                }
                feature.Enabled = true;
                feature.SetExplicitly = true;
                _xmlService.serialize(configFileSettings, ApplicationParameters.GlobalConfigFileLocation);
                this.Log().Info(() => "Enabled {0}".format_with(feature.Name));
            }
            else
            {
                this.Log().Warn(NO_CHANGE_MESSAGE);
            }
        }

        public string get_api_key(ChocolateyConfiguration configuration, Action<ConfigFileApiKeySetting> keyAction)
        {
            string apiKeyValue = null;

            if (!string.IsNullOrWhiteSpace(configuration.Sources))
            {
                var apiKey = configFileSettings.ApiKeys.FirstOrDefault(p => p.Source.TrimEnd('/').is_equal_to(configuration.Sources.TrimEnd('/')));
                if (apiKey != null)
                {
                    apiKeyValue = NugetEncryptionUtility.DecryptString(apiKey.Key).to_string();

                    if (keyAction != null)
                    {
                        keyAction.Invoke(new ConfigFileApiKeySetting {Key = apiKeyValue, Source = apiKey.Source});
                    }
                }
            }
            else
            {
                foreach (var apiKey in configFileSettings.ApiKeys.or_empty_list_if_null())
                {
                    var keyValue = NugetEncryptionUtility.DecryptString(apiKey.Key).to_string();
                    if (keyAction != null)
                    {
                        keyAction.Invoke(new ConfigFileApiKeySetting {Key = keyValue, Source = apiKey.Source});
                    }
                }
            }

            return apiKeyValue;
        }

        public void set_api_key(ChocolateyConfiguration configuration)
        {
            var apiKey = configFileSettings.ApiKeys.FirstOrDefault(p => p.Source.is_equal_to(configuration.Sources));
            if (apiKey == null)
            {
                configFileSettings.ApiKeys.Add(new ConfigFileApiKeySetting
                    {
                        Source = configuration.Sources,
                        Key = NugetEncryptionUtility.EncryptString(configuration.ApiKeyCommand.Key),
                    });

                _xmlService.serialize(configFileSettings, ApplicationParameters.GlobalConfigFileLocation);

                this.Log().Info(() => "Added ApiKey for {0}".format_with(configuration.Sources));
            }
            else
            {
                if (!NugetEncryptionUtility.DecryptString(apiKey.Key).to_string().is_equal_to(configuration.ApiKeyCommand.Key))
                {
                    apiKey.Key = NugetEncryptionUtility.EncryptString(configuration.ApiKeyCommand.Key);
                    _xmlService.serialize(configFileSettings, ApplicationParameters.GlobalConfigFileLocation);
                    this.Log().Info(() => "Updated ApiKey for {0}".format_with(configuration.Sources));
                }
                else
                {
                    this.Log().Warn(NO_CHANGE_MESSAGE);
                }
            }
        }
    }
}
