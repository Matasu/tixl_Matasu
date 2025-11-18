namespace T3.Editor.SkillQuest;

using T3.Editor.Gui.MagGraph.States;

internal static class SkillQuestStates
{
    internal static State Default
        = new(
              Enter: context => { },
              Update: context => { },
              Exit:
              _ => { }
             );

    /** A special mode to prevent interaction with graph elements */
    internal static State BackgroundContentIsInteractive
        = new(
              Enter: _ => { },
              Update: context =>
                      {
                          // Switch back
                          if (!context.ProjectView.GraphImageBackground.HasInteractionFocus)
                          {
                              context.StateMachine.SetState(Default, context);
                              return;
                          }
                      },
              Exit: _ => { }
             );

    /// <summary>
    /// Active while long tapping on a background for insertion
    /// </summary>
    internal static State HoldBackground
        = new(
              Enter: _ => { },
              Update: context => { },
              Exit: _ => { }
             );
}