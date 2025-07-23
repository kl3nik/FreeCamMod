//using BepInEx.Logging;
//using HarmonyLib;
//using System;
//using System.Reflection;
//using UnityEngine;
//using Cinemachine;
//using System.Linq;
//using System.Collections.Generic;
//using CustomCamera;

//[HarmonyPatch(typeof(RTS_Camera))]
//public class RTSCameraOrbitPatch
//{
//    private static readonly ManualLogSource Logger = BepInEx.Logging.Logger.CreateLogSource("RTS_CameraPatch");

//    // Free-cam settings
//    static float moveSpeed = 15f;
//    static float fastSpeed = 40f;
//    static float lookSensitivity = 2.0f;

//    // Reflection cache (unchanged from your template, not used for camera anymore)
//    static FieldInfo freeLookField;
//    static FieldInfo pivotField;
//    static bool initialized = false;

//    // Cinemachine toggling
//    static bool freeCamActive = false;
//    static List<Behaviour> disabledCinemachine = new List<Behaviour>();

//    [HarmonyPatch("Update")]
//    [HarmonyPrefix]
//    public static void Prefix(object __instance)
//    {
//        try
//        {
//            // One-time reflection cache if you need access to the RTS_Camera's fields later.
//            if (!initialized)
//            {
//                freeLookField = __instance.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
//                    .FirstOrDefault(f => typeof(CinemachineFreeLook).IsAssignableFrom(f.FieldType));
//                pivotField = __instance.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
//                    .FirstOrDefault(f => typeof(Transform).IsAssignableFrom(f.FieldType));
//                initialized = true;
//            }

//            // --- Find MainCamera ---
//            var mainCamera = Camera.main;
//            if (mainCamera == null)
//                return;
//            var camTransform = mainCamera.transform;

//            // --- On MMB click: toggle Cinemachine and FreeCam ---
//            if (Input.GetMouseButtonDown(2))
//            {
//                freeCamActive = !freeCamActive;
//                if (freeCamActive)
//                {
//                    // Disable all Cinemachine behaviours in the scene
//                    disabledCinemachine.Clear();

//                    foreach (var comp in GameObject.FindObjectsOfType<Behaviour>(true))
//                    {
//                        if (comp == null) continue;
//                        if (comp is CinemachineBrain || comp is CinemachineVirtualCameraBase || (comp.GetType().Namespace?.Contains("Cinemachine") == true))
//                        {
//                            if (comp.enabled)
//                            {
//                                comp.enabled = false;
//                                disabledCinemachine.Add(comp);
//                            }
//                        }
//                    }
//                    Logger.LogInfo("Cinemachine components disabled. FreeCam enabled.");
//                }
//                else
//                {
//                    // Re-enable any previously disabled Cinemachine components
//                    foreach (var comp in disabledCinemachine)
//                        if (comp != null) comp.enabled = true;
//                    disabledCinemachine.Clear();
//                    Logger.LogInfo("Cinemachine components re-enabled. FreeCam disabled.");
//                }
//            }

//            // --- If FreeCam is active, move MainCamera directly ---
//            if (freeCamActive)
//            {
//                // RMB: Look/rotate
//                if (Input.GetMouseButton(1))
//                {
//                    Cursor.visible = false;
//                    Cursor.lockState = CursorLockMode.Locked;

//                    float mouseX = Input.GetAxisRaw("Mouse X") * lookSensitivity;
//                    float mouseY = Input.GetAxisRaw("Mouse Y") * lookSensitivity;

//                    camTransform.Rotate(Vector3.up, mouseX, Space.World);
//                    camTransform.Rotate(Vector3.right, -mouseY, Space.Self);

//                    float speed = Input.GetKey(KeyCode.LeftShift) ? fastSpeed : moveSpeed;
//                    Vector3 move = Vector3.zero;
//                    if (Input.GetKey(KeyCode.W)) move += camTransform.forward;
//                    if (Input.GetKey(KeyCode.S)) move -= camTransform.forward;
//                    if (Input.GetKey(KeyCode.A)) move -= camTransform.right;
//                    if (Input.GetKey(KeyCode.D)) move += camTransform.right;
//                    if (Input.GetKey(KeyCode.E)) move += camTransform.up;
//                    if (Input.GetKey(KeyCode.Q)) move -= camTransform.up;

//                    camTransform.position += move * speed * Time.unscaledDeltaTime;
//                }
//                else
//                {
//                    Cursor.visible = true;
//                    Cursor.lockState = CursorLockMode.None;
//                }
//            }
//        }
//        catch (Exception ex)
//        {
//            Logger.LogError($"Exception in Cinemachine FreeCam Patch: {ex}");
//        }
//    }
//}
