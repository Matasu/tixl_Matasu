#nullable enable

using ImGuiNET;
using T3.Editor.Gui.Styling;

namespace T3.Editor.Gui.Hub;

internal static class SkillQuestPanel
{
    internal static void Draw()
    {
        ContentPanel.Begin("Skill Quest", "some sub title", DrawIcons, Height);
        {
            ImGui.BeginChild("Map", new Vector2(100, 0));
            ImGui.Text("Dragons\nbe here");
            ImGui.EndChild();

            ImGui.SameLine(0, 10);
            
            ImGui.BeginGroup();
            ImGui.BeginChild("Content", new Vector2(0, -30),false );
            {
                ImGui.Text("Active level name");
            }
            ImGui.EndChild();

            ImGui.BeginChild("actions");
            {
                ImGui.Button("Skip");
                ImGui.SameLine(0, 10);
                ImGui.Button("Start");
            }
            ImGui.EndChild();
            
            ImGui.EndGroup();
        }

        ContentPanel.End();
    }

    private static void DrawIcons()
    {
        ImGui.Button("New Project");
        ImGui.SameLine(0, 10);

        Icon.AddFolder.DrawAtCursor();
    }

    internal static float Height => 120 * T3Ui.UiScaleFactor;
}