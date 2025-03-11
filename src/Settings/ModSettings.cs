using System;
using System.IO;
using System.Xml.Serialization;
using TaleWorlds.Library;

namespace PickItUp.Settings
{
    public class ModSettings
    {
        private static ModSettings _instance;
        private static readonly string ConfigPath = Path.Combine(
            BasePath.Name,
            "Modules",
            "PickItUp",
            "config.xml"
        );
        
        public static ModSettings Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Load();
                }
                return _instance;
            }
        }

        public bool EnableReloadResetPatch { get; set; } = true;

        private static ModSettings Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
#if DEBUG
                    DebugHelper.Log("ModSettings", $"正在读取配置文件: {ConfigPath}");
#endif
                    using (var reader = new StreamReader(ConfigPath))
                    {
                        var serializer = new XmlSerializer(typeof(ModSettings));
                        var settings = (ModSettings)serializer.Deserialize(reader);
#if DEBUG
                        DebugHelper.Log("ModSettings", $"已读取配置，RBM填装修改重置状态: {(settings.EnableReloadResetPatch ? "启用" : "禁用")}");
#endif
                        return settings;
                    }
                }
#if DEBUG
                DebugHelper.Log("ModSettings", $"未找到配置文件: {ConfigPath}，使用默认配置（启用RBM填装修改重置）");
#endif
            }
            catch (Exception ex)
            {
#if DEBUG
                DebugHelper.LogError("ModSettings", $"读取配置文件出错: {ex.Message}", ex);
#endif
            }

            return new ModSettings();
        }
    }
} 