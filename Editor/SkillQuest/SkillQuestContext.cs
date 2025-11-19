#nullable enable
using T3.Editor.Gui.MagGraph.Ui;
using T3.Editor.Gui.Windows.Layouts;
using T3.Editor.SkillQuest.Data;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.SkillQuest;

internal sealed class SkillQuestContext // can't be generic because it's used for generic state
{
    internal static List<QuestTopic> Topics=[];
    internal static SkillProgress SkillProgress = new();
    internal OpenedProject? OpenedProject;
    internal UiState.UiElementsVisibility? PreviousUiState;
    internal MagGraphView? GraphView;
}