using System.Collections.Generic;
using KinematicCharacterController;
using UnityEngine;
using UnityEngine.Rendering;

namespace SlopMod
{
    /// <summary>
    /// Draws the colliders / triggers of the scene, colour-coded by what they do, so it works in the
    /// shipped URP build (toggled from the toolbox: T = trigger volumes, C = solid colliders).
    ///
    /// The kind of each collider is decided by the game component sitting on it (reviewed from the
    /// original Assembly-CSharp):
    ///   * <c>TimelineActivator</c>     - a timeline/cutscene trigger (usually disabled until reached).
    ///   * <c>CompleteMisionTrigger</c> - mission objective triggers.
    ///   * <c>TrackSimpleTrigger</c>    - Cinemachine camera triggers.
    ///   * <c>DialogInGameTrigger</c>   - in-game dialog triggers.
    ///   * <c>MusicTrigger</c>          - music zone triggers.
    ///   * <c>AchievementTrigger</c>    - achievement triggers.
    ///   * <c>PlayerDetectionArea</c>   - player detection areas.
    ///   * <c>DeepWater</c> / <c>RiverWaterStream</c> - water volumes.
    ///   * <c>StarCollected</c> / <c>StickerItem</c> / <c>ToySavingController</c> - collectibles.
    ///   * <c>HideAndShowRoom</c> / <c>HideAndShowRoomsAction</c> - room toggles.
    ///   * the <c>KinematicCharacterMotor.Capsule</c> of a <c>PlayerController</c> - player / NPC
    ///     hitboxes (players are AI-off, anything else counts as an NPC).
    ///   * anything else that is a trigger, or a solid collider.
    ///
    /// Inactive colliders are included as long as they are triggers, so cutscene/timeline triggers
    /// show up even while they are disabled. The cutscene/timeline, mission-objective and dialog
    /// triggers AND the player / NPC hitboxes are drawn <b>filled</b>; everything else is a wireframe.
    ///
    /// Only colliders within <see cref="CullRadius"/> of the player/camera are drawn. Each category has
    /// its own update frequency (<see cref="SetCategoryFrequency"/>, adjustable from the toolbox), and
    /// the caller can tune <see cref="CullRadius"/>, <see cref="WireThickness"/> and
    /// <see cref="Opacity"/> live.
    ///
    /// Every collider is drawn as its REAL shape in world space (oriented box / sphere / capsule /
    /// true mesh wireframe for <c>MeshCollider</c>) - NOT as its axis-aligned bounds, which is why a
    /// previous version showed inflated, axis-aligned boxes floating in the air. Culling is a cheap
    /// bounds distance test, and big mesh colliders are clipped per-triangle to the view radius, so a
    /// low <see cref="CullRadius"/> stays cheap even with "solid colliders" on.
    /// </summary>
    internal sealed class WorldDebugView : MonoBehaviour
    {
        internal struct CategoryInfo
        {
            public readonly string Name;
            public readonly Color Color;
            public readonly bool Filled;
            public readonly float DefaultFrequency; // Hz this category starts at; <= 0 means "use the global default"

            public CategoryInfo(string name, Color color, bool filled, float defaultFrequency = -1f)
            {
                Name = name;
                Color = color;
                Filled = filled;
                DefaultFrequency = defaultFrequency;
            }
        }

        // The cutscene/timeline category is index 0 (kept in sync with Classify()).
        internal const int CutsceneCategory = 0;

        // Character hitbox categories (kept in sync with CategoryDefs below). Players and NPCs are
        // both PlayerControllers whose collision is the KinematicCharacterMotor's capsule.
        internal const int PlayerHitboxCategory = 12;
        internal const int NpcHitboxCategory = 13;

        private static readonly CategoryInfo[] CategoryDefs =
        {
            new CategoryInfo("Cutscene / timeline", new Color(0.75f, 0.25f, 1f, 0.25f), true),
            new CategoryInfo("Mission objective",   new Color(1f, 0.25f, 0.25f, 0.25f), true),
            new CategoryInfo("Camera trigger",      new Color(0.2f, 0.9f, 1f), false),
            new CategoryInfo("Dialog trigger",      new Color(1f, 0.6f, 0.1f, 0.25f), true),
            new CategoryInfo("Music zone",          new Color(0.3f, 0.5f, 1f), false),
            new CategoryInfo("Achievement",         new Color(1f, 0.85f, 0.15f), false),
            new CategoryInfo("Player detection",    new Color(0.3f, 1f, 0.35f), false),
            new CategoryInfo("Water volume",        new Color(0.2f, 0.6f, 1f), false),
            new CategoryInfo("Collectible",         new Color(1f, 0.45f, 0.85f), false),
            new CategoryInfo("Room / hide",         new Color(0.8f, 0.8f, 1f), false),
            new CategoryInfo("Trigger (other)",     new Color(1f, 1f, 1f), false),
            new CategoryInfo("Solid collider",      new Color(0.55f, 0.55f, 0.55f), false),
            new CategoryInfo("Player hitbox",       new Color(0.15f, 1f, 0.45f, 0.35f), true, CharacterHitboxFrequency),
            new CategoryInfo("NPC hitbox",          new Color(1f, 0.45f, 0.1f, 0.35f), true, CharacterHitboxFrequency)
        };

        // Only colliders within this distance of the player/camera are drawn. Adjustable at runtime;
        // the minimum is deliberately small so the view can be dialled right down when the scene is
        // dense and "solid colliders" is on.
        internal const float MinCullRadius = 1f;
        internal const float MaxCullRadius = 500f;
        private static float _cullRadius = 60f;

        // Performance guards: each category has a vertex budget and a single giant mesh only draws
        // the first N triangles, so a huge level mesh cannot melt the framerate.
        private const int MaxVerticesPerCategory = 150000;
        private const int MaxMeshTrianglesPerCollider = 4000;

        // Wire-frame line thickness (world metres). 0 keeps Unity's cheap 1-pixel line pass; anything
        // larger expands each segment into crossed quads, because URP has no variable line width.
        internal const float MinWireThickness = 0f;
        internal const float MaxWireThickness = 0.3f;
        private const int MaxExpandedVertices = 150000;
        // Default to 0 (Unity's cheap 1-pixel line pass). Anything larger expands every segment into
        // crossed quads, which multiplies the vertex count and costs framerate.
        private static float _wireThickness = 0f;

        // Overall alpha multiplier applied to every collider/trigger colour.
        internal const float MinOpacity = 0f;
        internal const float MaxOpacity = 1f;
        private static float _opacity = 1f;

        internal static WorldDebugView Instance { get; private set; }

        internal static int CategoryCount { get { return CategoryDefs.Length; } }

        internal static CategoryInfo GetCategory(int index) { return CategoryDefs[index]; }

        /// <summary>Whether a collider category is currently drawn.</summary>
        internal static bool IsCategoryVisible(int index)
        {
            return _categoryVisible == null || _categoryVisible[index];
        }

        /// <summary>Shows / hides a whole collider category and forces a rebuild.</summary>
        internal static void SetCategoryVisible(int index, bool visible)
        {
            if (_categoryVisible == null)
            {
                _categoryVisible = new bool[CategoryDefs.Length];
                for (int i = 0; i < _categoryVisible.Length; i++) _categoryVisible[i] = true;
            }
            if (_categoryVisible[index] == visible) return;
            _categoryVisible[index] = visible;
            MarkCategoryDirty(index);
        }

        /// <summary>Whether a category is drawn filled (solid) rather than as a wireframe.</summary>
        internal static bool IsCategoryFilled(int index)
        {
            if (_categoryFill == null) return CategoryDefs[index].Filled;
            return _categoryFill[index];
        }

        /// <summary>Switches a category between filled and wireframe (categories that start filled default on).</summary>
        internal static void SetCategoryFilled(int index, bool filled)
        {
            if (_categoryFill == null)
            {
                _categoryFill = new bool[CategoryDefs.Length];
                for (int i = 0; i < _categoryFill.Length; i++) _categoryFill[i] = CategoryDefs[i].Filled;
            }
            if (_categoryFill[index] == filled) return;
            _categoryFill[index] = filled;
            MarkCategoryDirty(index);
        }

        // --- update frequency (how often each category's mesh is rebuilt) ---

        /// <summary>Global update frequency in Hz (the last value set via <see cref="SetAllUpdateFrequencies"/>).</summary>
        internal static float UpdateFrequency { get { return _globalFrequency; } }

        internal static float GetCategoryFrequency(int index)
        {
            EnsureFrequencyArray();
            return _categoryFrequency[index];
        }

        /// <summary>Sets the update frequency (Hz) of EVERY category at once.</summary>
        internal static void SetAllUpdateFrequencies(float hz)
        {
            hz = Mathf.Clamp(hz, MinUpdateFrequency, MaxUpdateFrequency);
            _globalFrequency = hz;
            EnsureFrequencyArray();
            for (int i = 0; i < _categoryFrequency.Length; i++) _categoryFrequency[i] = hz;
            MarkAllDirty();
        }

        /// <summary>Sets one category's update frequency (Hz). 0 freezes it (rebuilds once, then stops).</summary>
        internal static void SetCategoryFrequency(int index, float hz)
        {
            EnsureFrequencyArray();
            hz = Mathf.Clamp(hz, MinUpdateFrequency, MaxUpdateFrequency);
            if (Mathf.Approximately(_categoryFrequency[index], hz)) return;
            _categoryFrequency[index] = hz;
            MarkCategoryDirty(index);
        }

        private static void EnsureFrequencyArray()
        {
            if (_categoryFrequency != null) return;
            _categoryFrequency = new float[CategoryDefs.Length];
            for (int i = 0; i < _categoryFrequency.Length; i++)
            {
                // A category that declares its own DefaultFrequency (the character hitboxes) keeps it;
                // every other category inherits the global default.
                float categoryDefault = CategoryDefs[i].DefaultFrequency;
                _categoryFrequency[i] = categoryDefault > 0f ? categoryDefault : _globalFrequency;
            }
        }

        private static void MarkCategoryDirty(int index)
        {
            if (Instance != null) Instance.MarkCategoryDirtyInternal(index);
        }

        private static void MarkAllDirty()
        {
            if (Instance != null) Instance.MarkAllDirtyInternal();
        }

        private bool _showTriggers;
        private bool _showColliders;
        private bool _hideWorld;
        private readonly HashSet<Renderer> _hiddenRenderers = new HashSet<Renderer>();
        private float _nextHiddenRefresh;
        private const float HiddenRefreshInterval = 0.5f;
        private bool _shaderLogged;
        private bool _shaderWarned;

        private Mesh[] _meshes;
        private Material[] _materials;
        private static HashSet<long> _edgeScratch;

        // Per-category visibility (all shown by default). Toggled from the toolbox.
        private static bool[] _categoryVisible;

        // Per-category fill (solid vs wireframe). Defaults come from CategoryInfo.Filled, so the
        // categories that already drew filled start on. Toggled from the toolbox.
        private static bool[] _categoryFill;

        // Per-category update frequency (Hz): how often each category's mesh is rebuilt. The global
        // value is the default for every category and is what the "all categories" slider sets.
        internal const float MinUpdateFrequency = 0f;  // 0 = freeze (rebuild once, then never again)
        internal const float MaxUpdateFrequency = 60f; // one rebuild every ~17 ms
        private const float DefaultUpdateFrequency = 8f;

        // The character hitboxes trace fast-moving KinematicCharacterMotor capsules, so they start on
        // the maximum rate (60 Hz, matching the game's character update) instead of the cheap global
        // default; everything else - including all the (static) level geometry, slopes and ground -
        // stays on DefaultUpdateFrequency, where it barely moves and no smoothness is lost.
        internal const float CharacterHitboxFrequency = MaxUpdateFrequency; // 60 Hz

        private static float _globalFrequency = DefaultUpdateFrequency;
        private static float[] _categoryFrequency;

        private Collider[] _colliderCache;
        private int[] _metaCategory;       // cached per-collider category (-1 = skip); parallel to _colliderCache
        private bool[] _metaIgnoreRadius;  // cached "always visible" flag; parallel to _colliderCache
        private float _nextColliderRefresh;
        private float[] _nextCategoryUpdate;   // Time.unscaledTime at which each category is next rebuilt
        private bool[] _categoryDirty;         // categories waiting to be rebuilt right now
        private const float ColliderRefreshInterval = 0.5f;

        internal static bool TriggersVisible { get { return Instance != null && Instance._showTriggers; } }
        internal static bool CollidersVisible { get { return Instance != null && Instance._showColliders; } }

        /// <summary>True while the whole scene (level + objects) is hidden so only the visualizer shows.</summary>
        internal static bool HideWorld { get { return Instance != null && Instance._hideWorld; } }

        /// <summary>Hides / shows every scene renderer while the collider view is on.</summary>
        internal static void ToggleHideWorld()
        {
            EnsureExists();
            Instance._hideWorld = !Instance._hideWorld;
            if (SlopModPlugin.Log != null) SlopModPlugin.Log.LogInfo("Hide world " + (Instance._hideWorld ? "ON" : "OFF"));
        }

        /// <summary>Wire-frame line thickness in world metres (0 = 1 pixel).</summary>
        internal static float WireThickness
        {
            get { return _wireThickness; }
            set
            {
                float clamped = Mathf.Clamp(value, MinWireThickness, MaxWireThickness);
                if (Mathf.Approximately(clamped, _wireThickness)) return;
                _wireThickness = clamped;
                MarkAllDirty();
            }
        }

        /// <summary>Draw distance in metres; every collider inside it is shown in full.</summary>
        internal static float CullRadius
        {
            get { return _cullRadius; }
            set
            {
                float clamped = Mathf.Clamp(value, MinCullRadius, MaxCullRadius);
                if (Mathf.Approximately(clamped, _cullRadius)) return;
                _cullRadius = clamped;
                MarkAllDirty();
            }
        }

        /// <summary>Alpha multiplier applied to every collider/trigger colour.</summary>
        internal static float Opacity
        {
            get { return _opacity; }
            set
            {
                float clamped = Mathf.Clamp(value, MinOpacity, MaxOpacity);
                if (Mathf.Approximately(clamped, _opacity)) return;
                _opacity = clamped;
                if (Instance != null) Instance.ApplyOpacity();
            }
        }

        internal static void EnsureExists()
        {
            if (Instance != null) return;
            GameObject go = new GameObject("SlopMod_WorldDebugView");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<WorldDebugView>();
        }

        internal static void ToggleTriggers()
        {
            EnsureExists();
            Instance._showTriggers = !Instance._showTriggers;
            MarkAllDirty();
            if (SlopModPlugin.Log != null) SlopModPlugin.Log.LogInfo("Trigger view " + (Instance._showTriggers ? "ON" : "OFF"));
        }

        internal static void ToggleColliders()
        {
            EnsureExists();
            Instance._showColliders = !Instance._showColliders;
            MarkAllDirty();
            if (SlopModPlugin.Log != null) SlopModPlugin.Log.LogInfo("Collider view " + (Instance._showColliders ? "ON" : "OFF"));
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Object.Destroy(gameObject);
                return;
            }
            Instance = this;
            Object.DontDestroyOnLoad(gameObject);
            _meshes = new Mesh[CategoryDefs.Length];
            _materials = new Material[CategoryDefs.Length];
        }

        private void OnDestroy()
        {
            RestoreRenderers();
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            bool viewOn = _showTriggers || _showColliders;
            if (viewOn && !EnsureMaterials()) viewOn = false;

            // Hide the whole scene (level + objects) while the visualizer is showing, if requested.
            UpdateWorldVisibility(viewOn);

            if (!viewOn) return;

            if (_categoryFrequency == null) EnsureFrequencyArray();
            EnsureTimingArrays();

            float now = Time.unscaledTime;

            // The collider list is only re-scanned occasionally - FindObjectsOfType is expensive.
            if (_colliderCache == null || now >= _nextColliderRefresh)
            {
                _colliderCache = Object.FindObjectsOfType<Collider>(true);
                BuildMetadata(_colliderCache);
                _nextColliderRefresh = now + ColliderRefreshInterval;
                MarkAllDirtyInternal();
            }

            // Rebuild every category whose OWN update interval has elapsed. A low frequency is cheap;
            // a high frequency makes colliders track their positions more smoothly.
            bool anyDue = false;
            for (int i = 0; i < _categoryDirty.Length; i++)
            {
                if (_categoryDirty[i]) { anyDue = true; continue; }
                if (now >= _nextCategoryUpdate[i]) { _categoryDirty[i] = true; anyDue = true; }
            }
            if (anyDue) RebuildDirty(now);

            for (int i = 0; i < _meshes.Length; i++)
            {
                Mesh mesh = _meshes[i];
                Material material = _materials[i];
                if (mesh == null || material == null || mesh.vertexCount == 0) continue;
                Graphics.DrawMesh(mesh, Matrix4x4.identity, material, 0);
            }
        }

        /// <summary>Allocates the per-category timing arrays; every category starts out dirty.</summary>
        private void EnsureTimingArrays()
        {
            if (_categoryDirty != null && _categoryDirty.Length == CategoryDefs.Length) return;

            _categoryDirty = new bool[CategoryDefs.Length];
            _nextCategoryUpdate = new float[CategoryDefs.Length];
            for (int i = 0; i < _categoryDirty.Length; i++) _categoryDirty[i] = true;
        }

        private void MarkCategoryDirtyInternal(int index)
        {
            EnsureTimingArrays();
            if (index >= 0 && index < _categoryDirty.Length) _categoryDirty[index] = true;
        }

        private void MarkAllDirtyInternal()
        {
            EnsureTimingArrays();
            for (int i = 0; i < _categoryDirty.Length; i++) _categoryDirty[i] = true;
        }

        // --- hide the world while the visualizer is showing ---

        /// <summary>
        /// Disables every scene <see cref="Renderer"/> while the collider view is on (so only the
        /// visualizer's meshes show) and restores them as soon as it turns off.
        /// </summary>
        private void UpdateWorldVisibility(bool viewOn)
        {
            bool shouldHide = _hideWorld && viewOn;

            if (shouldHide)
            {
                float now = Time.unscaledTime;
                if (_hiddenRenderers.Count == 0 || now >= _nextHiddenRefresh)
                {
                    _nextHiddenRefresh = now + HiddenRefreshInterval;
                    HideRenderers();
                }
            }
            else if (_hiddenRenderers.Count > 0)
            {
                RestoreRenderers();
            }
        }

        private void HideRenderers()
        {
            Renderer[] renderers = Object.FindObjectsOfType<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled) continue;
                renderer.enabled = false;
                _hiddenRenderers.Add(renderer);
            }
        }

        private void RestoreRenderers()
        {
            foreach (Renderer renderer in _hiddenRenderers)
            {
                if (renderer != null) renderer.enabled = true;
            }
            _hiddenRenderers.Clear();
        }

        /// <summary>
        /// Rebuilds only the categories that are currently marked dirty (per-category update
        /// frequency). Each rebuilt category's next update is scheduled from its own frequency.
        /// </summary>
        private void RebuildDirty(float now)
        {
            Collider[] colliders = _colliderCache;
            if (colliders == null)
            {
                for (int i = 0; i < _categoryDirty.Length; i++) _categoryDirty[i] = false;
                return;
            }

            if (_metaCategory == null || _metaCategory.Length != colliders.Length) BuildMetadata(colliders);

            Vector3 center;
            bool haveCenter = TryGetViewCenter(out center);
            float radius = _cullRadius;
            float radiusSqr = radius * radius;

            int count = CategoryDefs.Length;
            List<Vector3>[] vertices = new List<Vector3>[count];
            List<int>[] triangles = new List<int>[count];
            for (int i = 0; i < count; i++)
            {
                if (!_categoryDirty[i]) continue;
                vertices[i] = new List<Vector3>();
                if (IsCategoryFilled(i)) triangles[i] = new List<int>();
            }

            int[] metadata = _metaCategory;
            bool[] ignoreFlags = _metaIgnoreRadius;

            for (int i = 0; i < colliders.Length; i++)
            {
                // The player test and the component-based classification are cached in
                // BuildMetadata() - doing them per collider (GetComponentInParent ~10x) every rebuild
                // was the main cost once "solid colliders" was on.
                int category = (metadata != null && i < metadata.Length) ? metadata[i] : -1;
                if (category < 0 || category >= count || !_categoryDirty[category]) continue;

                Collider collider = colliders[i];
                if (collider == null || !collider.enabled) continue;

                // "Show triggers" (T) and "Show colliders" (C) are two DISTINCT views: T draws the
                // trigger volumes, C draws the solid (non-trigger) collision.
                bool isTrigger = collider.isTrigger;
                if (isTrigger && !_showTriggers) continue;
                if (!isTrigger && !_showColliders) continue;

                if (!IsCategoryVisible(category)) continue;

                // Cheap bound-based distance test. (The old code called Collider.ClosestPoint here,
                // which is very expensive on big MeshColliders - that is what kept even a 5 m view
                // distance slow. Big meshes are clipped per-triangle further down instead.)
                bool ignoreRadius = ignoreFlags != null && i < ignoreFlags.Length && ignoreFlags[i];
                if (!ignoreRadius && haveCenter && collider.bounds.SqrDistance(center) > radiusSqr) continue;
                if (vertices[category].Count >= MaxVerticesPerCategory) continue;

                // Draw the collider's REAL shape (in world space), not its axis-aligned bounds.
                AppendCollider(vertices[category], triangles[category], collider, IsCategoryFilled(category),
                    haveCenter, center, radius);
            }

            for (int i = 0; i < count; i++)
            {
                if (!_categoryDirty[i]) continue;

                if (_meshes[i] == null) _meshes[i] = NewMesh();
                ApplyMesh(_meshes[i], vertices[i], triangles[i], IsCategoryFilled(i), _wireThickness);

                float hz = _categoryFrequency != null && i < _categoryFrequency.Length ? _categoryFrequency[i] : _globalFrequency;
                _nextCategoryUpdate[i] = hz > 0.001f ? now + (1f / hz) : float.PositiveInfinity;
                _categoryDirty[i] = false;
            }
        }

        /// <summary>
        /// Caches the expensive per-collider work (player test + component classification) into two
        /// arrays parallel to <see cref="_colliderCache"/>. Rebuild then only needs a cheap bounds
        /// distance test per collider, which is what makes low view distances usable when the scene
        /// has lots of solid colliders.
        /// </summary>
        private void BuildMetadata(Collider[] colliders)
        {
            if (_metaCategory == null || _metaCategory.Length != colliders.Length) _metaCategory = new int[colliders.Length];
            if (_metaIgnoreRadius == null || _metaIgnoreRadius.Length != colliders.Length) _metaIgnoreRadius = new bool[colliders.Length];

            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                _metaIgnoreRadius[i] = false;

                if (collider == null || !collider.enabled) { _metaCategory[i] = -1; continue; }

                // Character hitboxes: the KinematicCharacterMotor capsule of a player or an NPC. These
                // get their own filled categories and ignore the view distance so every character is
                // always visible. Character hitboxes are always capsules, so only capsule colliders pay
                // for the hierarchy lookup.
                if (collider is CapsuleCollider)
                {
                    KinematicCharacterMotor motor = collider.GetComponentInParent<KinematicCharacterMotor>();
                    if (motor != null && motor.Capsule == collider)
                    {
                        PlayerController owner = collider.GetComponentInParent<PlayerController>();
                        _metaCategory[i] = (owner != null && !owner.m_isAI) ? PlayerHitboxCategory : NpcHitboxCategory;
                        _metaIgnoreRadius[i] = true;
                        continue;
                    }
                }

                // Any other collider that belongs to a character (extra rig colliders, etc.) is ignored.
                if (collider.gameObject.layer == Layers.PLAYER || collider.GetComponentInParent<PlayerController>() != null)
                {
                    _metaCategory[i] = -1;
                    continue;
                }

                // Disabled objects are only shown when they are triggers - cutscene / timeline
                // triggers are usually switched off until they are needed.
                if (!collider.gameObject.activeInHierarchy && !collider.isTrigger) { _metaCategory[i] = -1; continue; }

                bool ignoreRadius;
                _metaCategory[i] = Classify(collider, out ignoreRadius);
                _metaIgnoreRadius[i] = ignoreRadius;
            }
        }

        /// <summary>
        /// Returns the category index of a collider, based on the game component on it (or its
        /// parents). Sets <paramref name="ignoreRadius"/> for cutscene/timeline triggers so they are
        /// always visible.
        /// </summary>
        private static int Classify(Collider collider, out bool ignoreRadius)
        {
            ignoreRadius = false;
            GameObject go = collider.gameObject;

            if (go.GetComponentInParent<TimelineActivator>() != null) { ignoreRadius = true; return 0; }
            if (go.GetComponentInParent<CompleteMisionTrigger>() != null) return 1;
            if (go.GetComponentInParent<TrackSimpleTrigger>() != null) return 2;
            if (go.GetComponentInParent<DialogInGameTrigger>() != null) return 3;
            if (go.GetComponentInParent<MusicTrigger>() != null) return 4;
            if (go.GetComponentInParent<AchievementTrigger>() != null) return 5;
            if (go.GetComponentInParent<PlayerDetectionArea>() != null) return 6;
            if (go.GetComponentInParent<DeepWater>() != null || go.GetComponentInParent<RiverWaterStream>() != null) return 7;
            if (go.GetComponentInParent<StarCollected>() != null || go.GetComponentInParent<StickerItem>() != null || go.GetComponentInParent<ToySavingController>() != null) return 8;
            if (go.GetComponentInParent<HideAndShowRoom>() != null || go.GetComponentInParent<HideAndShowRoomsAction>() != null) return 9;
            if (collider.isTrigger) return 10;
            return 11;
        }

        private static bool TryGetViewCenter(out Vector3 center)
        {
            GameplayManager gm = GameManager.m_gameplayManager;
            if (gm != null && gm.m_playerControllers != null)
            {
                for (int i = 0; i < gm.m_playerControllers.Length; i++)
                {
                    PlayerController player = gm.m_playerControllers[i];
                    if (player != null && player.gameObject.activeInHierarchy)
                    {
                        center = player.transform.position;
                        return true;
                    }
                }
            }

            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                center = mainCamera.transform.position;
                return true;
            }

            center = Vector3.zero;
            return false;
        }

        private static Mesh NewMesh()
        {
            Mesh mesh = new Mesh();
            mesh.indexFormat = IndexFormat.UInt32;
            mesh.hideFlags = HideFlags.HideAndDontSave;
            return mesh;
        }

        private static void ApplyMesh(Mesh mesh, List<Vector3> vertices, List<int> triangles, bool filled, float thickness)
        {
            mesh.Clear();
            if (vertices.Count == 0) return;
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);

            // Filled categories: real triangles. Only the box path produces them; sphere / capsule /
            // mesh shapes stay as line pairs and fall through to the wireframe path below.
            if (filled && triangles != null && triangles.Count > 0)
            {
                mesh.SetVertices(vertices);
                mesh.SetIndices(triangles.ToArray(), MeshTopology.Triangles, 0);
                return;
            }

            // Thin lines (0) use Unity's 1-pixel Lines pass - cheapest.
            if (thickness <= 0.0005f)
            {
                mesh.SetVertices(vertices);
                int[] indices = new int[vertices.Count];
                for (int i = 0; i < indices.Length; i++) indices[i] = i;
                mesh.SetIndices(indices, MeshTopology.Lines, 0);
                return;
            }

            // Thick lines: expand every segment (vertex pair) into two crossed quads, because URP
            // has no variable line width. Cull is off, so the cross is visible from any angle.
            float half = thickness * 0.5f;
            List<Vector3> expandedVertices = new List<Vector3>(vertices.Count * 4);
            List<int> expandedTriangles = new List<int>(vertices.Count * 6);
            int pairs = vertices.Count / 2;
            for (int i = 0; i < pairs; i++)
            {
                Vector3 a = vertices[i * 2];
                Vector3 b = vertices[i * 2 + 1];
                Vector3 direction = b - a;
                float length = direction.magnitude;
                if (length < 0.0001f) continue;
                direction /= length;

                // Any reference axis that is not parallel to the segment works.
                Vector3 reference = Mathf.Abs(direction.y) > 0.99f ? Vector3.right : Vector3.up;
                Vector3 sideA = Vector3.Cross(direction, reference).normalized * half;
                Vector3 sideB = Vector3.Cross(direction, sideA).normalized * half;

                AddQuad(expandedVertices, expandedTriangles, a - sideA, b - sideA, b + sideA, a + sideA);
                AddQuad(expandedVertices, expandedTriangles, a - sideB, b - sideB, b + sideB, a + sideB);

                if (expandedVertices.Count >= MaxExpandedVertices) break;
            }

            if (expandedVertices.Count == 0) return;
            mesh.SetVertices(expandedVertices);
            mesh.SetIndices(expandedTriangles.ToArray(), MeshTopology.Triangles, 0);
        }

        /// <summary>Appends a single quad (p0-p1-p2-p3, counter-clockwise) as two triangles.</summary>
        private static void AddQuad(List<Vector3> v, List<int> t, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
        {
            int b = v.Count;
            v.Add(p0); v.Add(p1); v.Add(p2); v.Add(p3);
            t.Add(b + 0); t.Add(b + 1); t.Add(b + 2);
            t.Add(b + 0); t.Add(b + 2); t.Add(b + 3);
        }

        /// <summary>
        /// Appends the real shape of a collider (in world space) - NOT its axis-aligned bounds.
        /// Corner order for boxes: 0..3 = bottom face (y = min), 4..7 = top face (y = max).
        /// </summary>
        private static void AppendCollider(List<Vector3> v, List<int> t, Collider collider, bool filled,
            bool haveCenter, Vector3 center, float radius)
        {
            BoxCollider box = collider as BoxCollider;
            if (box != null)
            {
                AppendOrientedBox(v, t, box.transform, box.center - box.size * 0.5f, box.center + box.size * 0.5f, filled);
                return;
            }

            SphereCollider sphere = collider as SphereCollider;
            if (sphere != null)
            {
                if (filled) AppendSphereSolid(v, t, sphere);
                else AppendSphereShape(v, sphere);
                return;
            }

            CapsuleCollider capsule = collider as CapsuleCollider;
            if (capsule != null)
            {
                if (filled) AppendCapsuleSolid(v, t, capsule);
                else AppendCapsuleShape(v, capsule);
                return;
            }

            MeshCollider meshCollider = collider as MeshCollider;
            if (meshCollider != null && meshCollider.sharedMesh != null)
            {
                Mesh shared = meshCollider.sharedMesh;
                if (shared.isReadable)
                {
                    AppendMeshWire(v, meshCollider, shared, haveCenter, center, radius);
                }
                else
                {
                    // Mesh data is stripped in the shipped build - fall back to the mesh's LOCAL bounds
                    // transformed by the collider (still oriented to the object, unlike a world AABB).
                    AppendOrientedBox(v, t, meshCollider.transform, shared.bounds.min, shared.bounds.max, filled);
                }
                return;
            }

            AppendBox(v, t, collider.bounds, filled);
        }

        /// <summary>Draws a box defined by a local min/max corner, transformed into world space.</summary>
        private static void AppendOrientedBox(List<Vector3> v, List<int> t, Transform tr, Vector3 localMin, Vector3 localMax, bool filled)
        {
            AppendBoxCorners(v, t, filled,
                tr.TransformPoint(new Vector3(localMin.x, localMin.y, localMin.z)),
                tr.TransformPoint(new Vector3(localMax.x, localMin.y, localMin.z)),
                tr.TransformPoint(new Vector3(localMax.x, localMax.y, localMin.z)),
                tr.TransformPoint(new Vector3(localMin.x, localMax.y, localMin.z)),
                tr.TransformPoint(new Vector3(localMin.x, localMin.y, localMax.z)),
                tr.TransformPoint(new Vector3(localMax.x, localMin.y, localMax.z)),
                tr.TransformPoint(new Vector3(localMax.x, localMax.y, localMax.z)),
                tr.TransformPoint(new Vector3(localMin.x, localMax.y, localMax.z)));
        }

        /// <summary>World-space AABB box (fallback for colliders with no specific shape).</summary>
        private static void AppendBox(List<Vector3> v, List<int> t, Bounds b, bool filled)
        {
            Vector3 min = b.min;
            Vector3 max = b.max;
            AppendBoxCorners(v, t, filled,
                new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z),
                new Vector3(max.x, max.y, min.z), new Vector3(min.x, max.y, min.z),
                new Vector3(min.x, min.y, max.z), new Vector3(max.x, min.y, max.z),
                new Vector3(max.x, max.y, max.z), new Vector3(min.x, max.y, max.z));
        }

        /// <summary>Adds the 8 given world-space corners as a wireframe (12 edges) or filled box.</summary>
        private static void AppendBoxCorners(List<Vector3> v, List<int> t, bool filled,
            Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3,
            Vector3 p4, Vector3 p5, Vector3 p6, Vector3 p7)
        {
            if (filled && t != null)
            {
                int b0 = v.Count;
                v.Add(p0); v.Add(p1); v.Add(p2); v.Add(p3);
                v.Add(p4); v.Add(p5); v.Add(p6); v.Add(p7);

                Tri(t, b0 + 0, b0 + 1, b0 + 2); Tri(t, b0 + 0, b0 + 2, b0 + 3);
                Tri(t, b0 + 5, b0 + 4, b0 + 7); Tri(t, b0 + 5, b0 + 7, b0 + 6);
                Tri(t, b0 + 4, b0 + 0, b0 + 3); Tri(t, b0 + 4, b0 + 3, b0 + 7);
                Tri(t, b0 + 1, b0 + 5, b0 + 6); Tri(t, b0 + 1, b0 + 6, b0 + 2);
                Tri(t, b0 + 4, b0 + 5, b0 + 1); Tri(t, b0 + 4, b0 + 1, b0 + 0);
                Tri(t, b0 + 3, b0 + 2, b0 + 6); Tri(t, b0 + 3, b0 + 6, b0 + 7);
            }
            else
            {
                Edge(v, p0, p1); Edge(v, p1, p2); Edge(v, p2, p3); Edge(v, p3, p0);
                Edge(v, p4, p5); Edge(v, p5, p6); Edge(v, p6, p7); Edge(v, p7, p4);
                Edge(v, p0, p4); Edge(v, p1, p5); Edge(v, p2, p6); Edge(v, p3, p7);
            }
        }

        private static void AppendSphereShape(List<Vector3> v, SphereCollider sphere)
        {
            Transform tr = sphere.transform;
            Vector3 c = sphere.center;
            float r = sphere.radius;
            AppendLocalCircle(v, tr, c, Vector3.right, Vector3.up, r);
            AppendLocalCircle(v, tr, c, Vector3.right, Vector3.forward, r);
            AppendLocalCircle(v, tr, c, Vector3.up, Vector3.forward, r);
        }

        private static void AppendCapsuleShape(List<Vector3> v, CapsuleCollider capsule)
        {
            Transform tr = capsule.transform;
            Vector3 axis = capsule.direction == 0 ? Vector3.right : (capsule.direction == 2 ? Vector3.forward : Vector3.up);
            Vector3 perpA = axis == Vector3.up ? Vector3.right : Vector3.up;
            Vector3 perpB = Vector3.Cross(axis, perpA).normalized;

            float r = capsule.radius;
            float half = Mathf.Max(0f, capsule.height * 0.5f - r);
            Vector3 top = capsule.center + axis * half;
            Vector3 bottom = capsule.center - axis * half;

            AppendLocalCircle(v, tr, top, perpA, perpB, r);
            AppendLocalCircle(v, tr, bottom, perpA, perpB, r);

            Vector3 offA = perpA * r;
            Vector3 offB = perpB * r;
            Edge(v, tr.TransformPoint(top + offA), tr.TransformPoint(bottom + offA));
            Edge(v, tr.TransformPoint(top - offA), tr.TransformPoint(bottom - offA));
            Edge(v, tr.TransformPoint(top + offB), tr.TransformPoint(bottom + offB));
            Edge(v, tr.TransformPoint(top - offB), tr.TransformPoint(bottom - offB));
        }

        /// <summary>
        /// Solid (triangle) capsule, used for the filled character hitboxes. The profile runs from the
        /// bottom pole up through the lower hemisphere, the cylindrical middle and the upper hemisphere
        /// to the top pole; the poles are emitted as degenerate rings so stitching is uniform.
        /// </summary>
        private static void AppendCapsuleSolid(List<Vector3> v, List<int> t, CapsuleCollider capsule)
        {
            Transform tr = capsule.transform;
            Vector3 axis = capsule.direction == 0 ? Vector3.right : (capsule.direction == 2 ? Vector3.forward : Vector3.up);
            Vector3 perpA = axis == Vector3.up ? Vector3.right : Vector3.up;
            perpA = (perpA - axis * Vector3.Dot(perpA, axis)).normalized;
            Vector3 perpB = Vector3.Cross(axis, perpA);

            const int segments = 16;
            const int hemiSteps = 6;
            float r = capsule.radius;
            float half = Mathf.Max(0f, capsule.height * 0.5f - r);
            Vector3 center = capsule.center;

            // (axialOffset, radialRadius) profile, bottom -> top.
            List<Vector2> profile = new List<Vector2>();
            profile.Add(new Vector2(-(half + r), 0f)); // bottom pole
            for (int k = 1; k <= hemiSteps; k++)
            {
                float a = -Mathf.PI * 0.5f + (k / (float)(hemiSteps + 1)) * (Mathf.PI * 0.5f);
                profile.Add(new Vector2(-half + r * Mathf.Sin(a), r * Mathf.Cos(a)));
            }
            profile.Add(new Vector2(-half, r));
            profile.Add(new Vector2(half, r));
            for (int k = 1; k <= hemiSteps; k++)
            {
                float a = (k / (float)(hemiSteps + 1)) * (Mathf.PI * 0.5f);
                profile.Add(new Vector2(half + r * Mathf.Sin(a), r * Mathf.Cos(a)));
            }
            profile.Add(new Vector2(half + r, 0f)); // top pole

            AppendProfileSolid(v, t, tr, center, axis, perpA, perpB, profile, segments);
        }

        /// <summary>Solid (triangle) sphere, used for the filled character hitboxes.</summary>
        private static void AppendSphereSolid(List<Vector3> v, List<int> t, SphereCollider sphere)
        {
            Transform tr = sphere.transform;
            Vector3 c = sphere.center;
            float r = sphere.radius;

            const int segments = 16;
            const int rings = 14; // includes degenerate pole rings

            List<Vector2> profile = new List<Vector2>(rings);
            for (int j = 0; j < rings; j++)
            {
                float phi = -Mathf.PI * 0.5f + (j / (float)(rings - 1)) * Mathf.PI;
                profile.Add(new Vector2(Mathf.Sin(phi) * r, Mathf.Cos(phi) * r));
            }

            AppendProfileSolid(v, t, tr, c, Vector3.up, Vector3.right, Vector3.forward, profile, segments);
        }

        /// <summary>
        /// Revolves a profile of (axialOffset, radialRadius) points around the given axis, emitting a
        /// triangle strip per ring. Profile points with radius 0 become poles (degenerate rings).
        /// </summary>
        private static void AppendProfileSolid(List<Vector3> v, List<int> t, Transform tr, Vector3 center,
            Vector3 axis, Vector3 perpA, Vector3 perpB, List<Vector2> profile, int segments)
        {
            int ringCount = profile.Count;
            int start = v.Count;

            for (int j = 0; j < ringCount; j++)
            {
                Vector2 p = profile[j];
                for (int s = 0; s < segments; s++)
                {
                    float theta = (s / (float)segments) * Mathf.PI * 2f;
                    Vector3 radial = perpA * Mathf.Cos(theta) + perpB * Mathf.Sin(theta);
                    Vector3 local = center + axis * p.x + radial * p.y;
                    v.Add(tr.TransformPoint(local));
                }
            }

            for (int j = 0; j < ringCount - 1; j++)
            {
                int ringA = start + j * segments;
                int ringB = start + (j + 1) * segments;
                for (int s = 0; s < segments; s++)
                {
                    int s1 = (s + 1) % segments;
                    Tri(t, ringA + s, ringB + s, ringB + s1);
                    Tri(t, ringA + s, ringB + s1, ringA + s1);
                }
            }
        }

        /// <summary>Circle in the collider's LOCAL space (so scale/rotation are applied by the transform).</summary>
        private static void AppendLocalCircle(List<Vector3> v, Transform tr, Vector3 localCenter, Vector3 localAxisA, Vector3 localAxisB, float radius)
        {
            const int segments = 20;
            Vector3 prev = tr.TransformPoint(localCenter + localAxisA * radius);
            for (int i = 1; i <= segments; i++)
            {
                float a = (i / (float)segments) * Mathf.PI * 2f;
                Vector3 cur = tr.TransformPoint(localCenter + (Mathf.Cos(a) * localAxisA + Mathf.Sin(a) * localAxisB) * radius);
                Edge(v, prev, cur);
                prev = cur;
            }
        }

        /// <summary>True mesh wireframe for a MeshCollider (level / object collision), edge-deduplicated.</summary>
        private static void AppendMeshWire(List<Vector3> v, MeshCollider meshCollider, Mesh shared,
            bool haveCenter, Vector3 center, float radius)
        {
            Vector3[] verts = shared.vertices;
            int[] tris = shared.triangles;
            if (verts == null || tris == null || verts.Length == 0 || tris.Length < 3) return;

            Matrix4x4 m = meshCollider.transform.localToWorldMatrix;
            int triCount = Mathf.Min(tris.Length / 3, MaxMeshTrianglesPerCollider);
            float radiusSqr = radius * radius;

            if (_edgeScratch == null) _edgeScratch = new HashSet<long>();
            _edgeScratch.Clear();

            for (int i = 0; i < triCount; i++)
            {
                int t0 = i * 3;
                int a = tris[t0];
                int b = tris[t0 + 1];
                int c = tris[t0 + 2];
                AppendMeshEdge(v, m, verts, a, b, haveCenter, center, radiusSqr);
                AppendMeshEdge(v, m, verts, b, c, haveCenter, center, radiusSqr);
                AppendMeshEdge(v, m, verts, c, a, haveCenter, center, radiusSqr);
            }
        }

        private static void AppendMeshEdge(List<Vector3> v, Matrix4x4 m, Vector3[] verts, int a, int b,
            bool haveCenter, Vector3 center, float radiusSqr)
        {
            if (a < 0 || b < 0 || a >= verts.Length || b >= verts.Length) return;
            int lo = a < b ? a : b;
            int hi = a < b ? b : a;
            long key = ((long)lo << 32) | (uint)hi;
            if (!_edgeScratch.Add(key)) return;

            Vector3 wa = m.MultiplyPoint3x4(verts[lo]);
            Vector3 wb = m.MultiplyPoint3x4(verts[hi]);

            // Clip the wireframe to the view radius, so a giant level mesh only contributes the edges
            // near the player instead of its whole triangle soup.
            if (haveCenter && SqrDistanceToSegment(center, wa, wb) > radiusSqr) return;

            v.Add(wa);
            v.Add(wb);
        }

        private static void Edge(List<Vector3> v, Vector3 a, Vector3 b)
        {
            v.Add(a);
            v.Add(b);
        }

        /// <summary>Squared distance from a point to a line segment (used to clip mesh wireframes).</summary>
        private static float SqrDistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float lengthSqr = ab.sqrMagnitude;
            if (lengthSqr < 1e-6f) return (p - a).sqrMagnitude;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / lengthSqr);
            Vector3 projection = a + ab * t;
            return (p - projection).sqrMagnitude;
        }

        private static void Tri(List<int> t, int a, int b, int c)
        {
            t.Add(a);
            t.Add(b);
            t.Add(c);
        }

        private bool EnsureMaterials()
        {
            if (_materials == null) _materials = new Material[CategoryDefs.Length];
            if (_meshes == null) _meshes = new Mesh[CategoryDefs.Length];

            // Fast path: everything is already built - never call Shader.Find on a per-frame basis.
            bool ready = true;
            for (int i = 0; i < _materials.Length; i++)
            {
                if (_materials[i] == null) { ready = false; break; }
            }
            if (ready) return true;

            Shader shader = ResolveShader();
            if (shader == null)
            {
                if (!_shaderWarned)
                {
                    _shaderWarned = true;
                    if (SlopModPlugin.Log != null) SlopModPlugin.Log.LogWarning("WorldDebugView: no usable shader found - colliders/triggers cannot be drawn.");
                }
                return false;
            }

            for (int i = 0; i < CategoryDefs.Length; i++)
            {
                if (_materials[i] == null) _materials[i] = MakeMaterial(shader, CategoryDefs[i].Color);
            }
            ApplyOpacity();
            return true;
        }

        private Shader ResolveShader()
        {
            for (int i = 0; i < ShaderCandidates.Length; i++)
            {
                Shader shader = Shader.Find(ShaderCandidates[i]);
                if (shader != null)
                {
                    if (!_shaderLogged)
                    {
                        _shaderLogged = true;
                        if (SlopModPlugin.Log != null) SlopModPlugin.Log.LogInfo("WorldDebugView: using shader '" + ShaderCandidates[i] + "'.");
                    }
                    return shader;
                }
            }

            // Fallback: reuse a shader from a material that is actually rendered in the scene.
            MeshRenderer meshRenderer = Object.FindObjectOfType<MeshRenderer>();
            if (meshRenderer != null && meshRenderer.sharedMaterial != null) return meshRenderer.sharedMaterial.shader;

            SkinnedMeshRenderer skinned = Object.FindObjectOfType<SkinnedMeshRenderer>();
            if (skinned != null && skinned.sharedMaterial != null) return skinned.sharedMaterial.shader;

            SpriteRenderer sprite = Object.FindObjectOfType<SpriteRenderer>();
            if (sprite != null && sprite.sharedMaterial != null) return sprite.sharedMaterial.shader;

            return null;
        }

        private static Material MakeMaterial(Shader shader, Color color)
        {
            Material material = new Material(shader);
            material.hideFlags = HideFlags.HideAndDontSave;

            SetColor(material, new Color(color.r, color.g, color.b, color.a * _opacity));

            if (material.HasProperty("_Cull")) material.SetInt("_Cull", (int)CullMode.Off);
            if (material.HasProperty("_ZTest")) material.SetInt("_ZTest", (int)CompareFunction.Always);
            if (material.HasProperty("_ZWrite")) material.SetInt("_ZWrite", 0);

            // Always alpha-blended, so the opacity slider affects every category. At opacity 1 the
            // blend is mathematically identical to an opaque draw, so nothing looks different.
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
            if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
            return material;
        }

        private static void SetColor(Material material, Color color)
        {
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_UnlitColor")) material.SetColor("_UnlitColor", color);
        }

        /// <summary>Re-applies the current <see cref="Opacity"/> to every cached material.</summary>
        private void ApplyOpacity()
        {
            if (_materials == null) return;
            for (int i = 0; i < _materials.Length; i++)
            {
                Material material = _materials[i];
                if (material == null) continue;
                Color c = CategoryDefs[i].Color;
                SetColor(material, new Color(c.r, c.g, c.b, c.a * _opacity));
            }
        }

        private static readonly string[] ShaderCandidates =
        {
            "Universal Render Pipeline/Unlit",
            "Universal Render Pipeline/2D/Sprite-Unlit-Default",
            "Sprites/Default",
            "UI/Default",
            "Universal Render Pipeline/Lit",
            "Standard"
        };
    }
}
