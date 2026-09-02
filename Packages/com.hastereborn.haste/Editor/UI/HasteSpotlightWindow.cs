using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Haste {

  // The Haste palette, built on UI Toolkit against the "Unity Spotlight" design.
  //
  // It replaces the IMGUI window. The design asks for rounded rows, hover states, chips
  // and a per-row highlight that IMGUI can only fake, and Unity migrated its own palette
  // the same way inside the Unity 6 cycle (2022.3 QuickSearch draws with OnGUI, 6000.x
  // SearchWindow uses CreateGUI and a plain ListView).
  //
  // Deliberately NOT ported from the design, and why:
  //   - The sliding actions pane (-> / Cmd+K). Out of scope for this pass.
  //   - Group headers per kind. Haste ranks globally by score, and grouping by kind in a
  //     fixed order would put a weak asset match above an exact menu-item match.
  //   - "Suggested commands" on launch. There is no signal behind it yet; recency is real
  //     and is what the Recent section shows.
  public class HasteSpotlightWindow : EditorWindow {

    public const int WindowWidth = 708;
    public const int WindowHeight = 452;

    const int ResultCount = 100;
    const int RowHeight = 32;

    // Rich-text markers spliced around matched characters. The design shows no
    // highlighting at all; keeping it is a deliberate departure, because with acronym
    // matching the highlight is the only thing that explains why a row is in the list.
    const string NameHighlightStart = "<color=#ffffff>";
    const string PathHighlightStart = "<color=#b9b9b9>";
    const string HighlightEnd = "</color>";

    static readonly string[] PrefixHints = {
      "asset:", "prefab:", "scene:", "script:", "h:", ">", "#",
    };

    public static HasteSpotlightWindow Instance { get; private set; }

    public static bool IsOpen {
      get { return Instance != null; }
    }

    // ------------------------------------------------------------------ state

    [SerializeField] UnityEngine.Object[] prevSelection;

    readonly HashSet<UnityEngine.Object> multiSelection = new HashSet<UnityEngine.Object>();

    IHasteResult[] results = new IHasteResult[0];
    int highlighted;

    string query = "";
    HasteKind scopeKinds = HasteKind.Any;
    string scopeToken;

    HasteSchedulerNode searching;
    bool holdsReloadLock;
    bool wasIndexing;

    // ------------------------------------------------------------- elements

    TextField queryField;
    VisualElement scopeChip;
    Label scopeChipLabel;
    VisualElement hintsRow;
    ListView listView;
    VisualElement messageView;
    Label messageTitle;
    Label messageHint;
    Label statusLabel;
    Label countLabel;
    VisualElement footerIcon;

    // ------------------------------------------------------------- lifecycle

    public static void Open() {
      if (!HasteSettings.Enabled) {
        return;
      }

      if (Instance != null) {
        Instance.Focus();
        return;
      }

      var window = CreateInstance<HasteSpotlightWindow>();
      Instance = window;
      window.position = GetPosition();
      window.minSize = window.maxSize = new Vector2(WindowWidth, WindowHeight);
      window.ShowPopup();
      window.Focus();
      window.FocusQuery();
    }

    // Centres on the display the mouse is on rather than on Screen.currentResolution,
    // which reports the primary display and put the palette on the wrong monitor -- and,
    // on a scaled display, off-centre on the right one.
    static Rect GetPosition() {
      var saved = HasteSettings.WindowPosition;
      if (saved != Vector2.zero) {
        return new Rect(saved.x, saved.y, WindowWidth, WindowHeight);
      }

      var area = HasteDisplay.MainWindowArea();
      return new Rect(
        area.x + (area.width - WindowWidth) / 2f,
        area.y + (area.height - WindowHeight) / 3f,
        WindowWidth, WindowHeight);
    }

    void OnEnable() {
      Instance = this;
      LockReload();

      if (Selection.objects != null) {
        prevSelection = new UnityEngine.Object[Selection.objects.Length];
        Array.Copy(Selection.objects, prevSelection, Selection.objects.Length);
      }
    }

    void OnDestroy() {
      if (searching != null && searching.IsRunning) {
        searching.Stop();
      }

      UnlockReload();

      if (Instance == this) {
        Instance = null;
      }
    }

    void LockReload() {
      if (!holdsReloadLock) {
        holdsReloadLock = true;
        EditorApplication.LockReloadAssemblies();
      }
    }

    void UnlockReload() {
      if (holdsReloadLock) {
        holdsReloadLock = false;
        EditorApplication.UnlockReloadAssemblies();
      }
    }

    // --------------------------------------------------------------- build

    public void CreateGUI() {
      var root = rootVisualElement;
      root.AddToClassList("haste-root");

      var sheet = HasteResources.Load<StyleSheet>("UI/HasteSpotlight.uss");
      if (sheet != null) {
        root.styleSheets.Add(sheet);
      }

      root.Add(BuildHeader());
      root.Add(BuildHints());
      root.Add(Divider());
      root.Add(BuildBody());
      root.Add(Divider());
      root.Add(BuildFooter());

      // TrickleDown so the arrow keys reach us before the text field consumes them.
      root.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);

      RestoreRecommendations();
      FocusQuery();
    }

    static VisualElement Divider() {
      var divider = new VisualElement();
      divider.AddToClassList("haste-divider");
      return divider;
    }

    VisualElement BuildHeader() {
      var header = new VisualElement();
      header.AddToClassList("haste-header");

      var badge = new Label("U");
      badge.AddToClassList("haste-badge");
      header.Add(badge);

      scopeChip = new VisualElement();
      scopeChip.AddToClassList("haste-scope");
      scopeChip.style.display = DisplayStyle.None;
      scopeChipLabel = new Label();
      scopeChip.Add(scopeChipLabel);
      var clear = new Label("×");
      clear.AddToClassList("haste-scope-clear");
      scopeChip.Add(clear);
      scopeChip.RegisterCallback<MouseDownEvent>(evt => { ClearScope(); evt.StopPropagation(); });
      header.Add(scopeChip);

      queryField = new TextField();
      queryField.AddToClassList("haste-query");
      queryField.RegisterValueChangedCallback(evt => OnQueryChanged(evt.newValue));
      header.Add(queryField);

      countLabel = new Label();
      countLabel.AddToClassList("haste-count");
      countLabel.style.display = DisplayStyle.None;
      header.Add(countLabel);

      return header;
    }

    VisualElement BuildHints() {
      hintsRow = new VisualElement();
      hintsRow.AddToClassList("haste-hints");

      foreach (var hint in PrefixHints) {
        var chip = new Label(hint);
        chip.AddToClassList("haste-hint");
        hintsRow.Add(chip);
      }

      return hintsRow;
    }

    VisualElement BuildBody() {
      var body = new VisualElement();
      body.AddToClassList("haste-body");

      listView = new ListView {
        fixedItemHeight = RowHeight,
        selectionType = SelectionType.Single,
        makeItem = MakeRow,
        bindItem = BindRow,
        itemsSource = results,
      };
      listView.AddToClassList("haste-list");
      listView.selectionChanged += _ => {
        if (listView.selectedIndex >= 0 && listView.selectedIndex != highlighted) {
          SetHighlighted(listView.selectedIndex, false);
        }
      };
      body.Add(listView);

      messageView = new VisualElement();
      messageView.AddToClassList("haste-message");
      var box = new VisualElement();
      box.AddToClassList("haste-message-box");
      messageView.Add(box);
      messageTitle = new Label();
      messageTitle.AddToClassList("haste-message-title");
      messageView.Add(messageTitle);
      messageHint = new Label();
      messageHint.AddToClassList("haste-message-hint");
      messageView.Add(messageHint);
      messageView.style.display = DisplayStyle.None;
      body.Add(messageView);

      return body;
    }

    VisualElement BuildFooter() {
      var footer = new VisualElement();
      footer.AddToClassList("haste-footer");

      footerIcon = new VisualElement();
      footerIcon.AddToClassList("haste-footer-icon");
      footer.Add(footerIcon);

      statusLabel = new Label();
      statusLabel.AddToClassList("haste-status");
      footer.Add(statusLabel);

      var spacer = new VisualElement();
      spacer.AddToClassList("haste-spacer");
      footer.Add(spacer);

      var open = new Label("Open");
      open.AddToClassList("haste-action-label");
      footer.Add(open);

      var key = new Label("↵");
      key.AddToClassList("haste-key");
      footer.Add(key);

      return footer;
    }

    // ----------------------------------------------------------------- rows

    VisualElement MakeRow() {
      var row = new VisualElement();
      row.AddToClassList("haste-row");

      var tag = new Label();
      tag.AddToClassList("haste-tag");
      tag.name = "tag";
      row.Add(tag);

      var name = new Label();
      name.AddToClassList("haste-name");
      name.name = "name";
      row.Add(name);

      var spacer = new VisualElement();
      spacer.AddToClassList("haste-spacer");
      row.Add(spacer);

      var path = new Label();
      path.AddToClassList("haste-path");
      path.name = "path";
      row.Add(path);

      var dot = new VisualElement();
      dot.AddToClassList("haste-dot");
      dot.name = "dot";
      row.Add(dot);

      row.RegisterCallback<MouseDownEvent>(evt => {
        var index = (int)row.userData;
        if (evt.actionKey) {
          ToggleMultiSelection(index);
        } else if (evt.clickCount >= 2) {
          Act(index);
        } else {
          SetHighlighted(index, true);
        }
        evt.StopPropagation();
      });

      return row;
    }

    void BindRow(VisualElement row, int index) {
      if (index < 0 || index >= results.Length) {
        return;
      }

      var result = results[index];
      var item = result.Item;
      row.userData = index;

      var name = HasteStringUtils.GetFileName(item.path);
      var directory = HasteStringUtils.GetDirectory(item.path);

      var nameLabel = row.Q<Label>("name");
      nameLabel.text = HasteStringUtils.BoldLabel(
        name, HasteStringUtils.GetHighlightIndices(name, result.Terms),
        NameHighlightStart, HighlightEnd);

      var pathLabel = row.Q<Label>("path");
      pathLabel.text = HasteStringUtils.BoldLabel(
        directory, HasteStringUtils.GetHighlightIndices(directory, result.Terms),
        PathHighlightStart, HighlightEnd);

      row.Q<Label>("tag").text = HasteKinds.Tag(item);

      // Hierarchy rows keep the editor's own colour coding: prefab blue, broken-prefab
      // red, dimmed when inactive. The design shows one row treatment; these are derived.
      nameLabel.EnableInClassList("haste-name--prefab", false);
      nameLabel.EnableInClassList("haste-name--broken", false);
      nameLabel.EnableInClassList("haste-name--disabled", false);

      var go = result.Object as GameObject;
      if (go != null) {
        switch (HasteHierarchyResult.ClassifyPrefab(go)) {
          case HasteHierarchyResult.PrefabDisplay.Prefab:
            nameLabel.AddToClassList("haste-name--prefab");
            break;
          case HasteHierarchyResult.PrefabDisplay.BrokenPrefab:
            nameLabel.AddToClassList("haste-name--broken");
            break;
        }
        if (!go.activeInHierarchy) {
          nameLabel.AddToClassList("haste-name--disabled");
        }
      }

      row.EnableInClassList("haste-row--highlighted", index == highlighted);

      var obj = result.Object;
      row.Q("dot").style.display =
        obj != null && multiSelection.Contains(obj) ? DisplayStyle.Flex : DisplayStyle.None;
    }

    // ------------------------------------------------------------- querying

    void FocusQuery() {
      if (queryField == null) {
        return;
      }
      EditorApplication.delayCall += () => {
        if (queryField != null) {
          queryField.Focus();
        }
      };
    }

    void OnQueryChanged(string value) {
      // A scope token is peeled off the front as soon as it is complete, so the chip
      // appears mid-typing exactly as the design shows.
      if (scopeKinds == HasteKind.Any) {
        HasteKind kinds;
        string token;
        var rest = HasteKinds.SplitScope(value, out kinds, out token);
        if (kinds != HasteKind.Any) {
          scopeKinds = kinds;
          scopeToken = token;
          queryField.SetValueWithoutNotify(rest);
          value = rest;
          SyncScopeChrome();
        }
      }

      query = value;
      Research();
    }

    void ClearScope() {
      if (scopeKinds == HasteKind.Any) {
        return;
      }
      scopeKinds = HasteKind.Any;
      scopeToken = null;
      SyncScopeChrome();
      Research();
    }

    void SyncScopeChrome() {
      var scoped = scopeKinds != HasteKind.Any;
      scopeChip.style.display = scoped ? DisplayStyle.Flex : DisplayStyle.None;
      hintsRow.style.display = scoped ? DisplayStyle.None : DisplayStyle.Flex;
      if (scoped) {
        scopeChipLabel.text = scopeToken;
      }
    }

    void Research() {
      if (searching != null) {
        searching.Stop();
      }

      if (string.IsNullOrEmpty(query.Trim())) {
        RestoreRecommendations();
        return;
      }

      searching = Haste.Scheduler.Start(SearchRoutine(query, scopeKinds));
    }

    IEnumerator SearchRoutine(string q, HasteKind kinds) {
      var promise = new Promise<IHasteResult[]>();
      yield return Haste.Scheduler.Start(Haste.Search.Search(q, kinds, ResultCount, promise));

      if (promise.Value != null) {
        SetResults(promise.Value);
      }
    }

    void RestoreRecommendations() {
      var recommendations = HasteRecommendations.instance.Get();
      SetResults(recommendations ?? new IHasteResult[0]);
    }

    void SetResults(IHasteResult[] next) {
      results = next ?? new IHasteResult[0];
      highlighted = results.Length > 0 ? 0 : -1;

      listView.itemsSource = results;
      listView.Rebuild();
      listView.SetSelectionWithoutNotify(highlighted >= 0 ? new[] { highlighted } : new int[0]);

      SyncEmptyState();
      SyncStatus();
    }

    void SyncEmptyState() {
      var empty = results.Length == 0;
      listView.style.display = empty ? DisplayStyle.None : DisplayStyle.Flex;
      messageView.style.display = empty ? DisplayStyle.Flex : DisplayStyle.None;

      if (!empty) {
        return;
      }

      if (string.IsNullOrEmpty(query.Trim())) {
        messageTitle.text = "Type to search";
        messageHint.text = HasteShortcutLabel();
      } else {
        messageTitle.text = scopeKinds == HasteKind.Any
          ? "No matches"
          : "No matches in this scope";
        messageHint.text = scopeKinds == HasteKind.Any
          ? "Try fewer characters"
          : "Backspace clears the scope";
      }
    }

    static string HasteShortcutLabel() {
      return Application.platform == RuntimePlatform.OSXEditor
        ? "⌘⇧K to reopen"
        : "Ctrl+Shift+K to reopen";
    }

    void SyncStatus() {
      var indexing = Haste.IsIndexing;
      footerIcon.EnableInClassList("haste-footer-icon--indexing", indexing);

      if (indexing) {
        statusLabel.text = "Indexing " + Haste.IndexedCount.ToString("N0") + " items…";
      } else if (multiSelection.Count > 0) {
        statusLabel.text = multiSelection.Count + " selected · ↵ to confirm";
      } else if (results.Length > 0) {
        statusLabel.text = results.Length + " results · ↑↓ to move";
      } else {
        statusLabel.text = HasteTips.Random;
      }

      countLabel.style.display = multiSelection.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
      countLabel.text = multiSelection.Count.ToString();
    }

    // ------------------------------------------------------------ selection

    void SetHighlighted(int index, bool syncListView) {
      if (results.Length == 0) {
        return;
      }

      highlighted = Mathf.Clamp(index, 0, results.Length - 1);

      if (syncListView) {
        listView.SetSelectionWithoutNotify(new[] { highlighted });
      }
      listView.ScrollToItem(highlighted);
      listView.RefreshItems();

      // "Soft" selection: browsing results previews them in the editor. Switchable off,
      // because it expands Hierarchy and Project folders as it goes.
      if (HasteSettings.SelectEnabled) {
        results[highlighted].Select();
      }
    }

    void Move(int delta) {
      if (results.Length == 0) {
        return;
      }
      var next = highlighted + delta;
      // Wrap, as the design's own arrow handling does.
      next = ((next % results.Length) + results.Length) % results.Length;
      SetHighlighted(next, true);
    }

    void ToggleMultiSelection(int index) {
      if (index < 0 || index >= results.Length) {
        return;
      }

      var obj = results[index].Object;
      if (obj == null) {
        return;
      }

      if (!multiSelection.Remove(obj)) {
        multiSelection.Add(obj);
      }

      Selection.objects = multiSelection.ToArray();
      listView.RefreshItems();
      SyncStatus();
    }

    void Act(int index) {
      if (index < 0 || index >= results.Length) {
        return;
      }

      if (multiSelection.Count > 0) {
        Selection.objects = multiSelection.ToArray();
        Close();
        return;
      }

      var result = results[index];
      HasteRecommendations.instance.Add(result.Item);

      Selection.objects = prevSelection;

      // Deferred until after the window is gone: acting can change layouts and other
      // editor state that Unity does not like being changed while a window is open.
      Haste.WindowAction += result.Action;
      Close();
    }

    void Dismiss() {
      Selection.objects = prevSelection;
      Close();
    }

    // ------------------------------------------------------------- keyboard

    void OnKeyDown(KeyDownEvent evt) {
      switch (evt.keyCode) {
        case KeyCode.Escape:
          Dismiss();
          evt.StopPropagation();
          break;

        case KeyCode.Return:
        case KeyCode.KeypadEnter:
          if (evt.actionKey) {
            ToggleMultiSelection(highlighted);
          } else {
            Act(highlighted);
          }
          evt.StopPropagation();
          break;

        case KeyCode.UpArrow:    Move(-1); evt.StopPropagation(); break;
        case KeyCode.DownArrow:  Move(1);  evt.StopPropagation(); break;
        case KeyCode.Home:       SetHighlighted(0, true); evt.StopPropagation(); break;
        case KeyCode.End:        SetHighlighted(results.Length - 1, true); evt.StopPropagation(); break;
        case KeyCode.PageUp:     Move(-VisibleRows()); evt.StopPropagation(); break;
        case KeyCode.PageDown:   Move(VisibleRows()); evt.StopPropagation(); break;

        case KeyCode.Backspace:
          // Only when there is nothing left to delete, so backspace still edits text.
          if (scopeKinds != HasteKind.Any && string.IsNullOrEmpty(query)) {
            ClearScope();
            evt.StopPropagation();
          }
          break;
      }
    }

    int VisibleRows() {
      return Mathf.Max(1, Mathf.FloorToInt(listView.resolvedStyle.height / RowHeight));
    }

    // --------------------------------------------------------------- update

    void Update() {
      if (wasIndexing != Haste.IsIndexing) {
        wasIndexing = Haste.IsIndexing;
        SyncStatus();
      }

      // Close on focus loss. OnLostFocus is avoided here for the same reason the IMGUI
      // window avoided it: it fires during layout in cases that leave the palette drawn
      // but dead.
      if (this != focusedWindow) {
        Dismiss();
      }
    }
  }
}
