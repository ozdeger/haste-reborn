using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NUnit.Framework;

namespace Haste {

  // Characterization tests. Every expected value here was generated from the code as it
  // behaved on Unity 6000.0.80f1 immediately after phase 0 of the revival, BEFORE any
  // subsystem was rewritten. They exist to make behaviour changes loud: the search
  // ranking, the fuzzy-match highlighting and the scoring ladder are the product, and a
  // rewrite that silently reorders results is a regression even if it compiles and looks
  // fine.
  //
  // Cases marked "BUG" record behaviour that is wrong but real. Fixing one of those is a
  // deliberate act that should update the expectation in the same commit.
  //
  // RE-BASELINED by the recall fix (HasteIndex now buckets by path character rather than
  // by word boundary). The shape of that diff is worth knowing: every result that ranked
  // before the fix kept its exact score and its exact position, because an item could
  // only rank at all if the query's first character began one of its words -- precisely
  // the items INTERIOR_START_DAMPING leaves untouched. What changed is additive:
  // interior matches now appear beneath the acronym matches, and queries of three
  // characters or more pick up the new substring rungs.
  [TestFixture]
  internal class HasteCharacterizationTests {

    // A deliberately mixed corpus: Unity menu-item paths, project asset paths and bare
    // scene-object paths, because all three flow through the same index.
    static readonly string[] Corpus = new string[] {
      "GameObject/Create Empty",
      "GameObject/Create Empty Child",
      "GameObject/Align With View",
      "Component/Physics/Mesh Collider",
      "Component/Physics/Cloth Renderer",
      "Component/Physics 2D/Polygon Collider 2D",
      "Component/Effects/Halo",
      "Component/Add...",
      "Component/Layout/Canvas",
      "File/Build Settings...",
      "Window/Hierarchy",
      "Assets/Create/Lens Flare",
      "Assets/Scripts/PlayerController.cs",
      "Assets/Scripts/Player/PlayerMovement.cs",
      "Assets/Materials/MainCamera.mat",
      "Assets/Prefabs/Main Camera.prefab",
      "Assets/Haste/Editor/HasteWindow.cs",
      "Main Camera",
      "Directional Light",
      "Player/Model/Mesh",
    };

    static HasteSearch NewSearch() {
      var index = new HasteIndex();
      foreach (var path in Corpus) {
        index.Add(new HasteItem(path, 0, ""));
      }
      return new HasteSearch(index);
    }

    static IHasteResult[] Search(string query, int count = 100) {
      var promise = new Promise<IHasteResult[]>();
      HasteScheduler.Sync(NewSearch().Search(query, count, promise));
      return promise.Value ?? new IHasteResult[0];
    }

    // Asserts the full ordered result list for a query. Each expected entry is
    // "score|path" so ordering AND scoring are pinned by one table.
    static void AssertRanking(string query, params string[] expected) {
      var results = Search(query);
      var actual = results
        .Select(r => r.Score.ToString("0.####", CultureInfo.InvariantCulture) + "|" + r.Item.path)
        .ToArray();

      Assert.That(actual, Is.EqualTo(expected),
        "ranking for query \"" + query + "\" changed.\nexpected:\n  " +
        string.Join("\n  ", expected) + "\nactual:\n  " + string.Join("\n  ", actual));
    }

    // ---------------------------------------------------------------- ranking

    [Test]
    [Category("Ranking")]
    public void Ranking_Acronym_mc() {
      // "mc" -> "Main Camera": the headline fuzzy-matching behaviour.
      AssertRanking("mc",
        "100|Main Camera",
        "80|Component/Physics/Mesh Collider",
        "73.3333|Assets/Materials/MainCamera.mat",
        "73.3333|Assets/Prefabs/Main Camera.prefab",
        "51.4286|Assets/Scripts/Player/PlayerMovement.cs",
        // Everything below here was unreachable before the recall fix: no word in any of
        // these items starts with 'm', so the boundary-keyed index never offered them to
        // the scorer at all. They are damped to half their acronym score and sit beneath
        // every genuine "mc" match.
        "16.6667|Component/Effects/Halo",
        "16.6667|Component/Layout/Canvas",
        "15|GameObject/Create Empty",
        "15|Component/Physics/Cloth Renderer",
        "14|GameObject/Create Empty Child",
        "12.5|Component/Physics 2D/Polygon Collider 2D");
    }

    [Test]
    [Category("Ranking")]
    public void Ranking_Acronym_ce() {
      AssertRanking("ce",
        "80|GameObject/Create Empty",
        "76.6667|Component/Effects/Halo",
        "76|GameObject/Create Empty Child",
        "53.3333|Component/Layout/Canvas",
        "50|Component/Physics/Cloth Renderer",
        "40|Main Camera",
        "40|Component/Physics/Mesh Collider",
        "38|Component/Add...",
        "35|Component/Physics 2D/Polygon Collider 2D",
        "30|Assets/Create/Lens Flare",
        "26.6667|Assets/Materials/MainCamera.mat",
        "26.6667|Assets/Prefabs/Main Camera.prefab",
        "26.6667|Assets/Scripts/PlayerController.cs",
        "25.7143|Assets/Scripts/Player/PlayerMovement.cs");
    }

    [Test]
    [Category("Ranking")]
    public void Ranking_Acronym_cec() {
      AssertRanking("cec",
        "84|GameObject/Create Empty Child",
        "73.3333|Component/Layout/Canvas",
        "66.6667|Component/Physics/Cloth Renderer",
        "63.3333|Component/Effects/Halo",
        "56.6667|Component/Physics/Mesh Collider",
        "46.6667|Component/Physics 2D/Polygon Collider 2D",
        "40|Assets/Scripts/PlayerController.cs",
        "19.0476|Assets/Scripts/Player/PlayerMovement.cs");
    }

    [Test]
    [Category("Ranking")]
    public void Ranking_Acronym_pc() {
      AssertRanking("pc",
        "73.3333|Assets/Scripts/PlayerController.cs",
        "71.4286|Assets/Scripts/Player/PlayerMovement.cs",
        "70|Component/Physics 2D/Polygon Collider 2D",
        "60|Component/Physics/Mesh Collider",
        "60|Component/Physics/Cloth Renderer",
        "53.3333|Assets/Prefabs/Main Camera.prefab",
        // Newly reachable interior matches, damped.
        "16.6667|Component/Effects/Halo",
        "16.6667|Component/Layout/Canvas",
        "14|GameObject/Create Empty Child");
    }

    [Test]
    [Category("Ranking")]
    public void Ranking_ByExtension_dotCs() {
      // Searching ".cs" to filter by asset type is a documented feature.
      // Each of these gained a flat +15 from the new path-substring rung: ".cs" occurs
      // literally in the path, a far stronger signal than boundary overlap alone, and it
      // previously scored nothing. Relative order is unchanged.
      AssertRanking(".cs",
        "55|Assets/Scripts/PlayerController.cs",
        "53.0952|Assets/Haste/Editor/HasteWindow.cs",
        "53.0952|Assets/Scripts/Player/PlayerMovement.cs");
    }

    [Test]
    [Category("Ranking")]
    public void Ranking_MenuPathAcronym_gce() {
      AssertRanking("gce",
        "80|GameObject/Create Empty",
        "74|GameObject/Create Empty Child",
        "31.3333|GameObject/Align With View",
        // Newly reachable: no word in "Polygon Collider 2D" starts with 'g'.
        "9.1667|Component/Physics 2D/Polygon Collider 2D");
    }

    [Test]
    [Category("Ranking")]
    public void Ranking_PartialWordThenAcronym_mainc() {
      AssertRanking("mainc",
        "76|Main Camera",
        "69.3333|Assets/Materials/MainCamera.mat",
        "49.3333|Assets/Prefabs/Main Camera.prefab");
    }

    [Test]
    [Category("Ranking")]
    public void Ranking_Word_player() {
      AssertRanking("player",
        "53.3333|Assets/Scripts/PlayerController.cs",
        "52.381|Assets/Scripts/Player/PlayerMovement.cs",
        "50|Player/Model/Mesh");
    }

    [Test]
    [Category("Ranking")]
    public void Ranking_NoMatch_ReturnsEmpty() {
      Assert.That(Search("z"), Is.Empty);
      Assert.That(Search("qqq"), Is.Empty);
      Assert.That(Search(""), Is.Empty);
    }

    [Test]
    [Category("Ranking")]
    public void Ranking_RespectsCountLimit() {
      // A 15th item matches "ce" as a scattered subsequence but scores 0, and zero-score
      // results are dropped rather than padding the tail. See HasteSearch.Map.
      Assert.That(Search("ce").Length, Is.EqualTo(14));
      Assert.That(Search("ce", 3).Length, Is.EqualTo(3));
      Assert.That(Search("ce", 1).Length, Is.EqualTo(1));
      // Truncation keeps the best-scoring results, not an arbitrary slice.
      Assert.That(Search("ce", 1)[0].Item.path, Is.EqualTo("GameObject/Create Empty"));
    }

    // ------------------------------------------------- recall: interior matches

    [Test]
    [Category("Ranking")]
    public void Index_FindsInteriorMatchesThatUsedToBeUnreachable() {
      // THE RECALL FIX. The index used to bucket by word-boundary character, so an item
      // was never even offered to the scorer unless the query's FIRST character began one
      // of its words. "Collider" and "Physics" were both indexed, yet both of these
      // queries returned nothing at all -- the single biggest recall limitation the tool
      // had. They now return the obvious answers.
      AssertRanking("ollider",
        "30.3571|Component/Physics 2D/Polygon Collider 2D",
        "25|Component/Physics/Mesh Collider");
    }

    [Test]
    [Category("Ranking")]
    public void Index_FindsInteriorMatchesThatUsedToBeUnreachable_ysics() {
      // The other half of the pair above, in its own test so a failure in one still
      // reports the other.
      AssertRanking("ysics",
        "24|Component/Physics/Mesh Collider",
        "24|Component/Physics/Cloth Renderer",
        "21.5|Component/Physics 2D/Polygon Collider 2D");
    }

    [Test]
    [Category("Ranking")]
    public void Ranking_AcronymMatchesStillOutrankInteriorMatches() {
      // The point of damping rather than dropping the boundary signal: widening the index
      // must not cost the acronym behaviour that is the reason to use Haste. For "mc",
      // every item whose words actually start with 'm' and 'c' still ranks above every
      // item that merely contains those letters mid-word.
      var results = Search("mc");
      var boundaryFirst = results
        .TakeWhile(r => r.Item.boundariesLower.IndexOf('m') != -1)
        .Select(r => r.Item.path).ToArray();

      Assert.That(boundaryFirst, Is.EqualTo(new[] {
        "Main Camera",
        "Component/Physics/Mesh Collider",
        "Assets/Materials/MainCamera.mat",
        "Assets/Prefabs/Main Camera.prefab",
        "Assets/Scripts/Player/PlayerMovement.cs",
      }), "an interior match broke into the acronym block");

      // And none of the interior matches that follow scores anywhere near them.
      var interior = results.Skip(boundaryFirst.Length).ToArray();
      Assert.That(interior, Is.Not.Empty);
      Assert.That(interior.Max(r => r.Score), Is.LessThan(results[boundaryFirst.Length - 1].Score));
    }

    [Test]
    [Category("Scoring")]
    public void Score_InteriorStartIsDampedNotDropped() {
      // "GameObject/Create Empty" has boundaries "goce" -- no 'm' -- so before the fix it
      // was unreachable for "mc" no matter how it scored. It is now scored, at exactly
      // INTERIOR_START_DAMPING of the acronym score it would otherwise have earned.
      var item = new HasteItem("GameObject/Create Empty", 0, "");
      Assert.That(item.boundariesLower, Is.EqualTo("goce"));
      Assert.That(HasteScoring.Score(item, "mc", 2), Is.EqualTo(30.0f * HasteScoring.INTERIOR_START_DAMPING).Within(0.001f));
      Assert.That(Search("mc").Any(r => r.Item.path == "GameObject/Create Empty"), Is.True);
    }

    [Test]
    [Category("Scoring")]
    public void Score_DampingAppliesToTheAcronymTermOnlyNotTheLadder() {
      // A literal substring is a deliberate, high-confidence signal and must survive
      // undamped, or a weak boundary-first match elsewhere outranks the thing the user
      // typed. "Mesh Collider" shares no boundary character at all with "ollider", so its
      // whole score is the ladder's substring rung, unhalved.
      var mesh = new HasteItem("Component/Physics/Mesh Collider", 0, "");
      Assert.That(HasteStringUtils.LongestCommonSubsequenceLength("ollider", mesh.boundariesLower), Is.EqualTo(0));
      Assert.That(HasteScoring.Score(mesh, "ollider", 7), Is.EqualTo(25.0f).Within(0.001f));
    }

    [Test]
    [Category("Ranking")]
    public void Ranking_ZeroScoringSubsequenceNoiseIsDropped() {
      // "GameObject/Align With View" contains a 'c' then an 'e' (in "obje-c-t"), so it is
      // a real subsequence match for "ce" and the widened index does reach it. It shares
      // no boundary character with the query, contains no substring, and begins with
      // neither -- it scores exactly 0, and carries no signal. HasteSearch.Map drops it.
      var noise = new HasteItem("GameObject/Align With View", 0, "");
      Assert.That(HasteStringUtils.ContainsSubsequence(noise.pathLower, "ce", noise.pathLower.Length, 2), Is.True);
      Assert.That(HasteScoring.Score(noise, "ce", 2), Is.EqualTo(0.0f).Within(0.001f));
      Assert.That(Search("ce").Any(r => r.Item.path == "GameObject/Align With View"), Is.False);
    }

    [Test]
    [Category("Ranking")]
    public void Ranking_InteriorFirstCharacterStillNeedsTheRestToMatch() {
      // Widening the bucket widens candidates, not truth: the subsequence walk still has
      // the final say, so a query whose characters are not all present in order matches
      // nothing.
      Assert.That(Search("z"), Is.Empty);
      Assert.That(Search("qqq"), Is.Empty);

      // It is no longer only the FIRST character that may be interior; all of them may.
      // This query used to return exactly the two camera assets, and only because 'a'
      // happens to begin "Assets".
      AssertRanking("amera",
        "54.3333|Assets/Materials/MainCamera.mat",
        "54.3333|Assets/Prefabs/Main Camera.prefab",
        // "Main Camera" is the recall fix in miniature: 'a' begins no word in it, so
        // this -- the one object actually named that -- did not appear at all before.
        "39|Main Camera",
        // A weak tail remains. These share only the boundary 'e' and score accordingly;
        // they are ranked far below, which is the job INTERIOR_START_DAMPING does.
        "9|GameObject/Create Empty",
        "8|GameObject/Create Empty Child");
    }

    // ----------------------------------------------------------------- scoring

    [Test]
    [Category("Scoring")]
    public void Score_ExactMatchesOutrankAcronyms() {
      // Exact name match adds 60 on top of the boundary component and short-circuits.
      var camera = new HasteItem("Main Camera", 0, "");
      Assert.That(HasteScoring.Score(camera, "main camera", 11), Is.EqualTo(107.2727f).Within(0.001f));

      // Exact full-path match adds 50.
      var mesh = new HasteItem("Component/Physics/Mesh Collider", 0, "");
      Assert.That(HasteScoring.Score(mesh, "component/physics/mesh collider", 31), Is.EqualTo(95.1613f).Within(0.001f));
      // Prefix-of-name match adds 40.
      Assert.That(HasteScoring.Score(mesh, "mesh collider", 13), Is.EqualTo(86.1539f).Within(0.001f));

      // A two-boundary path fully consumed by a two-character acronym is the ideal case:
      // both the query ratio and the boundary utilization saturate.
      var light = new HasteItem("Directional Light", 0, "");
      Assert.That(HasteScoring.Score(light, "dl", 2), Is.EqualTo(100.0f).Within(0.001f));
      var window = new HasteItem("Window/Hierarchy", 0, "");
      Assert.That(HasteScoring.Score(window, "wh", 2), Is.EqualTo(90.0f).Within(0.001f));
    }

    [Test]
    [Category("Scoring")]
    public void Score_LadderValues() {
      var mesh = new HasteItem("Component/Physics/Mesh Collider", 0, "");
      Assert.That(HasteScoring.Score(mesh, "mc", 2), Is.EqualTo(80.0f).Within(0.001f));
      Assert.That(HasteScoring.Score(mesh, "pc", 2), Is.EqualTo(60.0f).Within(0.001f));
      Assert.That(HasteScoring.Score(mesh, "cec", 3), Is.EqualTo(56.6667f).Within(0.001f));

      var camera = new HasteItem("Main Camera", 0, "");
      Assert.That(HasteScoring.Score(camera, "mc", 2), Is.EqualTo(100.0f).Within(0.001f));
      Assert.That(HasteScoring.Score(camera, "ce", 2), Is.EqualTo(40.0f).Within(0.001f));

      var controller = new HasteItem("Assets/Scripts/PlayerController.cs", 0, "");
      Assert.That(HasteScoring.Score(controller, "pc", 2), Is.EqualTo(73.3333f).Within(0.001f));
      // 40 from the boundary terms, plus the new +15 path-substring rung.
      Assert.That(HasteScoring.Score(controller, ".cs", 3), Is.EqualTo(55.0f).Within(0.001f));

      // No shared characters at all still yields zero, not a negative or a throw.
      var light = new HasteItem("Directional Light", 0, "");
      Assert.That(HasteScoring.Score(light, "mc", 2), Is.EqualTo(0.0f).Within(0.001f));
    }

    [Test]
    [Category("Scoring")]
    public void Score_ItemWithAnEmptyNameDoesNotThrow() {
      // A path that is nothing but an extension yields an empty name from
      // GetFileNameWithoutExtension, and the first-character rungs used to index into it
      // unguarded. A GameObject named ".x" in the hierarchy is enough to reach this.
      var item = new HasteItem(".x", 0, "");
      Assert.That(item.nameLower, Is.EqualTo(""));
      Assert.That(item.boundariesLower, Is.EqualTo("x"));
      Assert.That(() => HasteScoring.Score(item, "x", 1), Throws.Nothing);
      Assert.That(HasteScoring.Score(item, "x", 1), Is.EqualTo(80.0f).Within(0.001f));
    }

    [Test]
    [Category("Scoring")]
    public void Score_UserScoreMultipliesRecency() {
      // Recency is a multiplier of (1 + userScore/10) applied to the whole score. Before
      // phase 0 this was compiled out of the free edition entirely.
      var item = new HasteItem("Component/Physics/Mesh Collider", 0, "");
      Assert.That(item.userScore, Is.EqualTo(0.0f));
      Assert.That(HasteScoring.Score(item, "mc", 2), Is.EqualTo(80.0f).Within(0.001f));

      item.userScore = 1.0f;
      Assert.That(HasteScoring.Score(item, "mc", 2), Is.EqualTo(88.0f).Within(0.001f));

      item.userScore = 10.0f;
      Assert.That(HasteScoring.Score(item, "mc", 2), Is.EqualTo(160.0f).Within(0.001f));
    }

    [Test]
    [Category("Scoring")]
    public void Comparer_OrdersByScoreThenPathLengthThenNaturally() {
      // Equal scores fall back to shorter path, then to natural ordering.
      var shortItem = new HasteItem("Component/Physics/Mesh Collider", 0, "");
      var longItem = new HasteItem("Component/Physics/Cloth Renderer Extra", 0, "");
      var a = new HasteResult(shortItem, 50.0f, "mc");
      var b = new HasteResult(longItem, 50.0f, "mc");
      Assert.That(a.CompareTo(b), Is.EqualTo(-1));
      Assert.That(b.CompareTo(a), Is.EqualTo(1));

      var higher = new HasteResult(longItem, 60.0f, "mc");
      Assert.That(higher.CompareTo(a), Is.EqualTo(-1));

      // Equal score and equal length falls through to natural ordering of the path.
      var aaa = new HasteResult(new HasteItem("Component/Physics/AAA", 0, ""), 50.0f, "");
      var bbb = new HasteResult(new HasteItem("Component/Physics/BBB", 0, ""), 50.0f, "");
      Assert.That(aaa.CompareTo(bbb), Is.EqualTo(-1));
    }

    // -------------------------------------------------------------- primitives

    [Test]
    [Category("Primitives")]
    public void Boundaries() {
      Assert.That(HasteStringUtils.GetBoundaries("Yak"), Is.EqualTo("y"));
      Assert.That(HasteStringUtils.GetBoundaries("LlamaCrab"), Is.EqualTo("lc"));
      Assert.That(HasteStringUtils.GetBoundaries("ShrewRail/Wren"), Is.EqualTo("srw"));
      Assert.That(HasteStringUtils.GetBoundaries("Main Camera"), Is.EqualTo("mc"));
      Assert.That(HasteStringUtils.GetBoundaries("Component/Physics 2D/Polygon Collider 2D"), Is.EqualTo("cp2dpc2d"));
      Assert.That(HasteStringUtils.GetBoundaries("Assets/Scripts/PlayerController.cs"), Is.EqualTo("aspc.c"));
      // Punctuation and underscores open a new word; digits count as word characters.
      Assert.That(HasteStringUtils.GetBoundaries("my-file_name (1).prefab"), Is.EqualTo("mfn1.p"));
      Assert.That(HasteStringUtils.GetBoundaries("A1B2c3"), Is.EqualTo("ab"));
      // Runs of capitals yield only the first letter, so acronyms don't explode.
      Assert.That(HasteStringUtils.GetBoundaries("ALLCAPS/lowercase"), Is.EqualTo("al"));
      Assert.That(HasteStringUtils.GetBoundaries(""), Is.EqualTo(""));
      Assert.That(HasteStringUtils.GetBoundaries("/"), Is.EqualTo(""));
      Assert.That(HasteStringUtils.GetBoundaries("."), Is.EqualTo(""));
    }

    [Test]
    [Category("Primitives")]
    public void BoundaryIndices() {
      Assert.That(HasteStringUtils.GetBoundaryIndices("Yak"), Is.EqualTo(new[] { 0 }));
      Assert.That(HasteStringUtils.GetBoundaryIndices("LlamaCrab"), Is.EqualTo(new[] { 0, 5 }));
      Assert.That(HasteStringUtils.GetBoundaryIndices("ShrewRail/Wren"), Is.EqualTo(new[] { 0, 5, 10 }));
      Assert.That(HasteStringUtils.GetBoundaryIndices("Component/Physics 2D/Polygon Collider 2D"),
        Is.EqualTo(new[] { 0, 10, 18, 19, 21, 29, 38, 39 }));
      Assert.That(HasteStringUtils.GetBoundaryIndices("Assets/Scripts/PlayerController.cs"),
        Is.EqualTo(new[] { 0, 7, 15, 21, 31, 32 }));
      Assert.That(HasteStringUtils.GetBoundaryIndices(""), Is.Empty);
    }

    [Test]
    [Category("Primitives")]
    public void FileNameAndExtension() {
      Assert.That(HasteStringUtils.GetFileName("Assets/Scripts/PlayerController.cs"), Is.EqualTo("PlayerController.cs"));
      Assert.That(HasteStringUtils.GetFileNameWithoutExtension("Assets/Scripts/PlayerController.cs"), Is.EqualTo("PlayerController"));
      Assert.That(HasteStringUtils.GetExtension("Assets/Scripts/PlayerController.cs"), Is.EqualTo("cs"));

      Assert.That(HasteStringUtils.GetFileName("trailing/"), Is.EqualTo("trailing"));
      Assert.That(HasteStringUtils.GetExtension("no-extension"), Is.EqualTo(""));
      Assert.That(HasteStringUtils.GetExtension("Main Camera"), Is.EqualTo(""));
      Assert.That(HasteStringUtils.GetFileNameWithoutExtension("my-file_name (1).prefab"), Is.EqualTo("my-file_name (1)"));
      Assert.That(HasteStringUtils.GetExtension("my-file_name (1).prefab"), Is.EqualTo("prefab"));
    }

    [Test]
    [Category("Primitives")]
    public void FileNameAndExtension_TrailingEllipsisIsAnEllipsisNotAnExtension() {
      // FIXED. A menu path ending in "..." used to have its last two dots eaten from the
      // name ("Add..") and the remainder reported as an extension ("..").
      //
      // The blast radius was narrower than it looks, and worth recording so nobody hunts
      // for symptoms that were never there: the result row renders GetFileName, which was
      // always correct, and the extension HasteItem used to derive was read by nothing. What the
      // wrong name actually cost was scoring -- the exact-name, prefix-name and
      // substring-name rungs all compare against it, so "Component/Add..." could not be
      // matched by typing its own name.
      Assert.That(HasteStringUtils.GetFileNameWithoutExtension("Component/Add..."), Is.EqualTo("Add..."));
      Assert.That(HasteStringUtils.GetExtension("Component/Add..."), Is.EqualTo(""));
      Assert.That(HasteStringUtils.GetFileNameWithoutExtension("File/Build Settings..."), Is.EqualTo("Build Settings..."));
      Assert.That(HasteStringUtils.GetExtension("File/Build Settings..."), Is.EqualTo(""));

      // Real extensions are untouched, including one that follows a dotted stem.
      Assert.That(HasteStringUtils.GetFileNameWithoutExtension("Assets/Some.Thing.cs"), Is.EqualTo("Some.Thing"));
      Assert.That(HasteStringUtils.GetExtension("Assets/Some.Thing.cs"), Is.EqualTo("Thing.cs"));

      // A name that is only dots has no name and no extension, and does not throw.
      Assert.That(HasteStringUtils.GetExtension("Menu/..."), Is.EqualTo(""));
      Assert.That(HasteStringUtils.GetFileNameWithoutExtension("Menu/..."), Is.EqualTo("..."));

      // Typing the menu item's own name now reaches the exact-name rung.
      var add = new HasteItem("Component/Add...", 0, "");
      Assert.That(add.nameLower, Is.EqualTo("add..."));
      Assert.That(HasteScoring.Score(add, "add...", 6), Is.GreaterThan(HasteScoring.Score(add, "add..", 5)));

      // The same path's boundaries and full file name are unaffected.
      Assert.That(HasteStringUtils.GetFileName("Component/Add..."), Is.EqualTo("Add..."));
      Assert.That(HasteStringUtils.GetBoundaries("Component/Add..."), Is.EqualTo("ca..."));
    }

    [Test]
    [Category("Primitives")]
    public void Subsequence_IgnoresOrderOfTypingButNotOfCharacters() {
      Assert.That(HasteStringUtils.ContainsSubsequence("main camera", "mc", 11, 2), Is.True);
      // 'c' then 'm' also matches: "main Camera" has an 'm' after the 'c'.
      Assert.That(HasteStringUtils.ContainsSubsequence("main camera", "cm", 11, 2), Is.True);
      Assert.That(HasteStringUtils.ContainsSubsequence("main camera", "mainc", 11, 5), Is.True);
      Assert.That(HasteStringUtils.ContainsSubsequence("abc", "abc", 3, 3), Is.True);
      Assert.That(HasteStringUtils.ContainsSubsequence("abc", "abcd", 3, 4), Is.False);
      Assert.That(HasteStringUtils.ContainsSubsequence("abc", "", 3, 0), Is.True);
      Assert.That(HasteStringUtils.ContainsSubsequence("assets/scripts/playercontroller.cs", ".cs", 34, 3), Is.True);
    }

    [Test]
    [Category("Primitives")]
    public void LetterBitset_IsALossyPrefilter() {
      // The bitset is (1 << c) with the shift wrapping mod 32, so it is a cheap reject
      // filter with real collisions -- two different paths can share a bitset.
      var mesh = new HasteItem("Component/Physics/Mesh Collider", 0, "");
      var cloth = new HasteItem("Component/Physics/Cloth Renderer", 0, "");
      Assert.That(mesh.bitset, Is.EqualTo(cloth.bitset));

      Assert.That(HasteStringUtils.LetterBitsetFromString(""), Is.EqualTo(0));
      Assert.That(HasteStringUtils.LetterBitsetFromString("main camera"), Is.EqualTo(287275));

      // Containment is one-directional: query bits must be a subset of the item's bits.
      int item = HasteStringUtils.LetterBitsetFromString("main camera");
      Assert.That(HasteStringUtils.ContainsChars(item, HasteStringUtils.LetterBitsetFromString("mc")), Is.True);
      Assert.That(HasteStringUtils.ContainsChars(item, HasteStringUtils.LetterBitsetFromString("z")), Is.False);
    }

    [Test]
    [Category("Primitives")]
    public void LongestCommonSubsequenceLength() {
      Assert.That(HasteStringUtils.LongestCommonSubsequenceLength("mc", "gce"), Is.EqualTo(1));
      Assert.That(HasteStringUtils.LongestCommonSubsequenceLength("mc", "cpmc"), Is.EqualTo(2));
      Assert.That(HasteStringUtils.LongestCommonSubsequenceLength("abc", "abc"), Is.EqualTo(3));
      Assert.That(HasteStringUtils.LongestCommonSubsequenceLength("abc", "cba"), Is.EqualTo(1));
      Assert.That(HasteStringUtils.LongestCommonSubsequenceLength("", "abc"), Is.EqualTo(0));
      Assert.That(HasteStringUtils.LongestCommonSubsequenceLength("pc", "aspc"), Is.EqualTo(2));
    }

    // ------------------------------------------------------------- highlighting

    static void AssertHighlight(string path, string query, string expected) {
      var item = new HasteItem(path, 0, "");
      var boundaries = HasteStringUtils.GetBoundaryIndices(item.path);
      var indices = HasteStringUtils.GetWeightedSubsequence(item.pathLower, query, boundaries);
      Assert.That(HasteStringUtils.BoldLabel(item.path, indices, "[", "]"), Is.EqualTo(expected));
    }

    [Test]
    [Category("Highlighting")]
    public void Highlight_PrefersWordBoundaries() {
      AssertHighlight("Component/Physics/Mesh Collider", "mc", "Component/Physics/[M]esh [C]ollider");
      AssertHighlight("Assets/Scripts/PlayerController.cs", "pc", "Assets/Scripts/[P]layer[C]ontroller.cs");
      AssertHighlight("GameObject/Create Empty Child", "cec", "GameObject/[C]reate [E]mpty [C]hild");
      AssertHighlight("Player/Model/Mesh", "pmm", "[P]layer/[M]odel/[M]esh");
    }

    [Test]
    [Category("Highlighting")]
    public void Highlight_FallsBackToInteriorCharacters() {
      AssertHighlight("Main Camera", "mainc", "[M][a][i][n] [C]amera");
      AssertHighlight("Assets/Scripts/PlayerController.cs", ".cs", "Assets/Scripts/PlayerController[.][c][s]");
    }

    [Test]
    [Category("Highlighting")]
    public void BoldLabel_WithNoIndicesReturnsInputUnchanged() {
      Assert.That(HasteStringUtils.BoldLabel("Main Camera", new int[0], "[", "]"), Is.EqualTo("Main Camera"));
    }

    // ------------------------------------------------------------------- index

    [Test]
    [Category("Index")]
    public void Index_CountsItemsAndCharacterReferencesSeparately() {
      var index = new HasteIndex();
      Assert.That(index.Count, Is.EqualTo(0));
      Assert.That(index.Size, Is.EqualTo(0));

      // "main camera" has 8 distinct characters (m a i n space c e r), so it occupies
      // eight buckets. It used to occupy two, one per boundary character.
      var item = new HasteItem("Main Camera", 0, "");
      index.Add(item);
      Assert.That(index.Count, Is.EqualTo(1));
      Assert.That(index.Size, Is.EqualTo(8));

      index.Remove(item);
      Assert.That(index.Count, Is.EqualTo(0));
      Assert.That(index.Size, Is.EqualTo(0));

      // Removing something that was never indexed is a no-op. It used to decrement
      // Count anyway, so a watcher's spurious Deleted event drove the count negative.
      index.Remove(new HasteItem("Never Added", 0, ""));
      Assert.That(index.Count, Is.EqualTo(0));
      Assert.That(index.Size, Is.EqualTo(0));

      // Adding the same item twice counts it once.
      index.Add(item);
      index.Add(new HasteItem("Main Camera", 0, ""));
      Assert.That(index.Count, Is.EqualTo(1));
      Assert.That(index.Size, Is.EqualTo(8));
    }

    [Test]
    [Category("Index")]
    public void Index_BucketsAreKeyedByPathCharacter() {
      var index = new HasteIndex();
      index.Add(new HasteItem("Main Camera", 0, ""));

      HashSet<HasteItem> bucket;
      Assert.That(index.TryGetValue('m', out bucket), Is.True);
      Assert.That(bucket.Count, Is.EqualTo(1));
      Assert.That(index.TryGetValue('c', out bucket), Is.True);
      Assert.That(bucket.Count, Is.EqualTo(1));
      // 'a' appears in the path without beginning a word. It used to have no bucket at
      // all, which is exactly what made interior matches unreachable; it now has one.
      Assert.That(index.TryGetValue('a', out bucket), Is.True);
      Assert.That(bucket.Count, Is.EqualTo(1));

      // A character that does not occur in the path still has no bucket.
      Assert.That(index.TryGetValue('z', out bucket), Is.False);
    }

    [Test]
    [Category("Index")]
    public void Item_DerivesItsSearchableFormsFromThePath() {
      var item = new HasteItem("Assets/Materials/MainCamera.mat", 7, "Project");
      Assert.That(item.path, Is.EqualTo("Assets/Materials/MainCamera.mat"));
      Assert.That(item.pathLower, Is.EqualTo("assets/materials/maincamera.mat"));
      Assert.That(item.name, Is.EqualTo("MainCamera"));
      Assert.That(item.nameLower, Is.EqualTo("maincamera"));
      Assert.That(item.boundariesLower, Is.EqualTo("ammc.m"));
      // No extension is derived: the field that used to hold one was never read. Searching
      // by extension works through the "." boundary character, not through a stored value.
      Assert.That(typeof(HasteItem).GetField("extensionLower"), Is.Null,
        "the unread extension field is back; see the note in HasteItem.cs");
      Assert.That(item.id, Is.EqualTo(7));
      Assert.That(item.source, Is.EqualTo("Project"));
      Assert.That(item.userScore, Is.EqualTo(0.0f));
    }

    // ------------------------------------------------------------------ sources

    [Test]
    [Category("Sources")]
    public void MenuItemSource_StaticInitialiserDoesNotThrow() {
      // Regression guard, kept although the regex it was written for is gone with the
      // attribute-scanning path. A shortcut-stripping Regex written as
      // @"\s+[\%\#\&\_]+\w$" -- "\_" is not a legal escape sequence -- made modern
      // .NET throw while constructing it, turning every touch of this type into a
      // TypeInitializationException and silently killing menu-item search on Unity 6,
      // with the project compiling cleanly throughout. Static state here stays cheap
      // and total for that reason.
      Assert.DoesNotThrow(() => { var unused = new HasteMenuItemSource(); });
    }

    // The source enumerates the editor's live menu tree. These run against whatever
    // editor is executing them, so they assert invariants rather than a fixed list.

    static string[] SourcePaths() {
      return new HasteMenuItemSource().Select(i => i.path).ToArray();
    }

    // An independent oracle: Unsupported.GetSubmenus is public, and is a different entry
    // point from the internal Menu.GetMenuItems the source prefers.
    static HashSet<string> LiveMenuOracle() {
      var live = new HashSet<string>();
      foreach (var root in new[] { "File", "Edit", "Assets", "GameObject", "Component",
                                   "Window", "Help", "Services" }) {
        var paths = UnityEditor.Unsupported.GetSubmenus(root);
        if (paths == null) continue;
        foreach (var p in paths) live.Add(p);
      }
      return live;
    }

    [Test]
    [Category("Sources")]
    public void MenuItemSource_YieldsOnlyMenuItemsThatActuallyExist() {
      // The point of the rewrite. The shipped Unity 5 list had 109 of 241 paths (45%)
      // that do not exist on Unity 6 -- results that look real and do nothing.
      var live = LiveMenuOracle();
      var custom = new HashSet<string>(new[] {
        "Assets/Instantiate Prefab", "GameObject/Lock", "GameObject/Unlock",
        "GameObject/Activate", "GameObject/Deactivate", "GameObject/Reset Transform",
        "GameObject/Select Parent", "GameObject/Select Children", "GameObject/Select Prefab",
        "GameObject/Revert to Prefab",
      });

      var phantom = SourcePaths().Where(p => !custom.Contains(p) && !live.Contains(p)).ToArray();
      Assert.That(phantom, Is.Empty,
        "these paths are indexed but are not in the editor's menu tree:\n  " +
        string.Join("\n  ", phantom));
    }

    [Test]
    [Category("Sources")]
    public void MenuItemSource_FindsMenuItemsTheHardcodedListNeverHad() {
      var paths = new HashSet<string>(SourcePaths());
      // Unity 6 menu items with no equivalent in the Unity 5 list that used to ship.
      Assert.That(paths, Contains.Item("Edit/Project Settings..."));
      Assert.That(paths, Contains.Item("File/Build Profiles"));
      Assert.That(paths, Contains.Item("GameObject/Create Empty Parent"));
      // And it should be finding hundreds of items, not a few dozen.
      Assert.That(paths.Count, Is.GreaterThan(400));
    }

    [Test]
    [Category("Sources")]
    public void MenuItemSource_DoesNotIndexHasteItself() {
      Assert.That(SourcePaths(), Has.No.Member("Window/Haste"));
    }

    [Test]
    [Category("Sources")]
    public void MenuItemSource_PathsAreCleanAndUnique() {
      var paths = SourcePaths();
      Assert.That(paths.Where(p => p.EndsWith("/")), Is.Empty, "a path ended in a separator");
      Assert.That(paths.Where(p => string.IsNullOrEmpty(p)), Is.Empty);
      Assert.That(paths.Length, Is.EqualTo(paths.Distinct().Count()), "duplicate paths");
      // Live paths arrive without the "%k" a [MenuItem] attribute carries, so the source
      // no longer parses them. This is what makes that safe to rely on.
      Assert.That(paths.Where(p => System.Text.RegularExpressions.Regex.IsMatch(p, @"\s+[%#&_]+\w$")),
        Is.Empty, "a live menu path carried a shortcut suffix");
    }

    [Test]
    [Category("Sources")]
    public void MenuItemSource_EveryInventedActionHasAnImplementation() {
      // HasteActions and the source's custom list are two halves of one feature. If they
      // drift, either an action is offered that does nothing, or one exists that cannot
      // be reached. Both directions are checked.
      var invented = SourcePaths().Where(p => !LiveMenuOracle().Contains(p)).ToArray();
      foreach (var path in invented) {
        Assert.That(HasteActions.MenuItemFallbacks.ContainsKey(path), Is.True,
          "\"" + path + "\" is offered but has no implementation");
      }
      foreach (var key in HasteActions.MenuItemFallbacks.Keys) {
        Assert.That(invented, Contains.Item(key),
          "\"" + key + "\" is implemented but never offered");
      }
    }

    [Test]
    [Category("Index")]
    public void Item_IdentityIsPathAndIdOnly() {
      var a = new HasteItem("Main Camera", 0, "Hierarchy");
      var b = new HasteItem("Main Camera", 0, "Project");
      var c = new HasteItem("Main Camera", 1, "Hierarchy");

      // Source is deliberately NOT part of identity.
      Assert.That(a.Equals(b), Is.True);
      Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));

      Assert.That(a.Equals(c), Is.False);
      Assert.That(a.Equals(null), Is.False);
    }
  }
}
