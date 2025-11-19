using System.Diagnostics;
using T3.Editor.Gui;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows.Layouts;
using T3.Editor.Gui.Windows.Output;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.SkillQuest;

internal static class SkillQuestStates
{
    internal static State<SkillQuestContext> Inactive
        = new(
              Enter: context =>
                     {
                         if (context.PreviousUiState != null)
                             UiState.ApplyUiState(context.PreviousUiState);
                     },
              Update: context => { },
              Exit:
              _ => { }
             );

    internal static State<SkillQuestContext> Playing
        = new(
              Enter: context =>
                     {
                         Debug.Assert(context.OpenedProject != null);

                         context.PreviousUiState = UiState.KeepUiState();

                         LayoutHandling.LoadAndApplyLayoutOrFocusMode(LayoutHandling.Layouts.SkillQuest);

                         // Pin output
                         if (ProjectView.Focused != null)
                         {
                             var rootInstance = context.OpenedProject.Structure.GetRootInstance();
                             var outputWindow = OutputWindow.GetPrimaryOutputWindow();
                             outputWindow?.Pinning.PinInstance(rootInstance);
                         }

                         UiState.HideAllUiElements();
                     },
              Update: context => { },
              Exit: _ => { }
             );

    internal static State<SkillQuestContext> Completed
        = new(
              Enter: _ => { },
              Update: context => { },
              Exit: _ => { }
             );
}