﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using log4net;

namespace Raptor.Api
{
    internal sealed class ClientApi : IDisposable
    {
        private static readonly Version ApiVersion = new Version(1, 0);

        private readonly ILog _log = LogManager.GetLogger("API");
        private readonly List<TerrariaPlugin> _plugins = new List<TerrariaPlugin>();

        public void Dispose()
        {
            foreach (var plugin in _plugins)
            {
                try
                {
                    plugin.Dispose();
                    _log.Info($"Disposed plugin '{plugin.Name}'.");
                }
                catch (Exception ex)
                {
                    _log.Error($"An exception occurred while unloading {plugin.Name}:");
                    _log.Error(ex);
                }
            }
        }

        public void LoadPlugins()
        {
            Directory.CreateDirectory("plugins");
            foreach (var pluginPath     in Directory.EnumerateFiles("plugins", "*.dll"))
            {
                try
                {
                    var assembly = Assembly.LoadFrom(pluginPath);
                    var pluginTypes = from t in assembly.GetExportedTypes()
                                      where t.IsSubclassOf(typeof(TerrariaPlugin)) && !t.IsAbstract
                                      select t;
                    foreach (var pluginType in pluginTypes)
                    {
                        var attributes = pluginType.GetCustomAttributes(typeof(ApiVersionAttribute), false);
                        if (attributes.Length == 0)
                        {
                            _log.Error($"Plugin '{pluginType.FullName}' has no API version attribute.");
                            continue;
                        }

                        var apiVersion = ((ApiVersionAttribute)attributes[0]).ApiVersion;
                        if (apiVersion.Major != ApiVersion.Major || apiVersion.Minor != ApiVersion.Minor)
                        {
                            _log.Error($"Plugin '{pluginType.FullName}' is designed for a different API version.");
                            continue;
                        }

                        try
                        {
                            var plugin = (TerrariaPlugin)Activator.CreateInstance(pluginType);
                            _plugins.Add(plugin);
                            _log.Info($"Loaded {plugin.Name} v{plugin.Version} by {plugin.Author}.");
                        }
                        catch (Exception ex)
                        {
                            _log.Error($"An exception occurred while loading plugin '{pluginType.FullName}':");
                            _log.Error(ex);
                        }
                    }
                }
                catch (BadImageFormatException)
                {
                }
                catch (Exception ex)
                {
                    _log.Error($"An exception occurred while loading assembly '{pluginPath}':");
                    _log.Error(ex);
                }
            }

            IOrderedEnumerable<TerrariaPlugin> orderedPluginSelector =
                from x in _plugins
                orderby x.Order, x.Name
                select x;

            foreach (TerrariaPlugin current in orderedPluginSelector)
            {
                try
                {
                    current.Initialize();
                }
                catch (Exception ex)
                {
                    // Broken plugins better stop the entire server init.
                    throw new InvalidOperationException(string.Format(
                        "Plugin \"{0}\" has thrown an exception during initialization.", current.Name), ex);
                }

                _log.Error(string.Format(
                    "Plugin {0} v{1} (by {2}) initiated.", current.Name, current.Version, current.Author));
            }


        }
    }
}
