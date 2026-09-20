using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using Unity.Netcode;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Herramientas de editor para el personaje Astra (menu: Herramientas > Astra).
/// 1) Arma el Animator Controller por codigo: blend tree direccional (VelX/VelY) con los
///    packs walk/run de rifle, pistola y desarmado, y capas de salto (una por arma) con
///    transiciones INSTANTANEAS elegidas por codigo con el int "Pose". El paquete no tiene
///    saltos de costado: cada capa tiene solo el salto hacia arriba de cada arma.
///    Todo por codigo = las animaciones reaccionan al toque de boton sin delay.
/// 2) Construye el prefab PlayerAstra a partir del Player actual: mismo arbol (camara hija,
///    CharacterController, NetworkObject + ClientNetworkTransform) y reemplaza el modelo
///    del soldado por Astra.
/// 3) Opcional: lo instala como PlayerPrefab del NetworkManager.
/// </summary>
public static class AstraSetup
{
    const string CarpetaAssets = "Assets/AstraGenerado";
    const string RutaController = CarpetaAssets + "/AstraAnimator.controller";
    const string RutaPrefab = CarpetaAssets + "/PlayerAstra.prefab";
    const string RutaModelo = "Assets/Personaje/Modelo/astra.fbx";
    const string RutaAnimaciones = "Assets/Personaje/Animaciones";
    const string PrefabPlayerOriginal = "Assets/Prefabs/Player.prefab";

    // ------------------------------------------------------------------
    // PASO 1: Animator Controller
    // ------------------------------------------------------------------
    [MenuItem("Herramientas/Astra/1 - Crear Animator Controller")]
    public static void CrearController()
    {
        Directory.CreateDirectory(CarpetaAssets);

        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(RutaController);
        if (controller == null)
            controller = AnimatorController.CreateAnimatorControllerAtPath(RutaController);

        // --- parametros: VelX/VelY (blend direccional) y Pose (arma para el salto) ---
        AgregarParametro(controller, "VelX", AnimatorControllerParameterType.Float);
        AgregarParametro(controller, "VelY", AnimatorControllerParameterType.Float);
        AgregarParametro(controller, "Pose", AnimatorControllerParameterType.Int);

        // reconstruir desde cero para poder re-ejecutar sin duplicar estados ni capas
        AnimatorStateMachine baseLayer = controller.layers[0].stateMachine;
        baseLayer.name = "Base Layer";
        baseLayer.entryPosition = new Vector3(50, 0);
        baseLayer.anyStatePosition = new Vector3(50, -60);
        ChildAnimatorState[] estadosViejos = baseLayer.states; // copia: seguro remover mientras recorro
        foreach (ChildAnimatorState hijo in estadosViejos)
            baseLayer.RemoveState(hijo.state);
        while (controller.layers.Length > 1)
        {
            List<AnimatorControllerLayer> l = new List<AnimatorControllerLayer>(controller.layers);
            l.RemoveAt(l.Count - 1);
            controller.layers = l.ToArray();
        }

        // limpiar sub-assets huerfanos de ejecuciones anteriores (estados, transiciones,
        // blend trees y maquinas de capas borradas), para poder re-ejecutar sin acumular basura
        foreach (Object sub in AssetDatabase.LoadAllAssetsAtPath(RutaController))
        {
            if (sub is AnimatorController) continue;            // el controller queda
            if (sub == baseLayer) continue;                     // la maquina base queda
            if (sub is AnimatorStateMachine || sub is AnimatorState ||
                sub is AnimatorStateTransition || sub is BlendTree)
                Object.DestroyImmediate(sub, true);
        }

        // --- base layer: blend tree direccional por arma ---
        BlendTree blend = CrearBlendDireccion("Locomotion");
        // el BlendTree debe vivir como sub-asset del controller, si no se pierde al recargar
        AssetDatabase.AddObjectToAsset(blend, controller);
        AnimatorState locomotion = baseLayer.AddState("Locomotion", new Vector3(250, 20));
        locomotion.motion = blend;
        baseLayer.defaultState = locomotion;

        // --- capas de salto: una por arma, elegidas por codigo con el int Pose ---
        CrearCapaSalto(controller, "Salto Rifle", 1, "idle_jump_rifle");
        CrearCapaSalto(controller, "Salto Pistola", 2, "idle_jump_pistol");
        CrearCapaSalto(controller, "Salto Desarmado", 3, "idle_jump_unarmed");

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        Debug.Log("[AstraSetup] Controller creado en " + RutaController);
    }

    static void AgregarParametro(AnimatorController controller, string nombre, AnimatorControllerParameterType tipo)
    {
        foreach (AnimatorControllerParameter p in controller.parameters)
            if (p.name == nombre) return;
        controller.AddParameter(nombre, tipo);
    }

    // Blend tree 2D simple con todos los packs de locomotion (rifle, pistola y desarmado).
    // Plano VelX/VelY: caminar = radio 1, correr = radio 2 (la velocidad decide entre caminar y correr).
    static BlendTree CrearBlendDireccion(string nombre)
    {
        BlendTree blend = new BlendTree
        {
            name = nombre,
            blendType = BlendTreeType.SimpleDirectional2D,
            blendParameter = "VelX",
            blendParameterY = "VelY",
            useAutomaticThresholds = false
        };

        List<ChildMotion> hijos = new List<ChildMotion>();

        // rifle: adelante / atras / strafe izq-der, caminar y correr
        AgregarClip(hijos, "walk_forward_rifle", 0f, 1f);
        AgregarClip(hijos, "walk_backward_rifle", 0f, -1f);
        AgregarClip(hijos, "walk_strafe_left_rifle", -1f, 0f);
        AgregarClip(hijos, "walk_strafe_right_rifle", 1f, 0f);
        AgregarClip(hijos, "run_forward_rifle", 0f, 2f);
        AgregarClip(hijos, "run_backward_rifle", 0f, -2f);
        AgregarClip(hijos, "run_strafing_left_rifle", -2f, 0f);
        AgregarClip(hijos, "run_strafing_right_rifle", 2f, 0f);

        // pistola: mismos 8 puntos (los nombres de strafe de pistola son distintos)
        AgregarClip(hijos, "walk_forward_pistol", 0f, 1f);
        AgregarClip(hijos, "walk_backward_pistol", 0f, -1f);
        AgregarClip(hijos, "walk_pistol_strafe_left", -1f, 0f);
        AgregarClip(hijos, "walk_pistol_strafe_right", 1f, 0f);
        AgregarClip(hijos, "run_forward_pistol", 0f, 2f);
        AgregarClip(hijos, "run_backward_pistol", 0f, -2f);
        AgregarClip(hijos, "run_strafe_left_pistol", -2f, 0f);
        AgregarClip(hijos, "run_strafe_right_pistol", 2f, 0f);

        // desarmado: el paquete solo trae el idle/salto; se reusan los clips de rifle
        // (poses casi identicas). Si consiguen walk/run unarmed, reemplazar estos 8 puntos.
        AgregarClip(hijos, "walk_forward_rifle", 0f, 1f);
        AgregarClip(hijos, "walk_backward_rifle", 0f, -1f);
        AgregarClip(hijos, "walk_strafe_left_rifle", -1f, 0f);
        AgregarClip(hijos, "walk_strafe_right_rifle", 1f, 0f);
        AgregarClip(hijos, "run_forward_rifle", 0f, 2f);
        AgregarClip(hijos, "run_backward_rifle", 0f, -2f);
        AgregarClip(hijos, "run_strafing_left_rifle", -2f, 0f);
        AgregarClip(hijos, "run_strafing_right_rifle", 2f, 0f);

        blend.children = hijos.ToArray();
        return blend;
    }

    static void AgregarClip(List<ChildMotion> hijos, string nombreClip, float velX, float velY)
    {
        AnimationClip clip = BuscarClip(nombreClip);
        if (clip == null)
        {
            Debug.LogWarning("[AstraSetup] No encontre el clip '" + nombreClip + "' (se omite el punto del blend)");
            return;
        }
        hijos.Add(new ChildMotion { timeScale = 1f, position = new Vector2(velX, velY), motion = clip });
    }

    // Busca un clip por nombre de archivo dentro de los FBX de la carpeta de animaciones
    static AnimationClip BuscarClip(string nombreArchivo)
    {
        string[] guids = AssetDatabase.FindAssets(nombreArchivo, new[] { RutaAnimaciones });
        foreach (string guid in guids)
        {
            string ruta = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileNameWithoutExtension(ruta) != nombreArchivo) continue;
            foreach (Object sub in AssetDatabase.LoadAllAssetsAtPath(ruta))
                if (sub is AnimationClip clip && !clip.name.Contains("__preview__"))
                    return clip;
        }
        Debug.LogWarning("[AstraSetup] clip no encontrado: " + nombreArchivo);
        return null;
    }

    // Capa de salto: dos estados DENTRO de la capa (transiciones entre capas no existen en Unity).
    // "Vacio" (sin motion) es el default y no pinta nada: se ve el blend de la base.
    // Pose == valorPose -> pasa al salto en 0 segundos (instantaneo); Pose == 0 -> vuelve al vacio.
    static void CrearCapaSalto(AnimatorController controller, string nombreCapa, int valorPose, string nombreClipSalto)
    {
        AnimationClip clip = BuscarClip(nombreClipSalto);
        if (clip == null)
        {
            Debug.LogWarning("[AstraSetup] salto no encontrado: " + nombreClipSalto);
            return;
        }

        AnimatorControllerLayer capa = new AnimatorControllerLayer
        {
            name = nombreCapa,
            stateMachine = new AnimatorStateMachine { name = nombreCapa, hideFlags = HideFlags.HideInHierarchy },
            defaultWeight = 1f,
            iKPass = false
        };
        AnimatorStateMachine sm = capa.stateMachine;
        AssetDatabase.AddObjectToAsset(sm, controller); // persistir como sub-asset
        sm.entryPosition = new Vector3(50, 0);
        sm.anyStatePosition = new Vector3(50, -60);

        AnimatorState vacio = sm.AddState("Vacio", new Vector3(100, 20));
        AnimatorState salto = sm.AddState(nombreClipSalto, new Vector3(320, 20));
        salto.motion = clip;
        sm.defaultState = vacio;

        AnimatorStateTransition aSalto = new AnimatorStateTransition
        {
            destinationState = salto,
            hasExitTime = false,
            exitTime = 0f,
            duration = 0f,
            hasFixedDuration = true,
            canTransitionToSelf = false
        };
        aSalto.AddCondition(AnimatorConditionMode.Equals, valorPose, "Pose");
        vacio.AddTransition(aSalto);

        AnimatorStateTransition aVacio = new AnimatorStateTransition
        {
            destinationState = vacio,
            hasExitTime = false,
            exitTime = 0f,
            duration = 0f,
            hasFixedDuration = true,
            canTransitionToSelf = false
        };
        aVacio.AddCondition(AnimatorConditionMode.Equals, 0, "Pose");
        salto.AddTransition(aVacio);

        List<AnimatorControllerLayer> capas = new List<AnimatorControllerLayer>(controller.layers);
        capas.Add(capa);
        controller.layers = capas.ToArray();
    }

    // ------------------------------------------------------------------
    // PASO 2: prefab PlayerAstra a partir del Player actual
    // ------------------------------------------------------------------
    [MenuItem("Herramientas/Astra/2 - Crear prefab PlayerAstra (desde Player)")]
    public static void CrearPrefabAstra()
    {
        if (AssetDatabase.LoadAssetAtPath<Object>(RutaModelo) == null)
        {
            Debug.LogError("[AstraSetup] No encuentro el modelo " + RutaModelo);
            return;
        }

        GameObject original = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPlayerOriginal);
        if (original == null)
        {
            Debug.LogError("[AstraSetup] No encuentro el prefab " + PrefabPlayerOriginal);
            return;
        }

        Directory.CreateDirectory(CarpetaAssets);

        // 1) instanciar el Player actual como base (camara hija, CharacterController, red)
        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(original);
        go.name = "PlayerAstra";

        // 2) controlador nuevo; el viejo queda APAGADO para no pelear (no hay doble control)
        if (go.GetComponent<PlayerAstra>() == null)
            go.AddComponent<PlayerAstra>();
        Player playerViejo = go.GetComponent<Player>();
        if (playerViejo != null) playerViejo.enabled = false;
        PlayerNetworkMovement movimientoViejo = go.GetComponent<PlayerNetworkMovement>();
        if (movimientoViejo != null) movimientoViejo.enabled = false;

        // 3) reemplazar el modelo hijo (PrefabInstance del soldado) por el de Astra.
        //    Va en (0,0,0): el script ajusta los pies al origen solo al arrancar.
        Animator animatorViejo = go.GetComponentInChildren<Animator>();
        int orden = 1;
        if (animatorViejo != null)
            orden = animatorViejo.transform.GetSiblingIndex();
        if (animatorViejo != null)
            Object.DestroyImmediate(animatorViejo.transform.gameObject);

        GameObject modeloNuevo = (GameObject)PrefabUtility.InstantiatePrefab(
            AssetDatabase.LoadAssetAtPath<Object>(RutaModelo), go.transform);
        modeloNuevo.name = "Astra";
        modeloNuevo.transform.localPosition = Vector3.zero;
        modeloNuevo.transform.localRotation = Quaternion.identity;
        modeloNuevo.transform.SetSiblingIndex(orden);

        // asignar el controller al Animator del modelo (si el FBX trae uno propio, se pisa)
        Animator animatorNuevo = modeloNuevo.GetComponent<Animator>();
        if (animatorNuevo == null) animatorNuevo = modeloNuevo.AddComponent<Animator>();
        AnimatorController controllerGenerado = AssetDatabase.LoadAssetAtPath<AnimatorController>(RutaController);
        if (controllerGenerado != null)
            animatorNuevo.runtimeAnimatorController = controllerGenerado;
        else
            Debug.LogWarning("[AstraSetup] No encontre el controller; ejecuta el paso 1 antes del paso 2");

        // 4) guardar como prefab nuevo
        PrefabUtility.SaveAsPrefabAsset(go, RutaPrefab);
        Object.DestroyImmediate(go);
        AssetDatabase.SaveAssets();
        Debug.Log("[AstraSetup] Prefab creado en " + RutaPrefab +
                  "\nRevisa en el prefab: altura de camara (alturaCamara/atrasCamara) y ajusteDePies si el modelo queda hundido.");
    }

    // ------------------------------------------------------------------
    // PASO 3 (opcional): usar PlayerAstra como jugador de la red
    // ------------------------------------------------------------------
    [MenuItem("Herramientas/Astra/3 - Usar PlayerAstra como PlayerPrefab del NetworkManager")]
    public static void InstalarComoPlayerPrefab()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RutaPrefab);
        if (prefab == null)
        {
            Debug.LogError("[AstraSetup] Primero ejecuta el paso 2 (crear prefab)");
            return;
        }

        NetworkManager nm = Object.FindObjectOfType<NetworkManager>();
        if (nm == null)
        {
            Debug.LogError("[AstraSetup] No hay NetworkManager en la escena abierta (abri Menu.unity)");
            return;
        }

        Undo.RecordObject(nm, "Set PlayerPrefab");
        nm.NetworkConfig.PlayerPrefab = prefab;
        EditorUtility.SetDirty(nm);
        MarkSceneDirty(nm.gameObject);

        // tambien al NetworkPrefabsList default para que los clientes lo conozcan
        NetworkPrefabsList lista = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>("Assets/DefaultNetworkPrefabs.asset");
        if (lista != null)
        {
            Undo.RecordObject(lista, "Add PlayerAstra");
            if (!lista.Contains(prefab))
            {
                lista.Add(new NetworkPrefab { Prefab = prefab });
                EditorUtility.SetDirty(lista);
            }
        }

        Debug.Log("[AstraSetup] NetworkManager ahora usa PlayerAstra como jugador. Guarda la escena Menu.");
    }

    static void MarkSceneDirty(Object objetoDeEscena)
    {
        Scene scene = ((Component)objetoDeEscena).gameObject.scene;
        if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);
    }}
