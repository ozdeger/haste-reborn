using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace Haste {

  public delegate void SceneChangedHandler(string currentScene, string previousScene);
  public delegate void SelectionChangedHandler();

  public delegate void HasteWindowAction();

  [InitializeOnLoad]
  public static class Haste {

    public static readonly string VERSION = "1.8.6";

    private static Version version;
    public static Version Version {
      get {
        if (version == null) {
          version = new Version(VERSION);
        }
        return version;
      }
    }

    public static event SceneChangedHandler SceneChanged;
    public static event SelectionChangedHandler SelectionChanged;

    public static HasteScheduler Scheduler;
    public static HasteIndex Index;
    public static HasteSearch Search;
    public static HasteWatcherManager Watchers;

    internal static event HasteWindowAction WindowAction;

    static string currentScene;
    static bool isCompiling = false;
    // static object prefKey;

    static double layoutInterval = 30.0;
    static double lastLayoutCheck = 0.0;

    public static bool IsApplicationBusy {
      get {
        var willPlay = EditorApplication.isPlayingOrWillChangePlaymode &&
          !EditorApplication.isPlaying;

        return !HasteSettings.Enabled ||
               willPlay ||
               EditorApplication.isUpdating;
      }
    }

    public static int IndexedCount {
      get { return Watchers.IndexedCount; }
    }

    public static int IndexingCount {
      get { return Watchers.IndexingCount; }
    }

    public static bool IsIndexing {
      get { return Watchers.IsIndexing; }
    }

    static Haste() {
      // prefKey = HasteReflection.Instantiate(HasteReflection.EditorAssembly, "UnityEditor.PrefKey", "Window/Haste", DEFAULT_SHORTCUT);

      currentScene = ActiveScenePath;
      isCompiling = EditorApplication.isCompiling;

      Scheduler = new HasteScheduler();
      Index = new HasteIndex();
      Search = new HasteSearch(Index);
      Watchers = new HasteWatcherManager();

      Watchers.AddSource(HasteProjectSource.NAME,
        EditorPrefs.GetBool(HasteSettings.GetPrefKey(HasteSetting.Source, HasteProjectSource.NAME), true),
        () => new HasteProjectSource());
      Watchers.AddSource(HasteHierarchySource.NAME,
        EditorPrefs.GetBool(HasteSettings.GetPrefKey(HasteSetting.Source, HasteHierarchySource.NAME), true),
        () => new HasteHierarchySource());
      Watchers.AddSource(HasteMenuItemSource.NAME,
        EditorPrefs.GetBool(HasteSettings.GetPrefKey(HasteSetting.Source, HasteMenuItemSource.NAME), true),
        () => new HasteMenuItemSource());
      Watchers.AddSource(HasteLayoutSource.NAME,
        EditorPrefs.GetBool(HasteSettings.GetPrefKey(HasteSetting.Source, HasteLayoutSource.NAME), true),
        () => new HasteLayoutSource());

      lastLayoutCheck = EditorApplication.timeSinceStartup;

      HasteSettings.ChangedBool += BoolSettingChanged;
      HasteSettings.ChangedString += StringSettingChanged;
      EditorApplication.projectChanged += ProjectWindowChanged;
      EditorApplication.hierarchyChanged += HierarchyWindowChanged;

      // Scene changes used to be found by polling the obsolete
      // EditorApplication.currentScene on every editor update. They arrive as events now.
      //
      // Two different things matter here and conflating them loses one: which scene is
      // ACTIVE -- what SceneChanged reports, and all `currentScene` could ever mean -- and
      // which scenes are LOADED, which is what the hierarchy index actually depends on. A
      // scene opened additively adds objects without changing the active scene at all.
      EditorSceneManager.activeSceneChangedInEditMode += ActiveSceneChanged;
      EditorSceneManager.sceneOpened += SceneOpened;
      EditorSceneManager.sceneClosed += SceneClosed;
      EditorSceneManager.newSceneCreated += NewSceneCreated;

      // Likewise: this replaces comparing Selection.activeInstanceID against a cached copy
      // on every update. Per HANDOFF 3.3 the obsolete property's own suggested
      // replacement, activeEntityId, does not exist in 6000.0 -- Selection.objects and
      // Selection.selectionChanged are clean on both editors.
      Selection.selectionChanged += OnSelectionChanged;

      SceneChanged += HandleSceneChanged;
      EditorApplication.update += Update;

      // AddGlobalEventHandler();

      if (HasteSettings.UsageSince == 0L) {
        HasteSettings.UsageSince = DateTime.Now.Ticks;
      }

      HasteSettings.Version = VERSION;

      // The palette's own styling is USS and loads with the window; this is only for the
      // preferences page, which is still IMGUI.
      Scheduler.Start(HasteStyles.Init());
    }

    // static void AddGlobalEventHandler() {
    //   var fieldInfo = typeof(EditorApplication).GetField("globalEventHandler", BindingFlags.NonPublic|BindingFlags.Static);

    //   var origHandler = (EditorApplication.CallbackFunction)fieldInfo.GetValue(null);
    //   var newHandler = new EditorApplication.CallbackFunction(GlobalEventHandler);
    //   fieldInfo.SetValue(null, Delegate.Combine(
    //     origHandler,
    //     newHandler
    //   ));
    // }

    static void BoolSettingChanged(HasteSetting setting, bool before, bool after) {
      switch (setting) {
        case HasteSetting.Enabled:
          if (after) {
            Rebuild();
          } else {
            Stop();
          }
          break;
      }
    }

    static void StringSettingChanged(HasteSetting setting, string before, string after) {
      switch (setting) {
        case HasteSetting.Version:
          Rebuild();
          break;
      }
    }

    static string ActiveScenePath {
      get { return SceneManager.GetActiveScene().path; }
    }

    static void ActiveSceneChanged(Scene previous, Scene current) {
      SyncActiveScene();
    }

    static void SceneOpened(Scene scene, OpenSceneMode mode) {
      SyncActiveScene();
    }

    static void SceneClosed(Scene scene) {
      SyncActiveScene();
    }

    static void NewSceneCreated(Scene scene, NewSceneSetup setup, NewSceneMode mode) {
      SyncActiveScene();
    }

    static void SyncActiveScene() {
      var previousScene = currentScene;
      currentScene = ActiveScenePath;

      if (currentScene != previousScene) {
        OnSceneChanged(currentScene, previousScene);
      } else {
        // Same active scene, different set of loaded objects: an additive open or a close.
        // The hierarchy index still has to be rebuilt.
        Watchers.RestartSource(HasteHierarchySource.NAME);
      }
    }

    static void ProjectWindowChanged() {
      Watchers.RestartSource(HasteProjectSource.NAME);
    }

    static void HierarchyWindowChanged() {
      Watchers.RestartSource(HasteHierarchySource.NAME);
    }

    static void HandleSceneChanged(string currentScene, string previousScene) {
      Watchers.RestartSource(HasteHierarchySource.NAME);
    }

    public static void Rebuild() {
      Index.Clear();
      Watchers.Rebuild();
    }

    public static void Stop() {
      Index.Clear();
      Watchers.Stop();
    }

    static void OnSceneChanged(string currentScene, string previousScene) {
      if (SceneChanged != null) {
        SceneChanged(currentScene, previousScene);
      }
    }

    static void OnSelectionChanged() {
      if (SelectionChanged != null) {
        SelectionChanged();
      }
    }

    static void OnScriptsCompiled() {
      Watchers.RestartSource(HasteMenuItemSource.NAME);
    }

    // static void HasteShortcutHandler() {
    //   if (HasteReflection.GetPropValue<bool>(prefKey, "activated")) {
    //     Event.current.Use();
    //     HasteWindow.Open();
    //   }
    // }

    // static void GlobalEventHandler() {
    //   HasteShortcutHandler();
    // }

    // The maximum time an iteration can spend working per update
    public const float MAX_ITER_TIME = 16.0f / 1000.0f;

    // Main update loop in Haste—run's scheduler
    static void Update() {
      // We must delay the window action to handle actions
      // that affect layout state to prevent bugs in Unity.
      if (WindowAction != null && HasteSpotlightWindow.Instance == null) {
        try {
          WindowAction();
        } finally {
          WindowAction = null;
        }
      }

      // Compiling state changed
      if (isCompiling != EditorApplication.isCompiling) {
        isCompiling = EditorApplication.isCompiling;

        // Done compiling
        if (!isCompiling) {
          OnScriptsCompiled();
        }
      }

      if (!IsApplicationBusy) {
        // Check layouts folder every so often
        double now = EditorApplication.timeSinceStartup;
        if (now - lastLayoutCheck > layoutInterval) {
          lastLayoutCheck = now;
          Watchers.RestartSource(HasteLayoutSource.NAME);
        }

        // The condition measures elapsed time from `start` on every pass. It used to
        // accumulate (now - start) into a running total each iteration, which sums
        // t + 2t + 3t + ... rather than n*t -- a triangular series that reached the 16 ms
        // budget after about sqrt(2 * MAX_ITER_TIME / t) iterations instead of
        // MAX_ITER_TIME / t. At a 0.1 ms tick that is ~18 iterations per frame where the
        // budget allows ~160, so indexing and search ran close to an order of magnitude
        // slower than this constant says they do.
        //
        // Fixing the arithmetic makes MAX_ITER_TIME mean what it claims, which is a real
        // increase in work done per frame. If that proves too aggressive in a live editor,
        // MAX_ITER_TIME is the dial -- do not reintroduce the bug to get the old feel.
        var start = EditorApplication.timeSinceStartup;

        while (Scheduler.IsRunning &&
               (EditorApplication.timeSinceStartup - start) < MAX_ITER_TIME) {
          Scheduler.Tick();
        }
      }
    }
  }
}
