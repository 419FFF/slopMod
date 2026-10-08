using System.Collections.Generic;
using UnityEngine;

namespace SlopMod
{
    /// <summary>
    /// Dev-tool teleports:
    ///   * Teleport to level spawn   -> the level's default spawn points (the game's own path).
    ///   * Teleport to next cutscene -> the world position where the next cinematic will actually
    ///     play. The legacy attempt used the cinematic checkpoint's <c>m_spawnPlayer</c>, which is
    ///     almost always <see cref="Vector3.zero"/> / flagged <c>m_ignoreThisPosition</c>, so it just
    ///     dropped the player at the world origin (which looks like the default spawn). The real
    ///     location is the destination of the mission that precedes the next CinematicData checkpoint
    ///     (<c>MisionDataScriptableObject.m_destinationPoint</c>), with the game's current objective
    ///     target (<c>GameplayManager.m_episodeTargetPosition</c>, what the radar points at) next.
    /// </summary>
    internal static class TeleportTools
    {
        private static readonly Vector3 StandOffset = new Vector3(0f, 0.5f, 0f);

        // --- feature: teleport to level spawn ---

        internal static void TeleportToLevelSpawn()
        {
            GameplayManager gm = GameManager.m_gameplayManager;
            if (gm == null) { Warn("Teleport: no active gameplay."); return; }

            if (GameplayManager.m_currentLevelManager == null)
            {
                Warn("Teleport: no current level manager (are you in a level?).");
                return;
            }

            gm.TeleportPlayersToSpawnPoints();
            Info("Teleported all players to the level spawn points.");
        }

        // --- feature: teleport to next cutscene ---

        internal static void TeleportToNextCutscene()
        {
            GameplayManager gm = GameManager.m_gameplayManager;
            if (gm == null) { Warn("Teleport: no active gameplay."); return; }

            EpisodeManager em = gm.m_episodeManager;
            if (em == null) { Warn("Teleport: no active episode."); return; }

            Vector3 target;
            if (!TryResolveCutsceneTarget(gm, em, out target))
            {
                Warn("Teleport: no upcoming cutscene / objective target found.");
                return;
            }

            int moved = TeleportAllPlayers(gm, target);
            Info("Teleported " + moved + " player(s) to the next-cutscene location " + target.ToString("0.0") + ".");
        }

        // --- feature: teleport the player(s) to the freecam (F6) position ---

        /// <summary>
        /// Moves the player(s) to wherever the detached debug camera (F6) currently floats. Freecam
        /// drives the main camera by hand, so we leave it first (the restored Cinemachine brain can
        /// then snap the game camera onto the player) and teleport with an instant camera move. The
        /// players land exactly on the camera position (no stand / spread offset) and drop to the floor
        /// themselves.
        /// </summary>
        internal static void TeleportPlayersToFreecam()
        {
            if (!FreeCam.Active)
            {
                Warn("Teleport to freecam: freecam is not active (press F6 first).");
                return;
            }

            Camera camera = Camera.main;
            if (camera == null) { Warn("Teleport to freecam: no main camera."); return; }

            GameplayManager gm = GameManager.m_gameplayManager;
            if (gm == null) { Warn("Teleport to freecam: no active gameplay."); return; }

            Vector3 target = camera.transform.position;

            // Leave freecam first so the restored brain owns the camera again; the teleport below
            // does its own instant camera move, so we skip the snap here (snapCamera: false).
            FreeCam.Disable(false);

            int moved = TeleportAllPlayers(gm, target, false);
            Info("Teleported " + moved + " player(s) to the freecam position " + target.ToString("0.0") + ".");
        }

        private static bool TryResolveCutsceneTarget(GameplayManager gm, EpisodeManager em, out Vector3 target)
        {
            // 1) Destination of the mission that immediately precedes the next cinematic checkpoint.
            if (TryNextCinematicMissionDestination(em, out target)) return true;

            // 2) The game's own current objective target (where the radar points).
            if (gm.m_episodeTargetPosition != Vector3.zero) { target = gm.m_episodeTargetPosition; return true; }

            // 3) The next cinematic's own spawn points, if it actually places players.
            if (TryNextCinematicSpawn(gm, em, out target)) return true;

            // 4) The current checkpoint's spawn.
            PlayerController player = FirstPlayer(gm);
            EpisodeCheckPointBaseScriptableObject current = em.GetCurrentCheckpoint();
            if (player != null && current != null)
            {
                Vector3 spawn = SpawnForPlayer(current, player);
                if (spawn != Vector3.zero) { target = spawn; return true; }
            }

            target = Vector3.zero;
            return false;
        }

        private static bool TryNextCinematicMissionDestination(EpisodeManager em, out Vector3 target)
        {
            target = Vector3.zero;
            EpisodeScriptableObject episode = em.CurrenEpisode;
            if (episode == null || episode.m_checkPointsList == null) return false;

            List<Checkpoints> list = episode.m_checkPointsList;
            int current = CurrentCheckpointIndex(em, list);

            int nextCinematic = -1;
            for (int i = current + 1; i < list.Count; i++)
            {
                if (list[i].m_checkpoint is CinematicDataScriptableObject) { nextCinematic = i; break; }
            }
            if (nextCinematic < 0) return false;

            for (int j = nextCinematic - 1; j >= 0; j--)
            {
                MisionDataScriptableObject mission = list[j].m_checkpoint as MisionDataScriptableObject;
                if (mission != null && mission.m_destinationPoint != Vector3.zero)
                {
                    target = mission.m_destinationPoint;
                    return true;
                }
            }
            return false;
        }

        private static bool TryNextCinematicSpawn(GameplayManager gm, EpisodeManager em, out Vector3 target)
        {
            target = Vector3.zero;
            EpisodeScriptableObject episode = em.CurrenEpisode;
            if (episode == null || episode.m_checkPointsList == null) return false;

            List<Checkpoints> list = episode.m_checkPointsList;
            int current = CurrentCheckpointIndex(em, list);
            PlayerController player = FirstPlayer(gm);
            if (player == null) return false;

            for (int i = current + 1; i < list.Count; i++)
            {
                if (list[i].m_checkpoint is CinematicDataScriptableObject)
                {
                    Vector3 spawn = SpawnForPlayer(list[i].m_checkpoint, player);
                    if (spawn != Vector3.zero) { target = spawn; return true; }
                    return false;
                }
            }
            return false;
        }

        private static int CurrentCheckpointIndex(EpisodeManager em, List<Checkpoints> list)
        {
            EpisodeCheckPointBaseScriptableObject current = em.GetCurrentCheckpoint();
            if (current != null)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].m_checkpoint == current) return i;
                }
            }

            // The save's episodePhase is the current checkpoint index (EpisodeManager sets
            // _currentCheckpoint = episodeProgress.episodePhase).
            if (GameManager.m_saveManager != null)
            {
                EpisodesProgress progress = GameManager.m_saveManager.GetEpisodesProgress();
                if (progress.episodePhase > 0 && progress.episodePhase < list.Count) return progress.episodePhase;
            }
            return 0;
        }

        // --- helpers ---

        private static int TeleportAllPlayers(GameplayManager gm, Vector3 target)
        {
            // Spawn / cutscene teleports nudge the players apart so they do not land inside each other.
            return TeleportAllPlayers(gm, target, true);
        }

        private static int TeleportAllPlayers(GameplayManager gm, Vector3 target, bool addOffsets)
        {
            PlayerController[] players = gm.m_playerControllers;
            if (players == null) return 0;

            int moved = 0;
            for (int i = 0; i < players.Length; i++)
            {
                PlayerController player = players[i];
                if (player == null || !player.gameObject.activeInHierarchy) continue;

                Vector3 offset = addOffsets ? StandOffset + PlayerOffset(moved) : Vector3.zero;
                Teleport(player, target + offset);
                moved++;
            }
            return moved;
        }

        private static Vector3 PlayerOffset(int index)
        {
            float x = (index % 2 == 0) ? -1.2f : 1.2f;
            float z = (index < 2) ? 0f : 1.2f;
            return new Vector3(x, 0f, z);
        }

        private static PlayerController FirstPlayer(GameplayManager gm)
        {
            PlayerController player = gm.GetFirstPlayerControllerByInputID();
            if (player != null) return player;

            if (gm.m_playerControllers != null)
            {
                for (int i = 0; i < gm.m_playerControllers.Length; i++)
                {
                    if (gm.m_playerControllers[i] != null) return gm.m_playerControllers[i];
                }
            }
            return null;
        }

        private static Vector3 SpawnForPlayer(EpisodeCheckPointBaseScriptableObject checkpoint, PlayerController player)
        {
            SpawnPlayer[] spawns = checkpoint.m_spawnPlayer;
            if (spawns == null || spawns.Length == 0) return Vector3.zero;

            for (int i = 0; i < spawns.Length; i++)
            {
                if (spawns[i].m_playerID == player.m_playerID) return spawns[i].m_spawnPoint;
            }
            return spawns[0].m_spawnPoint;
        }

        private static void Teleport(PlayerController player, Vector3 position)
        {
            if (player.m_playerMovement != null)
            {
                player.m_playerMovement.TeleportTo(position, true);
            }
            else if (player.m_kinematicCharacterMotor != null)
            {
                player.m_kinematicCharacterMotor.SetPosition(position, true);
            }
        }

        private static void Info(string message)
        {
            if (SlopModPlugin.Log != null) SlopModPlugin.Log.LogInfo(message);
        }

        private static void Warn(string message)
        {
            if (SlopModPlugin.Log != null) SlopModPlugin.Log.LogWarning(message);
        }
    }
}
