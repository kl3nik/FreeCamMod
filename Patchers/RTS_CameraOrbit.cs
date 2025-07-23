using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Reflection;
using UnityEngine;
using Cinemachine;
using System.Linq;
using System.Collections.Generic;
using CustomCamera;
using UnityEngine.Rendering;

// ECS
using Unity.Entities;
using Unity.Transforms;
using Ecs.Input.Components.Tags;

namespace YourModNamespace.Patchers
{
    [HarmonyPatch(typeof(RTS_Camera))]
    public class RTSCameraOrbitPatch
    {
        private static readonly ManualLogSource Logger = BepInEx.Logging.Logger.CreateLogSource("RTS_CameraPatch");

        static float moveSpeed = 15f;
        static float fastSpeed = 40f;
        static float lookSensitivity = 2.0f;

        static FieldInfo freeLookField;
        static FieldInfo pivotField;
        static bool initialized = false;

        static bool freeCamActive = false;
        static List<Behaviour> disabledCinemachine = new List<Behaviour>();

        // ECS
        static EntityManager ecsEntityManager;
        static EntityQuery selectedPawnQuery;
        static Entity? followingPawn = null;
        static bool isFollowingPawn = false;

        // Double-click detection
        static float lastClickTime = -1f;
        static float doubleClickMax = 0.25f;

        // Orbit state (when following)
        static float orbitYaw = 0f;
        static float orbitPitch = 25f;
        static float followDistance = 16f;
        static float followHeight = 8f;
        static Vector3 orbitOffset = Vector3.zero;

        [HarmonyPatch("Update")]
        [HarmonyPrefix]
        public static void Prefix(object __instance)
        {
            try
            {
                if (!initialized)
                {
                    freeLookField = __instance.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .FirstOrDefault(f => typeof(CinemachineFreeLook).IsAssignableFrom(f.FieldType));
                    pivotField = __instance.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .FirstOrDefault(f => typeof(Transform).IsAssignableFrom(f.FieldType));
                    initialized = true;

                    // ECS setup
                    if (World.DefaultGameObjectInjectionWorld != null)
                    {
                        ecsEntityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
                        selectedPawnQuery = ecsEntityManager.CreateEntityQuery(
                            typeof(Unity.Transforms.LocalToWorld),
                            typeof(SelectedEntity)
                        );
                    }
                }

                var mainCamera = Camera.main;
                if (mainCamera == null)
                    return;

                var camTransform = mainCamera.transform;

                // --- Toggle Freecam with MMB ---
                if (Input.GetMouseButtonDown(2))
                {
                    freeCamActive = !freeCamActive;
                    if (freeCamActive)
                    {
                        disabledCinemachine.Clear();
                        foreach (var comp in GameObject.FindObjectsOfType<Behaviour>(true))
                        {
                            if (comp == null) continue;
                            if (comp is CinemachineBrain || comp is CinemachineVirtualCameraBase || (comp.GetType().Namespace?.Contains("Cinemachine") == true))
                            {
                                if (comp.enabled)
                                {
                                    comp.enabled = false;
                                    disabledCinemachine.Add(comp);
                                }
                            }
                        }
                        Logger.LogInfo("Cinemachine components disabled. FreeCam enabled.");
                    }
                    else
                    {
                        foreach (var comp in disabledCinemachine)
                            if (comp != null) comp.enabled = true;
                        disabledCinemachine.Clear();
                        isFollowingPawn = false;
                        followingPawn = null;
                        Logger.LogInfo("Cinemachine components re-enabled. FreeCam disabled.");
                    }
                }

                if (!freeCamActive)
                    return;

                // --- DOUBLE CLICK TOGGLE FOLLOW ---
                if (Input.GetMouseButtonDown(0))
                {
                    float now = Time.unscaledTime;
                    if (now - lastClickTime < doubleClickMax)
                    {
                        // Double click detected!
                        Entity? pawn = GetNearestSelectedPawnUnderMouse(camTransform.position, 30f); // 30 units fallback radius
                        if (pawn.HasValue)
                        {
                            if (!isFollowingPawn || !followingPawn.HasValue || followingPawn.Value != pawn.Value)
                            {
                                followingPawn = pawn;
                                isFollowingPawn = true;

                                // Get initial orbit angles and offset
                                var ltw = ecsEntityManager.GetComponentData<LocalToWorld>(followingPawn.Value);
                                Vector3 pawnPos = ltw.Position;
                                Vector3 toCam = (camTransform.position - pawnPos);
                                followDistance = toCam.magnitude;
                                orbitYaw = Mathf.Atan2(toCam.x, toCam.z) * Mathf.Rad2Deg;
                                orbitPitch = Mathf.Asin(toCam.y / Mathf.Max(followDistance, 0.01f)) * Mathf.Rad2Deg;
                                Logger.LogInfo($"Now following pawn {followingPawn.Value.Index}.");
                            }
                            else
                            {
                                // Double click again on same pawn to stop following
                                isFollowingPawn = false;
                                followingPawn = null;
                                Logger.LogInfo("Stopped following pawn.");
                            }
                        }
                        lastClickTime = -1f; // reset
                    }
                    else
                    {
                        lastClickTime = now;
                    }
                }

                if (isFollowingPawn && followingPawn.HasValue && ecsEntityManager.Exists(followingPawn.Value))
                {
                    // --- ZOOM WHILE FOLLOWING PAWN ---
                    float minZoom = 4f, maxZoom = 64f, zoomSpeed = 10f;
                    float scroll = Input.GetAxisRaw("Mouse ScrollWheel");
                    if (Mathf.Abs(scroll) > 0.01f)
                    {
                        followDistance = Mathf.Clamp(followDistance - scroll * zoomSpeed, minZoom, maxZoom);
                    }

                    // --- BREAK FOLLOW ON MOVEMENT ---
                    if (freeCamActive &&
                        (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.A) ||
                         Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.D) ||
                         Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.E)))
                    {
                        isFollowingPawn = false;
                        followingPawn = null;
                        Logger.LogInfo("Stopped following pawn due to camera movement.");
                        // Optionally: return; // if you want to skip the rest of this frame
                    }
                    else
                    {
                        var ltw = ecsEntityManager.GetComponentData<LocalToWorld>(followingPawn.Value);
                        Vector3 pawnPos = ltw.Position;

                        // --- Right Mouse Orbit ---
                        if (Input.GetMouseButton(1))
                        {
                            Cursor.visible = false;
                            Cursor.lockState = CursorLockMode.Locked;

                            float mouseX = Input.GetAxisRaw("Mouse X") * lookSensitivity;
                            float mouseY = Input.GetAxisRaw("Mouse Y") * lookSensitivity;

                            orbitYaw += mouseX;
                            orbitPitch = Mathf.Clamp(orbitPitch - mouseY, 10f, 80f);
                        }
                        else
                        {
                            Cursor.visible = true;
                            Cursor.lockState = CursorLockMode.None;
                        }

                        // Calculate orbit camera offset
                        float yawRad = orbitYaw * Mathf.Deg2Rad;
                        float pitchRad = orbitPitch * Mathf.Deg2Rad;
                        Vector3 offset = new Vector3(
                            Mathf.Sin(yawRad) * Mathf.Cos(pitchRad),
                            Mathf.Sin(pitchRad),
                            Mathf.Cos(yawRad) * Mathf.Cos(pitchRad)
                        ) * followDistance;

                        Vector3 desiredPos = pawnPos + offset;
                        camTransform.position = Vector3.Lerp(camTransform.position, desiredPos, 0.18f);
                        camTransform.LookAt(pawnPos + Vector3.up * 2f);
                    }
                }

                else
                {
                    // Regular freecam (WASD + mouse look)
                    if (Input.GetMouseButton(1))
                    {
                        Cursor.visible = false;
                        Cursor.lockState = CursorLockMode.Locked;

                        float mouseX = Input.GetAxisRaw("Mouse X") * lookSensitivity;
                        float mouseY = Input.GetAxisRaw("Mouse Y") * lookSensitivity;

                        camTransform.Rotate(Vector3.up, mouseX, Space.World);
                        camTransform.Rotate(Vector3.right, -mouseY, Space.Self);

                        float speed = Input.GetKey(KeyCode.LeftShift) ? fastSpeed : moveSpeed;
                        Vector3 move = Vector3.zero;
                        if (Input.GetKey(KeyCode.W)) move += camTransform.forward;
                        if (Input.GetKey(KeyCode.S)) move -= camTransform.forward;
                        if (Input.GetKey(KeyCode.A)) move -= camTransform.right;
                        if (Input.GetKey(KeyCode.D)) move += camTransform.right;
                        if (Input.GetKey(KeyCode.E)) move += camTransform.up;
                        if (Input.GetKey(KeyCode.Q)) move -= camTransform.up;

                        camTransform.position += move * speed * Time.unscaledDeltaTime;
                    }
                    else
                    {
                        Cursor.visible = true;
                        Cursor.lockState = CursorLockMode.None;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Exception in Cinemachine FreeCam Patch: {ex}");
            }
        }

        /// <summary>
        /// Finds the closest selected pawn to the mouse ray within a max world-space distance.
        /// </summary>
        private static Entity? GetNearestSelectedPawnUnderMouse(Vector3 camPos, float maxDist)
        {
            if (ecsEntityManager == null || selectedPawnQuery == null)
                return null;

            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);

            // We'll use the closest to the ray (not to the mouse click on ground!)
            Entity? closest = null;
            float bestDist = maxDist;
            using (var entities = selectedPawnQuery.ToEntityArray(Unity.Collections.Allocator.Temp))
            {
                foreach (var entity in entities)
                {
                    var ltw = ecsEntityManager.GetComponentData<LocalToWorld>(entity);
                    Vector3 pawnPos = ltw.Position;
                    // Compute closest point on ray to pawn position
                    Vector3 toPawn = pawnPos - ray.origin;
                    float proj = Vector3.Dot(ray.direction, toPawn);
                    Vector3 closestPoint = ray.origin + ray.direction * Mathf.Max(0, proj);
                    float dist = Vector3.Distance(closestPoint, pawnPos);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        closest = entity;
                    }
                }
            }
            return closest;
        }
    }
}
