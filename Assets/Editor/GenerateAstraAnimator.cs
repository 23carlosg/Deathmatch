using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Collections.Generic;
using System.Linq;
using System.IO;

public class GenerateAstraAnimator : EditorWindow
{
    [MenuItem("Tools/Astra/Generate Animator Controller")]
    public static void Generate()
    {
        string animRoot = "Assets/Personaje/Animaciones";
        string outputPath = "Assets/AstraGenerado/AstraAnimatorFull.controller";

        // 1. Collect all clips from FBX files
        var clipsByCategory = new Dictionary<string, List<AnimationClip>>();
        foreach (var dir in Directory.GetDirectories(animRoot))
        {
            string cat = Path.GetFileName(dir);
            foreach (var fbx in Directory.GetFiles(dir, "*.fbx"))
            {
                if (fbx.EndsWith(".meta")) continue;
                var loaded = AssetDatabase.LoadAllAssetsAtPath(fbx);
                foreach (var obj in loaded)
                {
                    if (obj is AnimationClip clip && clip.name != "__preview__")
                    {
                        if (!clipsByCategory.ContainsKey(cat)) clipsByCategory[cat] = new List<AnimationClip>();
                        clipsByCategory[cat].Add(clip);
                    }
                }
            }
        }

        // Log what we found
        foreach (var kv in clipsByCategory)
        {
            Debug.Log($"[{kv.Key}] {string.Join(", ", kv.Value.Select(c => c.name))}");
        }

        // 2. Create Controller
        var controller = AnimatorController.CreateAnimatorControllerAtPath(outputPath);
        controller.AddParameter("VelX", AnimatorControllerParameterType.Float);
        controller.AddParameter("VelY", AnimatorControllerParameterType.Float);
        controller.AddParameter("Arma", AnimatorControllerParameterType.Int); // 0=unarmed, 1=rifle, 2=pistol
        controller.AddParameter("Pose", AnimatorControllerParameterType.Int); // 0=ground, 1=jump_rifle, 2=jump_pistol, 3=jump_unarmed
        controller.AddParameter("isGrounded", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);

        var rootStateMachine = controller.layers[0].stateMachine;
        rootStateMachine.entryPosition = new Vector3(0, 0, 0);
        rootStateMachine.anyStatePosition = new Vector3(0, -60, 0);
        rootStateMachine.exitPosition = new Vector3(800, 120, 0);

        // 3. Build Locomotion Blend Trees per weapon
        var weaponNames = new[] { "unarmed", "pistol", "rifle" };
        var weaponArmaValue = new Dictionary<string, int> { { "unarmed", 0 }, { "rifle", 1 }, { "pistol", 2 } };
        var locomotionStates = new Dictionary<string, AnimatorState>();

        float yPos = 20;
        foreach (var weapon in weaponNames)
        {
            // Walk Blend Tree 2D
            var walkTree = new BlendTree
            {
                name = $"Walk_{weapon}",
                blendType = (BlendTreeType)1, // Blend2D
                blendParameter = "VelX",
                blendParameterY = "VelY",
                minThreshold = -2f,
                maxThreshold = 2f,
                useAutomaticThresholds = false
            };

            // Run Blend Tree 2D
            var runTree = new BlendTree
            {
                name = $"Run_{weapon}",
                blendType = (BlendTreeType)1, // Blend2D
                blendParameter = "VelX",
                blendParameterY = "VelY",
                minThreshold = -2f,
                maxThreshold = 2f,
                useAutomaticThresholds = false
            };

            // Add clips to blend trees
            AddDirectionalClips(walkTree, clipsByCategory, $"Walk {weapon}", weapon);
            AddDirectionalClips(runTree, clipsByCategory, $"Run {weapon}", weapon);

            // Create a Blend Tree that blends between Walk and Run based on Speed
            var locomotionTree = new BlendTree
            {
                name = $"Locomotion_{weapon}",
                blendType = (BlendTreeType)0, // Blend1D
                blendParameter = "Speed",
                minThreshold = 0f,
                maxThreshold = 1f,
                useAutomaticThresholds = false
            };
            locomotionTree.children = new[]
            {
                new ChildMotion { motion = walkTree, threshold = 0f, timeScale = 1f },
                new ChildMotion { motion = runTree, threshold = 1f, timeScale = 1f }
            };

            // State for this weapon's locomotion
            var state = rootStateMachine.AddState($"{weapon}_Locomotion", new Vector3(200, yPos, 0));
            state.motion = locomotionTree;
            state.writeDefaultValues = true;
            locomotionStates[weapon] = state;
            yPos += 100;
        }

        // 4. Create Idle states (single clip each)
        var idleClips = clipsByCategory.ContainsKey("Idle completo") ? clipsByCategory["Idle completo"] : new List<AnimationClip>();
        var idleStates = new Dictionary<string, AnimatorState>();
        foreach (var weapon in weaponNames)
        {
            var clip = idleClips.FirstOrDefault(c => c.name == $"idle_{weapon}");
            if (clip != null)
            {
                var state = rootStateMachine.AddState($"Idle_{weapon}", new Vector3(200, yPos, 0));
                state.motion = clip;
                state.writeDefaultValues = true;
                idleStates[weapon] = state;
                yPos += 80;
            }
        }

        // 5. Jump Sub-State Machine
        var jumpSM = rootStateMachine.AddStateMachine("Jump", new Vector3(600, 20, 0));
        jumpSM.entryPosition = new Vector3(0, 0, 0);
        jumpSM.anyStatePosition = new Vector3(0, -60, 0);
        jumpSM.exitPosition = new Vector3(800, 120, 0);
        jumpSM.parentStateMachinePosition = new Vector3(800, 20, 0);

        var jumpClips = new Dictionary<string, AnimationClip>();
        if (clipsByCategory.ContainsKey("Idle completo"))
        {
            foreach (var weapon in weaponNames)
            {
                var clip = clipsByCategory["Idle completo"].FirstOrDefault(c => c.name == $"idle_jump_{weapon}");
                if (clip != null) jumpClips[weapon] = clip;
            }
        }
        // Also check Jump folders
        if (clipsByCategory.ContainsKey("Jump rifle completo"))
        {
            var clip = clipsByCategory["Jump rifle completo"].FirstOrDefault(c => c.name == "forward_jump_rifle");
            if (clip != null) jumpClips["rifle"] = clip;
        }
        if (clipsByCategory.ContainsKey("Jump pistol completo"))
        {
            var clip = clipsByCategory["Jump pistol completo"].FirstOrDefault(c => c.name == "forward_jump_pistol");
            if (clip != null) jumpClips["pistol"] = clip;
        }

        var jumpStates = new Dictionary<string, AnimatorState>();
        float jumpY = 20;
        foreach (var weapon in weaponNames)
        {
            if (jumpClips.ContainsKey(weapon))
            {
                var state = jumpSM.AddState($"Jump_{weapon}", new Vector3(200, jumpY, 0));
                state.motion = jumpClips[weapon];
                state.writeDefaultValues = true;
                jumpStates[weapon] = state;
                jumpY += 100;
            }
        }

        // Set default state for Jump Sub-State Machine
        if (jumpStates.ContainsKey("rifle"))
            jumpSM.defaultState = jumpStates["rifle"];
        else if (jumpStates.ContainsKey("pistol"))
            jumpSM.defaultState = jumpStates["pistol"];
        else if (jumpStates.ContainsKey("unarmed"))
            jumpSM.defaultState = jumpStates["unarmed"];

        // Add Exit transitions from each jump state (to allow leaving the sub-state machine)
        foreach (var weapon in weaponNames)
        {
            if (jumpStates.ContainsKey(weapon))
            {
                var jumpState = jumpStates[weapon];
                var exitTransition = jumpState.AddExitTransition();
                exitTransition.hasExitTime = true;
                exitTransition.exitTime = 0.95f;
                exitTransition.duration = 0.1f;
                exitTransition.AddCondition(AnimatorConditionMode.If, 0, "isGrounded");
            }
        }

        // Add StateMachineTransitions: Jump.Exit -> Locomotion (per weapon)
        foreach (var weapon in weaponNames)
        {
            if (jumpStates.ContainsKey(weapon) && locomotionStates.ContainsKey(weapon))
            {
                var smt = rootStateMachine.AddStateMachineTransition(jumpSM, locomotionStates[weapon]);
                smt.AddCondition(AnimatorConditionMode.If, 0, "isGrounded");
                smt.AddCondition(AnimatorConditionMode.Equals, weaponArmaValue[weapon], "Arma");
            }
        }

        // 6. Transitions: Locomotion <-> Idle based on Speed
        foreach (var weapon in weaponNames)
        {
            if (locomotionStates.ContainsKey(weapon) && idleStates.ContainsKey(weapon))
            {
                var loco = locomotionStates[weapon];
                var idle = idleStates[weapon];

                // Locomotion -> Idle (Speed < 0.1)
                var t1 = loco.AddTransition(idle);
                t1.hasExitTime = false;
                t1.duration = 0.1f;
                t1.AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");
                t1.AddCondition(AnimatorConditionMode.Equals, weaponArmaValue[weapon], "Arma");

                // Idle -> Locomotion (Speed > 0.1)
                var t2 = idle.AddTransition(loco);
                t2.hasExitTime = false;
                t2.duration = 0.1f;
                t2.AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");
                t2.AddCondition(AnimatorConditionMode.Equals, weaponArmaValue[weapon], "Arma");
            }
        }

        // 7. Transitions: Locomotion -> Jump (based on Pose + !isGrounded)
        foreach (var weapon in weaponNames)
        {
            if (locomotionStates.ContainsKey(weapon) && jumpStates.ContainsKey(weapon))
            {
                var loco = locomotionStates[weapon];
                var jump = jumpStates[weapon];
                int poseVal = weapon == "rifle" ? 1 : (weapon == "pistol" ? 2 : 3);

                var t = loco.AddTransition(jump);
                t.hasExitTime = false;
                t.duration = 0f;
                t.AddCondition(AnimatorConditionMode.Equals, poseVal, "Pose");
                t.AddCondition(AnimatorConditionMode.IfNot, 0, "isGrounded");
                t.AddCondition(AnimatorConditionMode.Equals, weaponArmaValue[weapon], "Arma");
            }
        }

        // 9. Set default state to unarmed locomotion (or idle)
        if (idleStates.ContainsKey("unarmed"))
            rootStateMachine.defaultState = idleStates["unarmed"];
        else if (locomotionStates.ContainsKey("unarmed"))
            rootStateMachine.defaultState = locomotionStates["unarmed"];

        // 10. Transitions between weapons (Arma parameter changes)
        // When Arma changes, transition to new weapon's idle/locomotion
        foreach (var fromWeapon in weaponNames)
        {
            foreach (var toWeapon in weaponNames)
            {
                if (fromWeapon == toWeapon) continue;
                if (!locomotionStates.ContainsKey(fromWeapon) || !locomotionStates.ContainsKey(toWeapon)) continue;

                var from = locomotionStates[fromWeapon];
                var to = locomotionStates[toWeapon];

                var t = from.AddTransition(to);
                t.hasExitTime = false;
                t.duration = 0.1f;
                t.AddCondition(AnimatorConditionMode.Equals, weaponArmaValue[toWeapon], "Arma");
            }
        }

        // Also idle to idle transitions for weapon change
        foreach (var fromWeapon in weaponNames)
        {
            foreach (var toWeapon in weaponNames)
            {
                if (fromWeapon == toWeapon) continue;
                if (!idleStates.ContainsKey(fromWeapon) || !idleStates.ContainsKey(toWeapon)) continue;

                var from = idleStates[fromWeapon];
                var to = idleStates[toWeapon];

                var t = from.AddTransition(to);
                t.hasExitTime = false;
                t.duration = 0.1f;
                t.AddCondition(AnimatorConditionMode.Equals, weaponArmaValue[toWeapon], "Arma");
            }
        }

        // 11. Jump layer (additive upper body) - optional, but let's add it as layer 1
        AssetDatabase.SaveAssets();
        Debug.Log($"[Astra] Generated Animator Controller at {outputPath}");
        EditorUtility.FocusProjectWindow();
        Selection.activeObject = controller;
    }

    static void AddDirectionalClips(BlendTree tree, Dictionary<string, List<AnimationClip>> clipsByCategory, string categoryName, string weapon)
    {
        if (!clipsByCategory.ContainsKey(categoryName)) return;

        var clips = clipsByCategory[categoryName];
        var dirMap = new Dictionary<string, Vector2>
        {
            { "forward", new Vector2(0, 1) },
            { "backward", new Vector2(0, -1) },
            { "strafe_left", new Vector2(-1, 0) },
            { "strafe_right", new Vector2(1, 0) },
            { "strafing_left", new Vector2(-1, 0) },
            { "strafing_right", new Vector2(1, 0) },
        };

        foreach (var clip in clips)
        {
            // clip name format: walk_forward_rifle, run_strafing_left_pistol, etc.
            string lower = clip.name.ToLower();
            Vector2 pos = Vector2.zero;
            bool found = false;

            foreach (var kv in dirMap)
            {
                if (lower.Contains(kv.Key))
                {
                    pos = kv.Value;
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                // fallback: try to parse direction from name
                if (lower.Contains("forward")) pos = new Vector2(0, 1);
                else if (lower.Contains("backward")) pos = new Vector2(0, -1);
                else if (lower.Contains("left")) pos = new Vector2(-1, 0);
                else if (lower.Contains("right")) pos = new Vector2(1, 0);
                else continue;
            }

            // Scale for run (double radius)
            if (categoryName.StartsWith("Run"))
                pos *= 2f;

            tree.AddChild(clip, pos);
        }
    }
}