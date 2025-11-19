#nullable enable
using System.Diagnostics.CodeAnalysis;
using System.IO;
using T3.Core.UserData;
using T3.Editor.Gui;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.MagGraph.Ui;
using T3.Editor.Gui.Window;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows.Layouts;
using T3.Editor.Gui.Windows.Output;
using T3.Editor.SkillQuest.Data;
using T3.Editor.UiModel;
using T3.Editor.UiModel.ProjectHandling;
using T3.Serialization;

namespace T3.Editor.SkillQuest;

internal static partial class SkillManager
{
    internal static void Initialize()
    {
        InitializeLevels();

        SkillProgressUserData.LoadUserData();
        SkillProgressUserData.SaveUserData();
    }

    internal static void Update()
    {
        var playmodeEnded = _context.GraphView is { Destroyed: true };
        if (_stateMachine.CurrentState != SkillQuestStates.Inactive && playmodeEnded)
        {
            _stateMachine.SetState(SkillQuestStates.Inactive, _context);
        }

        _stateMachine.UpdateAfterDraw(_context);
    }

    private static void InitializeLevels()
    {
        // TODO: Load from Json
        SkillQuestContext.Topics = CreateMockLevelStructure();
    }

    public static bool TryGetActiveTopic([NotNullWhen(true)] out QuestTopic? topic)
    {
        topic = null;

        if (SkillQuestContext.Topics.Count == 0)
            return false;

        topic = SkillQuestContext.Topics[0];
        return true;
    }

    public static bool TryGetActiveLevel([NotNullWhen(true)] out QuestLevel? level)
    {
        level = null;
        if (!TryGetActiveTopic(out var activeTopic))
            return false;

        if (activeTopic.Levels.Count == 0)
            return false;

        level = activeTopic.Levels[0];
        return true;
    }

    public static bool TryGetSkillsProject([NotNullWhen(true)] out EditableSymbolProject? skillProject)
    {
        skillProject = null;
        foreach (var p in EditableSymbolProject.AllProjects)
        {
            if (p.Alias == "Skills")
            {
                skillProject = p;
                return true;
            }
        }

        return false;
    }

    public static void StartGame(GraphWindow graphWindow, QuestLevel activeLevel)
    {
        if (!TryGetSkillsProject(out var skillProject))
            return;

        if (!OpenedProject.TryCreateWithExplicitHome(skillProject,
                                                     activeLevel.SymbolId,
                                                     out var openedProject,
                                                     out var failureLog))
        {
            Log.Warning(failureLog);
            return;
        }

        graphWindow.TrySetToProject(openedProject);
        _context.OpenedProject = openedProject;

        if (graphWindow.ProjectView?.GraphView is not MagGraphView magGraphView)
            return;

        _context.GraphView = magGraphView;

        _stateMachine.SetState(SkillQuestStates.Playing, _context);
    }

    private static readonly SkillQuestContext _context = new();
    private static readonly StateMachine<SkillQuestContext> _stateMachine = new(SkillQuestStates.Inactive);
}