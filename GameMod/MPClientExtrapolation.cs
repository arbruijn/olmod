﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using Overload;
using UnityEngine;
using UnityEngine.Networking;

// Commented out code represents an attempt at making ships respond to physics.  It didn't work, and will be revisited in the future.
namespace GameMod {
    //public class MPClientExtrapolation_Velocities {
    //    public Vector3 Velocity;
    //    public Vector3 AngularVelocity;
    //    public float Drag;
    //    public Vector3 LocalPosition;
    //    public Quaternion Rotation;
    //}

    public class MPClientExtrapolation {
        public const int MAX_PING = 1000;
        public static List<Rigidbody> bodies_to_resolve = new List<Rigidbody>();
        //        public static Dictionary<Player, MPClientExtrapolation_Velocities> players_to_resolve = new Dictionary<Player, MPClientExtrapolation_Velocities>();
        public static Dictionary<string, Queue<Vector3>> player_positions = new Dictionary<string, Queue<Vector3>>();

        public static void LerpProjectile(Projectile c_proj) {
            // Bail if:
            if (
                !GameplayManager.IsMultiplayerActive ||          // it's not a MP game (no network)
                Network.isServer ||                              // if it's the server (not necessary)
                MPObserver.Enabled ||                            // if the current player is an observer (not necessary for observer games)
                c_proj.m_owner_player.isLocalPlayer ||           // it's the local player (not necessary)
                c_proj.m_type == ProjPrefab.missile_creeper ||   // a creeper (handled by creeper/TB sync)
                c_proj.m_type == ProjPrefab.missile_timebomb ||  // a time bomb (handled by creeper/TB sync)
                c_proj.m_type == ProjPrefab.proj_flak_cannon ||  // a flak projectile (lifespan is too short to make a difference)
                c_proj.m_type == ProjPrefab.proj_driller ||      // a driller projectile (projectile is too fast to make a difference)
                c_proj.m_type == ProjPrefab.proj_driller_mini || // do not lerp projectile children
                c_proj.m_type == ProjPrefab.missile_smart_mini ||
                c_proj.m_type == ProjPrefab.missile_devastator_mini
            ) {
                return;
            }

            // Queue to simulate physics.
            bodies_to_resolve.Add(c_proj.c_rigidbody);
        }

        // Factor is 0 for observers, and the average of the desired 1.0 for ship positions and 0.5 for projectile positions for non-observers.
        public static float GetFactor() {
            return MPObserver.Enabled ? 0f : 0.75f;
        }

        // How far ahead to advance weapons, in seconds.
        public static float GetWeaponExtrapolationTime() {
            if (MPObserver.Enabled || Menus.mms_lag_compensation == 0 || Menus.mms_lag_compensation == 1) {
                return 0f;
            }
            float time_ms = Math.Min(GameManager.m_local_player.m_avg_ping_ms,
                                      Menus.mms_weapon_lag_compensation_max);
            return (Menus.mms_weapon_lag_compensation_scale / 100f) * time_ms / 1000f;

        }

        // How far ahead to advance ships, in seconds.
        public static float GetShipExtrapolationTime() {
            if (MPObserver.Enabled || Menus.mms_lag_compensation == 0 || Menus.mms_lag_compensation == 2) {
                return 0f;
            }
            float time_ms = Math.Min(GameManager.m_local_player.m_avg_ping_ms,
                                      Menus.mms_ship_lag_compensation_max);
            return (Menus.mms_ship_lag_compensation_scale / 100f) * time_ms / 1000f;

        }

        public static void InitForMatch() {
            bodies_to_resolve.Clear();
            //players_to_resolve.Clear();
        }
    }

    [HarmonyPatch(typeof(NetworkMatch), "InitBeforeEachMatch")]
    class MPClientExtrapolation_InitBeforeEachMatch {
        private static void Postfix() {
            MPClientExtrapolation.InitForMatch();
        }
    }

    [HarmonyPatch(typeof(PlayerShip), "FixedUpdateAll")]
    class MPClientExtrapolation_FixedUpdateAll {
        private static void Postfix() {
            if (MPClientExtrapolation.bodies_to_resolve.Count == 0/* && MPClientExtrapolation.players_to_resolve.Count == 0 */) {
                return;
            }

            var amount = MPClientExtrapolation.GetWeaponExtrapolationTime();
            if (amount <= 0f) {
                return;
            }

            //foreach (var kvp in MPClientExtrapolation.players_to_resolve) {
            //    var player = kvp.Key;
            //    var velocities = kvp.Value;
            //    player.c_player_ship.c_transform.localPosition = velocities.LocalPosition;
            //    player.c_player_ship.c_transform.rotation = velocities.Rotation;
            //    player.c_player_ship.c_mesh_collider_trans.localPosition = player.c_player_ship.c_transform.localPosition;
            //}

            NetworkSim.PauseAllRigidBodiesExcept(null);

            foreach (var body in MPClientExtrapolation.bodies_to_resolve) {
                if (NetworkSim.m_paused_rigid_bodies.ContainsKey(body)) {
                    var state = NetworkSim.m_paused_rigid_bodies[body];
                    body.isKinematic = false;
                    body.velocity = state.m_velocity;
                    body.angularVelocity = state.m_angular_velocity;
                }
            }

            //foreach (var kvp in MPClientExtrapolation.players_to_resolve) {
            //    var player = kvp.Key;
            //    var velocities = kvp.Value;
            //    player.c_player_ship.c_rigidbody.isKinematic = false;
            //    player.c_player_ship.c_rigidbody.velocity = velocities.Velocity;
            //    player.c_player_ship.c_rigidbody.angularVelocity = velocities.AngularVelocity;
            //    player.c_player_ship.c_rigidbody.drag = 0f;
            //}

            Physics.Simulate(amount);

            //foreach (var kvp in MPClientExtrapolation.players_to_resolve) {
            //    var player = kvp.Key;
            //    var velocities = kvp.Value;
            //    player.c_player_ship.c_rigidbody.isKinematic = false;
            //    player.c_player_ship.c_rigidbody.velocity = Vector3.zero;
            //    player.c_player_ship.c_rigidbody.angularVelocity = Vector3.zero;
            //    player.c_player_ship.c_rigidbody.drag = velocities.Drag;
            //}

            NetworkSim.ResumeAllPausedRigidBodies();

            MPClientExtrapolation.bodies_to_resolve.Clear();
            //MPClientExtrapolation.players_to_resolve.Clear();
        }
    }

    [HarmonyPatch(typeof(ProjectileManager), "FireProjectile")]
    class MPClientExtrapolation_FireProjectile {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> codes) {
            foreach (var code in codes) {
                // Replace where the projectile is added to the list of projectiles with our own function.
                if (code.opcode == OpCodes.Callvirt && ((MethodInfo)code.operand).Name == "Fire") {
                    yield return code;
                    yield return new CodeInstruction(OpCodes.Ldloc_1);
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(MPClientExtrapolation), "LerpProjectile"));
                    Debug.Log("Patched FireProjectile for MPClientExtrapolation");
                    continue;
                }

                yield return code;
            }
        }
    }

    class MPClientShipReckoning{
        // ship interpolation OR extrapolation for clients
        public static float m_last_update_time;
        public static float m_last_frame_time;

        // simple statistic
        public static float m_compensation_sum;
        public static int m_compensation_count;
        public static int m_compensation_interpol_count;
        public static float m_compensation_last;

        // simple ring buffer, use size 4 which is a power of two, so the % 4 becomes simple & 3
        private static NewPlayerSnapshotToClientMessage[] m_last_messages_ring = new NewPlayerSnapshotToClientMessage[4];
        private static int m_last_messages_ring_count = 0;          // number of elements in the ring buffer
        private static int m_last_messages_ring_pos_last = 3;       // position of the last added element
        private static object m_last_messages_lock = new object();  // lock used to guard access to buffer contents AND m_last_update_time

        private static void EnqueueToRing(NewPlayerSnapshotToClientMessage msg, bool wasOld = false)
        {
            // For old snapshots, we will fill in the ship velocity if that information is available.
            if (wasOld) {
                var last_snapshots = m_last_messages_ring[m_last_messages_ring_pos_last];
                foreach (var snapshot in msg.m_snapshots) {
                    var last_snapshot = last_snapshots.m_snapshots.FirstOrDefault(m => m.m_net_id == snapshot.m_net_id);
                    if (last_snapshot != null) {
                        snapshot.m_vel = (snapshot.m_pos - last_snapshot.m_pos) / Time.fixedDeltaTime;
                        snapshot.m_vrot = (Quaternion.Inverse(snapshot.m_rot) * Quaternion.SlerpUnclamped(last_snapshot.m_rot, snapshot.m_rot, 1f / Time.fixedDeltaTime)).eulerAngles;
                    }
                }
            }

            m_last_messages_ring_pos_last = (m_last_messages_ring_pos_last + 1) & 3;
            m_last_messages_ring[m_last_messages_ring_pos_last] = msg;
            if (m_last_messages_ring_count < 4) {
                m_last_messages_ring_count++;
            }
            //Debug.LogFormat("Adding {0} at {1}, have {2}", msg.m_timestamp, Time.time, m_last_messages_ring_count);
        }

        // Clear the contents of the ring buffer
        private static void ClearRing()
        {
            m_last_messages_ring_pos_last = 3;
            m_last_messages_ring_count = 0;
        }

        // Prepare for a new match
        // resets all history data and metadata we keep
        public static void ResetForNewMatch()
        {
            lock (m_last_messages_lock) {
                ClearRing();
                m_last_update_time = Time.time;
                m_last_frame_time = Time.time;
            }
            m_compensation_sum = 0.0f;
            m_compensation_count = 0;
            m_compensation_interpol_count = 0;
            m_compensation_last = Time.time;
        }

        // add a AddNewPlayerSnapshot(NewPlayerSnapshotToClientMessage
        // this should be called as soon as possible after the message arrives
        // This function adds the message into the ring buffer, and
        // also implements the time sync algorithm between the message sequence and
        // the local render time.
        //
        // It is safe to be called from an arbitrary thread, as accesses are
        // guareded by a lock.
        public static void AddNewPlayerSnapshot(NewPlayerSnapshotToClientMessage msg, bool wasOld = false)
        {
            lock (m_last_messages_lock) {
                if (m_last_messages_ring_count == 0) {
                    // first packet
                    EnqueueToRing(msg);
                    m_last_update_time = Time.time;
                } else {
                    // next in sequence, as we expected
                    EnqueueToRing(msg, wasOld);
                    // this assumes the server sends 60Hz
                    // during time dilation (timebombs!) this is not true,
                    // it will actually send data packets _worth_ of 16.67ms real time, spread out
                    // to longer intervals such as 24 ms.
                    // However, this is not a problem, since the reference clock we sync to
                    // is Time.time which has the timeScale already applied.
                    // That means the 24ms tick will be seen as 16.67 in Time.time,
                    // and everything cancles itself out nicely
                    m_last_update_time += Time.fixedDeltaTime;
                }
                // check if the time base is still plausible
                float delta = (Time.time - m_last_update_time) / Time.fixedDeltaTime; // in ticks
                // allow a sliding window to catch up for latency jitter
                float frameSoftSyncLimit = 2.0f; ///hard-sync if we're off by more than that many physics ticks
                if (delta < -frameSoftSyncLimit || delta > frameSoftSyncLimit) {
                    // hard resync
                    // Debug.LogFormat("hard resync by {0} frames", delta);
                    m_last_update_time = Time.time;
                } else {
                    // soft resync
                    float smoothing_factor = 0.1f;
                    m_last_update_time += smoothing_factor * delta * Time.fixedDeltaTime;
                }
            } // end lock
        }

        static private MethodInfo _Client_GetPlayerFromNetId_Method = AccessTools.Method(typeof(Client), "GetPlayerFromNetId");

        public static NewPlayerSnapshot GetPlayerSnapshot(NewPlayerSnapshotToClientMessage msg, Player p)
        {
            for (int i = 0; i < msg.m_num_snapshots; i++)
            {
                NewPlayerSnapshot playerSnapshot = msg.m_snapshots[i];
                Player candidate = (Player)_Client_GetPlayerFromNetId_Method.Invoke(null, new object[] {playerSnapshot.m_net_id});

                if (candidate == p)
                {
                    return playerSnapshot;
                }
            }
            return null;
        }

        // Deal with the respawn. Return true if the player should not be moved around
        private static bool HandlePlayerRespawn(Player player, NewPlayerSnapshot snapshot)
        {
            // this logic was in vanilla Player.LerpRemotePlayer()
            if (player.m_lerp_wait_for_respawn_pos) {
                float num = Vector3.Distance(snapshot.m_pos, player.m_lerp_respawn_pos);
                float num2 = Vector3.Distance(snapshot.m_pos, player.m_lerp_death_pos);
                if (num >= num2) {
                    // still special case for respawning
                    return true;
                }

                player.m_lerp_wait_for_respawn_pos = false;
            }
            return false;
        }

        public static void interpolatePlayer(Player player, NewPlayerSnapshot A, NewPlayerSnapshot B, float t)
        {
            if (HandlePlayerRespawn(player,A)) {
                return;
            }
            player.c_player_ship.c_transform.localPosition = Vector3.LerpUnclamped(A.m_pos, B.m_pos, t);
            player.c_player_ship.c_transform.rotation = Quaternion.SlerpUnclamped(A.m_rot, B.m_rot, t);
            player.c_player_ship.c_mesh_collider_trans.localPosition = player.c_player_ship.c_transform.localPosition;
        }

        public static void extrapolatePlayer(Player player, NewPlayerSnapshot snapshot, float t){
            if (HandlePlayerRespawn(player,snapshot)) {
                return;
            }
            player.c_player_ship.c_transform.localPosition = Vector3.LerpUnclamped(snapshot.m_pos, snapshot.m_pos+snapshot.m_vel, t);
            player.c_player_ship.c_transform.rotation = Quaternion.SlerpUnclamped(snapshot.m_rot, snapshot.m_rot*Quaternion.Euler(snapshot.m_vrot), t);
            player.c_player_ship.c_mesh_collider_trans.localPosition = player.c_player_ship.c_transform.localPosition;
        }

        // Called per frame, moves ships along their interpolation/extrapolation motions
        public static void updatePlayerPositions()
        {
            float now = Time.time; // needs to be the same time source we use for m_last_update_time
            NewPlayerSnapshotToClientMessage msgA = null; // interpolation: start
            NewPlayerSnapshotToClientMessage msgB = null; // interpolation: end, extrapolation start
            float interpolate_factor = 0.0f;              // interpolation: factor in [0,1]
            float delta_t = 0.0f;
            int interpolate_ticks = 0;
            bool do_interpolation = false;

            // find out which case we have, and get the relevant snapshot message(s)
            lock (m_last_messages_lock) {
                /*
                for (int xxx=0; xxx<m_last_messages_ring_count; xxx++) {
                    Debug.LogFormat("having snapshot from {0} represents {1}", m_last_messages_ring[(m_last_messages_ring_pos_last + 4 - xxx)&3].m_timestamp, m_last_update_time - xxx* Time.fixedDeltaTime);
                }
                */
                if (m_last_messages_ring_count < 1) {
                    // we do not have any snapshot messages...
                    return;
                }

                // NOTE: now and m_last_update_time indirectly have timeScale already applied, as they are based on Time.time
                //       we need to adjust just the ping and the mms_ship_max_interpolate_frames offset...
                //       Also note that the server still sends the unscaled velocities.
                delta_t = now + Time.timeScale * MPClientExtrapolation.GetShipExtrapolationTime() - m_last_update_time;
                // if we want interpolation, add this as a _negative_ offset
                // we use delta_t=0  as the base for from which we extrapolate into the future
                delta_t -= (Menus.mms_lag_compensation_ship_added_lag / 1000f) * Time.timeScale;
                // it might sound absurd, but after this point, the Time.fixedDeltaTime is correct
                // and MUST NOT be scaled by timeScale. The data packets do contain 16.67ms of
                // movement each, we already have taken the time dilation into account above...
                // time difference in physics ticks
                float delta_ticks = delta_t / Time.fixedDeltaTime;
                // the number of frames we need to interpolate into
                // <= 0 means no interpolation at all,
                // 1 would mean we use the second most recent and the most recent snapshot, and so on...
                interpolate_ticks = -(int)Mathf.Floor(delta_ticks);
                // do we need to do interpolation?
                do_interpolation = (interpolate_ticks > 0);

                if (do_interpolation) {
                    // we need interpolate_ticks + 1 elements in the ring buffer
                    // NOTE: in the code below, the index [(m_last_messages_ring_pos_last + 4 - i) &3]
                    //       effectively acceses the i-ith most recent element (i starting by 0)
                    //       since 4-(i-1) == 4-i+ 1 = 5-i, 5-i references the next older one
                    if ( interpolate_ticks < m_last_messages_ring_count ) {
                        msgA = m_last_messages_ring[(m_last_messages_ring_pos_last + 4 - interpolate_ticks) & 3];
                        msgB = m_last_messages_ring[(m_last_messages_ring_pos_last + 5 - interpolate_ticks) & 3];
                        interpolate_factor = delta_ticks - Mathf.Floor(delta_ticks);
                    } else {
                        // not enough packets received so far
                        // "extrapolate" into the past
                        do_interpolation = false;
                        // get the oldest snapshot we have
                        msgB =  m_last_messages_ring[(m_last_messages_ring_pos_last + 5 - m_last_messages_ring_count) & 3];
                        // offset the time for the extrapolation
                        // delta_t is currently relative to the most recent element we have,
                        // but we need it relative to msgA
                        delta_t += Time.fixedDeltaTime * (m_last_messages_ring_count-1);
                    }
                } else {
                    // extrapolation case
                    // use the most recently received snapshot
                    msgB = m_last_messages_ring[m_last_messages_ring_pos_last];
                }
            } // lock
            m_last_frame_time = now;

            /*
            Debug.LogFormat("At: {0} Setting: {1} IntFrames: {2}, dt: {3}, IntFact {4}",now,Menus.mms_ship_max_interpolate_frames, interpolate_ticks, delta_t, interpolate_factor);
            if (interpolate_ticks > 0) {
                Debug.LogFormat("Using A from {0}", msgA.m_timestamp);
                Debug.LogFormat("Using B from {0}", msgB.m_timestamp);
            } else {
                Debug.LogFormat("Using B from {0}", msgB.m_timestamp);
            }
            */

            // keep statistics
            m_compensation_sum += delta_t;
            m_compensation_count++;
            // NOTE: one can't replace(interpolate_ticks > 0) by do_interpolation here,
            //       because even in the (interpolate_ticks > 0) case the code above could
            //       have reset do_interpolation to false because we technically want
            //       the "extrapolation" into the past thing, but we don't want to count that
            //       as extrapolation...
            m_compensation_interpol_count += (interpolate_ticks > 0)?1:0;
            if (Time.time >= m_compensation_last + 5.0 && m_compensation_count > 0) {
                //Debug.LogFormat("ship lag compensation over last {0} frames: {1}ms / {2} physics ticks, {3} interpolation ({4}%)",
                //                m_compensation_count, 1000.0f* (m_compensation_sum/ m_compensation_count),
                //                (m_compensation_sum/m_compensation_count)/Time.fixedDeltaTime,
                //                m_compensation_interpol_count,
                //                100.0f*((float)m_compensation_interpol_count/(float)m_compensation_count));
                m_compensation_sum = 0.0f;
                m_compensation_count = 0;
                m_compensation_interpol_count = 0;
                m_compensation_last = Time.time;
            }

            // actually apply the operation to each player
            foreach (Player player in Overload.NetworkManager.m_Players)
            {
                if (player != null && !player.isLocalPlayer && !player.m_spectator)
                {
                    // do the actual interpolation or extrapolation, as calculated above
                    if (do_interpolation) {
                        NewPlayerSnapshot A = GetPlayerSnapshot(msgA, player);
                        NewPlayerSnapshot B = GetPlayerSnapshot(msgB, player);
                        if(A != null && B != null){
                            interpolatePlayer(player, A, B, interpolate_factor);
                        }
                    } else {
                        NewPlayerSnapshot snapshot = GetPlayerSnapshot(msgB, player);
                        if(snapshot != null){
                            extrapolatePlayer(player, snapshot, delta_t);
                        }
                    }
                }
            }
        }
    }

    // called per frame
    [HarmonyPatch(typeof(Client), "InterpolateRemotePlayers")]
    class MPClientExtrapolation_ClientUpdate{
        static bool Prefix(){
            // This function is called once per frame from Client.Update()
            if (Overload.NetworkManager.IsServer() || NetworkMatch.m_match_state != MatchState.PLAYING) {
                // no need to move ships around
                return false;
            }
            MPClientShipReckoning.updatePlayerPositions();
            return false;
        }

    }

    // called per physics update
    [HarmonyPatch(typeof(Client), "FixedUpdate")]
    class MPClientExtrapolation_ClientFixedUpdate{
        static bool Prefix(){
            if (Overload.NetworkManager.IsServer() || NetworkMatch.m_match_state != MatchState.PLAYING) {
                return true;
            }
            // Client.FixedUpdate() did nothing except call UpdateInterpolationBuffer,
            // which we now ignore
            return false;
        }
    }

    // called when connecting to the server
    [HarmonyPatch(typeof(Client), "Connect")]
    class MPClientExtrapolation_Connect {
        private static void Postfix()
        {
            if (Overload.NetworkManager.IsServer()) {
                return;
            }
            MPClientShipReckoning.ResetForNewMatch();
        }
    }

    /*[HarmonyPatch(typeof(Client), "UpdateInterpolationBuffer")]
    class MPClientExtrapolation_UpdateInterpolationBuffer {

        static bool Prefix(){
            if (Client.m_InterpolationBuffer[0] == null)
            {
                return true;
            }
            else
            {
                if(Client.m_PendingPlayerSnapshotMessages.Count < 1){
                    return false;
                }
                else if(Client.m_PendingPlayerSnapshotMessages.Count == 1){
                    Client.m_InterpolationBuffer[1] = Client.m_InterpolationBuffer[2];
                    Client.m_InterpolationBuffer[2] = Client.m_PendingPlayerSnapshotMessages.Dequeue();
                }
                else{
                    while (Client.m_PendingPlayerSnapshotMessages.Count > 2)
                    {
                        Client.m_PendingPlayerSnapshotMessages.Dequeue();
                    }
                    Client.m_InterpolationBuffer[1] = Client.m_PendingPlayerSnapshotMessages.Dequeue();
                    Client.m_InterpolationBuffer[2] = Client.m_PendingPlayerSnapshotMessages.Dequeue();
                }
                Client.m_InterpolationStartTime = Time.time;

                return false;
            }
        }
    }*/

    /*[HarmonyPatch(typeof(Client), "InterpolateRemotePlayers")]
    class MPClientExtrapolation_InterpolateRemotePlayers {
        static private MethodInfo _Client_GetPlayerSnapshotFromInterpolationBuffer_Method = AccessTools.Method(typeof(Client), "GetPlayerSnapshotFromInterpolationBuffer");

        static bool Prefix() {
            if (Client.m_InterpolationBuffer[0] == null || Client.m_InterpolationBuffer[1] == null || Client.m_InterpolationBuffer[2] == null) {
                return true;
            }
            float num = CalculateLerpParameter();
            PlayerSnapshotToClientMessage msg;
            PlayerSnapshotToClientMessage msg2;
            msg = Client.m_InterpolationBuffer[1];
            msg2 = Client.m_InterpolationBuffer[2];
            foreach (Player player in Overload.NetworkManager.m_Players) {
                if (player != null && !player.isLocalPlayer && !player.m_spectator) {
                    PlayerSnapshot playerSnapshotFromInterpolationBuffer = (PlayerSnapshot)_Client_GetPlayerSnapshotFromInterpolationBuffer_Method.Invoke(null, new object[] { player, msg });
                    PlayerSnapshot playerSnapshotFromInterpolationBuffer2 = (PlayerSnapshot)_Client_GetPlayerSnapshotFromInterpolationBuffer_Method.Invoke(null, new object[] { player, msg2 });
                    if (playerSnapshotFromInterpolationBuffer != null && playerSnapshotFromInterpolationBuffer2 != null) {
                        LerpRemotePlayer(player, playerSnapshotFromInterpolationBuffer, playerSnapshotFromInterpolationBuffer2, num);
                    }
                }
            }
            return false;
        }

        static void LerpRemotePlayer(Player player, PlayerSnapshot A, PlayerSnapshot B, float t) {
            if (player.m_lerp_wait_for_respawn_pos) {
                player.LerpRemotePlayer(A, B, t);
                return;
            }

            // Lookahead in frames.
            float lookahead = MPClientExtrapolation.GetShipExtrapolationTime() / Time.fixedDeltaTime;

            // reduce oversteer by extrapolating less for rotation
            var rot_lookahead = lookahead * .5f;

            player.c_player_ship.c_transform.localPosition = Vector3.LerpUnclamped(A.m_pos, B.m_pos, t + lookahead);
            player.c_player_ship.c_transform.rotation = Quaternion.SlerpUnclamped(A.m_rot, B.m_rot, t + rot_lookahead);
            player.c_player_ship.c_mesh_collider_trans.localPosition = player.c_player_ship.c_transform.localPosition;

            //// Bail if we're observing.
            //var factor = MPClientExtrapolation.GetFactor();
            //if (factor == 0f || GameManager.m_local_player.m_avg_ping_ms <= 0f) {
            //    // Reposition ship 1 frame ahead to compensate for showing old position data.
            //    __instance.c_player_ship.c_transform.localPosition = Vector3.LerpUnclamped(A.m_pos, B.m_pos, t + 1);
            //    __instance.c_player_ship.c_transform.rotation = Quaternion.SlerpUnclamped(A.m_rot, B.m_rot, t + 1);
            //    __instance.c_player_ship.c_mesh_collider_trans.localPosition = __instance.c_player_ship.c_transform.localPosition;
            //    return false;
            //}

            //// Queue to simulate physics.
            //B.m_rot.ToAngleAxis(out var B_angle, out var B_axis);
            //A.m_rot.ToAngleAxis(out var A_angle, out var A_axis);
            //MPClientExtrapolation.players_to_resolve[__instance] = new MPClientExtrapolation_Velocities {
            //    Velocity = __instance.c_player_ship.c_rigidbody.velocity = (B.m_pos - A.m_pos) / Time.fixedDeltaTime,
            //    AngularVelocity = ((B_angle * B_axis * Mathf.Deg2Rad) / Time.fixedDeltaTime) - ((A_angle * A_axis * Mathf.Deg2Rad) / Time.fixedDeltaTime),
            //    Drag = __instance.c_player_ship.c_rigidbody.drag,
            //    LocalPosition = Vector3.LerpUnclamped(A.m_pos, B.m_pos, t + 1),
            //    Rotation = Quaternion.SlerpUnclamped(A.m_rot, B.m_rot, t + 1)
            //};
        }

        // Not the same as vanilla
        private static float CalculateLerpParameter() {
            float num = Mathf.Max(0f, Time.time - Client.m_InterpolationStartTime);
            return num / Time.fixedDeltaTime;
        }
    }*/
}
