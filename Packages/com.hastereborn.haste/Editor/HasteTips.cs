namespace Haste {

  public static class HasteTips {

    public static string[] Tips = new string[]{
      "Add your own commands to Haste using the MenuItem attribute.",
      "Try searching for <b>Build Settings</b> to avoid hunting it down.",
      // "Search results are ranked by usage.",

      "Use fuzzy matching to search without typing the full name.",
      "Access Haste's preferences in the Unity preferences window.",
      "You can <b>drag and drop an Asset or GameObject</b> directly from Haste.",
      "You can select multiple results in Haste using <b>Cmd/Ctrl+Click</b>.",
      "Select <b>Assets > Haste > Ignore/Unignore</b> to hide results.",
      "Manage ignored assets from Haste's preferences.",
    };

    public static string Random {
      get {
        return Tips[HasteSettings.UsageCount % Tips.Length];
      }
    }
  }
}
