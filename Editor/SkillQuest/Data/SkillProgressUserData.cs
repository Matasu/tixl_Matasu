using System.IO;
using T3.Core.UserData;
using T3.Serialization;

namespace T3.Editor.SkillQuest.Data;

internal static class SkillProgressUserData
{
    internal static void LoadUserData()
    {
        if (!File.Exists(SkillProgressPath))
        {
            SkillQuestContext.SkillProgress = new SkillProgress(); // Fallback
        }

        try
        {
            SkillQuestContext.SkillProgress = JsonUtils.TryLoadingJson<SkillProgress>(SkillProgressPath);
            if (SkillQuestContext.SkillProgress == null)
                throw new Exception("Failed to load SkillProgress");
        }
        catch (Exception e)
        {
            Log.Error($"Failed to load {SkillProgressPath} : {e.Message}");
            SkillQuestContext.SkillProgress = new SkillProgress();
        }
    }

    internal static void SaveUserData()
    {
        Directory.CreateDirectory(FileLocations.SettingsDirectory);
        JsonUtils.TrySaveJson(SkillQuestContext.SkillProgress, SkillProgressPath);
    }

    private static string SkillProgressPath => Path.Combine(FileLocations.SettingsDirectory, "SkillProgress.json");
}