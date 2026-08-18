using System;
using System.Reflection;
using Vintagestory.API.Common;

namespace ElectricalProgressiveColdStore;

internal static class ConfigIntegration
{
    private const string Domain =
        "electricalprogressivecoldstore";

    private const string ConfigFile =
        "electricalprogressivecoldstore.json";

    private sealed class Config
    {
        [System.ComponentModel.DisplayName("electricalprogressivecoldstore:setting-MinimumPerishRate")]
        [System.ComponentModel.Description("electricalprogressivecoldstore:setting-MinimumPerishRate-description")]
        public float MinimumPerishRate { get; set; } = 0.025f;
    }

    private static Config config = new();

    private static object? configLib;

    private static MethodInfo? getConfigMethod;

    private static MethodInfo? assignSettingsValuesMethod;

    public static float MinimumPerishRate
    {
        get
        {
            return config.MinimumPerishRate;
        }
    }

    public static void Initialize(ICoreAPI api)
    {
        try
        {
            configLib =
                api.ModLoader.GetModSystem(
                    "ConfigLib.ConfigLibModSystem"
                );

            if (configLib == null)
            {
                api.Logger.Warning(
                    "[ColdStore] ConfigLib not found."
                );

                return;
            }

            Type configLibType =
                configLib.GetType();

            MethodInfo? registerMethod = null;

            foreach (
                MethodInfo method
                in configLibType.GetMethods(
                    BindingFlags.Instance |
                    BindingFlags.Public
                )
            )
            {
                if (
                    method.Name ==
                    "RegisterCustomManagedConfig"
                )
                {
                    registerMethod = method;
                    break;
                }
            }

            if (registerMethod == null)
            {
                api.Logger.Warning(
                    "[ColdStore] RegisterCustomManagedConfig not found."
                );

                return;
            }

            config = new Config();

            registerMethod.Invoke(
                configLib,
                new object?[]
                {
                    Domain,
                    config,
                    ConfigFile,
                    null,
                    null,
                    null
                }
            );

            getConfigMethod =
                configLibType.GetMethod(
                    "GetConfig",
                    BindingFlags.Instance |
                    BindingFlags.Public,
                    null,
                    new[] { typeof(string) },
                    null
                );

            if (getConfigMethod == null)
            {
                api.Logger.Warning(
                    "[ColdStore] ConfigLib GetConfig method not found."
                );
            }

            api.Logger.Notification(
                "[ColdStore] ConfigLib managed config registered."
            );

            EventInfo? configsLoadedEvent =
                configLibType.GetEvent(
                    "ConfigsLoaded",
                    BindingFlags.Instance |
                    BindingFlags.Public
                );

            if (configsLoadedEvent != null)
            {
                Action callback =
                    () => ApplyLoadedConfig(api);

                configsLoadedEvent.AddEventHandler(
                    configLib,
                    callback
                );
            }
            else
            {
                api.Logger.Warning(
                    "[ColdStore] ConfigLib ConfigsLoaded event not found."
                );
            }
        }
        catch (Exception exception)
        {
            api.Logger.Error(
                "[ColdStore] ConfigLib integration failed: {0}",
                exception.ToString()
            );
        }
    }

    private static void ApplyLoadedConfig(
        ICoreAPI api
    )
    {
        try
        {
            if (
                configLib == null ||
                getConfigMethod == null
            )
            {
                return;
            }

            object? configObject =
                getConfigMethod.Invoke(
                    configLib,
                    new object[]
                    {
                        Domain
                    }
                );

            if (configObject == null)
            {
                api.Logger.Warning(
                    "[ColdStore] ConfigLib config '{0}' was not found.",
                    Domain
                );

                return;
            }

            if (
                assignSettingsValuesMethod == null
            )
            {
                assignSettingsValuesMethod =
                    configObject
                        .GetType()
                        .GetMethod(
                            "AssignSettingsValues",
                            BindingFlags.Instance |
                            BindingFlags.Public
                        );
            }

            if (
                assignSettingsValuesMethod == null
            )
            {
                api.Logger.Warning(
                    "[ColdStore] AssignSettingsValues method not found."
                );

                return;
            }

            assignSettingsValuesMethod.Invoke(
                configObject,
                new object[]
                {
                    config
                }
            );

            api.Logger.Notification(
                "[ColdStore] MinimumPerishRate loaded = {0}",
                config.MinimumPerishRate
            );
        }
        catch (Exception exception)
        {
            api.Logger.Error(
                "[ColdStore] Failed to apply ConfigLib values: {0}",
                exception.ToString()
            );
        }
    }
}