using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SlopMod
{
    /// <summary>
    /// Detached debug camera (hotkey F6).
    ///
    /// Gameplay renders through the Cinemachine-driven main camera, but cutscenes activate a SEPARATE
    /// camera (<c>GameplayManager.m_cinematicCamera</c>) that the cinematic Timeline
    /// (<c>PlayableDirector</c>) animates directly - which is why moving the main camera alone did
    /// nothing visible during a cutscene. Freecam therefore drives EVERY active game camera (the main
    /// camera and the cinematic camera) and writes their transform in LateUpdate while keeping its own
    /// position, so it wins against both the Cinemachine brain and the cutscene Timeline (which write
    /// earlier in the frame). The Cinemachine brain(s) are switched off while freecam is active.
    ///
    /// Controls: hold the right mouse button to look, WASD to move, Q/E (or Space/Ctrl) down/up,
    /// Shift to move faster, mouse wheel to change the speed.
    /// </summary>
    internal sealed class FreeCam : MonoBehaviour
    {
        internal static FreeCam Instance { get; private set; }

        private const float LookSensitivity = 0.12f;

        private static Type _brainType;
        private static bool _brainTypeSearched;

        private bool _active;
        private float _speed = 10f;
        private float _yaw;
        private float _pitch;
        private Vector3 _position;
        private bool _seeded;
        private readonly List<Behaviour> _disabledBrains = new List<Behaviour>();
        private readonly List<Camera> _cameras = new List<Camera>();

        internal static bool Active { get { return Instance != null && Instance._active; } }

        internal static void EnsureExists()
        {
            if (Instance != null) return;
            GameObject go = new GameObject("SlopMod_FreeCam");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<FreeCam>();
        }

        internal static void Toggle()
        {
            EnsureExists();
            Instance.SetActive(!Instance._active);
        }

        /// <summary>Turns freecam off (no-op when it was never on). Used before teleporting the player.</summary>
        /// <param name="snapCamera">
        /// When true the gameplay camera is snapped straight back onto the player. Callers that move
        /// the player themselves right after (the teleport) pass false so the snap is not wasted.
        /// </param>
        internal static void Disable(bool snapCamera = true)
        {
            if (Instance != null && Instance._active) Instance.SetActive(false, snapCamera);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                UnityEngine.Object.Destroy(gameObject);
                return;
            }
            Instance = this;
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            RestoreBrains();
        }

        private void SetActive(bool active, bool snapCamera = true)
        {
            if (_active == active) return;

            if (active)
            {
                _disabledBrains.Clear();

                // The live camera set changes (cutscenes swap cameras), so seed lazily in LateUpdate
                // from whichever camera is actually rendering.
                _seeded = false;
                _speed = 10f;
            }
            else
            {
                // Hand the camera back to the game: re-enable the Cinemachine brain(s) the freecam
                // switched off (previously these stayed disabled, so the base camera froze where the
                // freecam was parked) and snap the gameplay camera onto the player so it resumes
                // following normally.
                RestoreBrains();
                if (snapCamera) SnapGameCameraToPlayer();
            }

            _active = active;

            if (SlopModPlugin.Log != null) SlopModPlugin.Log.LogInfo("Freecam " + (active ? "ON" : "OFF"));
        }

        /// <summary>
        /// Uses the game's own camera manager to snap the gameplay camera straight back onto the
        /// player (the same path the game takes after a teleport), so leaving freecam returns the view
        /// to where the base camera follows. No-op outside gameplay.
        /// </summary>
        private static void SnapGameCameraToPlayer()
        {
            GameplayManager gm = GameManager.m_gameplayManager;
            if (gm == null) return;

            CameraManager cameraManager = gm.m_cameraManager;
            if (cameraManager == null) return;

            cameraManager.InstantCameraMove();
        }

        private void LateUpdate()
        {
            if (!_active) return;

            CollectCameras(_cameras);
            if (_cameras.Count == 0) return;

            for (int i = 0; i < _cameras.Count; i++) DisableBrains(_cameras[i]);

            if (!_seeded) SeedFromPrimaryCamera();

            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;

            float delta = Time.unscaledDeltaTime;
            float boost = (keyboard != null && keyboard.leftShiftKey.isPressed) ? 4f : 1f;

            Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 forward = rotation * Vector3.forward;
            Vector3 right = rotation * Vector3.right;

            Vector3 move = Vector3.zero;
            if (keyboard != null)
            {
                if (keyboard.wKey.isPressed) move += forward;
                if (keyboard.sKey.isPressed) move -= forward;
                if (keyboard.dKey.isPressed) move += right;
                if (keyboard.aKey.isPressed) move -= right;
                if (keyboard.eKey.isPressed || keyboard.spaceKey.isPressed) move += Vector3.up;
                if (keyboard.qKey.isPressed || keyboard.leftCtrlKey.isPressed) move -= Vector3.up;
            }
            _position += move * (_speed * boost) * delta;

            if (mouse != null)
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f) _speed = Mathf.Clamp(_speed * (1f + scroll * 0.1f), 0.5f, 300f);

                if (mouse.rightButton.isPressed)
                {
                    Vector2 look = mouse.delta.ReadValue();
                    _yaw += look.x * LookSensitivity;
                    _pitch = Mathf.Clamp(_pitch - look.y * LookSensitivity, -89f, 89f);
                }
            }

            Quaternion finalRotation = Quaternion.Euler(_pitch, _yaw, 0f);

            // Write to every live game camera in LateUpdate: the Cinemachine brain and the cutscene
            // Timeline both write earlier in the frame, so this is the value that gets rendered.
            for (int i = 0; i < _cameras.Count; i++)
            {
                Transform cam = _cameras[i].transform;
                cam.position = _position;
                cam.rotation = finalRotation;
            }
        }

        /// <summary>
        /// Gathers the game cameras that are currently rendering: the Cinemachine main camera and the
        /// cutscene camera (activated + animated by the cinematic Timeline). Falls back to every enabled
        /// camera when none of the known ones are live.
        /// </summary>
        private static void CollectCameras(List<Camera> results)
        {
            results.Clear();

            AddCamera(results, Camera.main);

            GameplayManager gm = GameManager.m_gameplayManager;
            if (gm != null)
            {
                CameraManager cameraManager = gm.m_cameraManager;
                if (cameraManager != null) AddCamera(results, cameraManager.m_mainCamera);

                AddCamera(results, gm.m_cinematicCamera);
            }

            if (results.Count == 0)
            {
                Camera[] all = Camera.allCameras;
                for (int i = 0; i < all.Length; i++) AddCamera(results, all[i]);
            }
        }

        private static void AddCamera(List<Camera> results, Camera camera)
        {
            if (camera == null || !camera.enabled || !camera.gameObject.activeInHierarchy) return;
            for (int i = 0; i < results.Count; i++)
            {
                if (results[i] == camera) return;
            }
            results.Add(camera);
        }

        /// <summary>Seeds the freecam from the live camera that is actually on top (highest depth).</summary>
        private void SeedFromPrimaryCamera()
        {
            Camera primary = _cameras[0];
            for (int i = 1; i < _cameras.Count; i++)
            {
                if (_cameras[i].depth > primary.depth) primary = _cameras[i];
            }

            _position = primary.transform.position;
            Vector3 euler = primary.transform.eulerAngles;
            _yaw = euler.y;
            _pitch = NormalizePitch(euler.x);
            _seeded = true;
        }

        private void DisableBrains(Camera camera)
        {
            Type brainType = BrainType();
            if (brainType == null) return;

            Behaviour brain = camera.GetComponent(brainType) as Behaviour;
            if (brain == null || !brain.enabled) return;

            brain.enabled = false;
            if (!_disabledBrains.Contains(brain)) _disabledBrains.Add(brain);
        }

        private void RestoreBrains()
        {
            for (int i = 0; i < _disabledBrains.Count; i++)
            {
                if (_disabledBrains[i] != null) _disabledBrains[i].enabled = true;
            }
            _disabledBrains.Clear();
        }

        /// <summary>Finds Cinemachine's brain type without taking a hard assembly reference.</summary>
        private static Type BrainType()
        {
            if (_brainTypeSearched) return _brainType;
            _brainTypeSearched = true;

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type[] types;
                try { types = assemblies[i].GetTypes(); }
                catch { continue; }

                for (int j = 0; j < types.Length; j++)
                {
                    if (types[j].Name == "CinemachineBrain")
                    {
                        _brainType = types[j];
                        return _brainType;
                    }
                }
            }
            return _brainType;
        }

        private static float NormalizePitch(float x)
        {
            if (x > 180f) x -= 360f;
            return Mathf.Clamp(x, -89f, 89f);
        }
    }
}
