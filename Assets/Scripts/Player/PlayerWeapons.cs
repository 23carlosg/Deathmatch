using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Armas del jugador: cambio de arma, apuntar, recargar, disparar (rayo desde la camara),
/// retroceso, destello, rayo laser y sonido. Publica por red el arma en la mano.
/// El daño se avisa al servidor, que se lo aplica a PlayerHealth de la victima.
/// </summary>
public class PlayerWeapons : NetworkBehaviour
{
    [Header("Disparo - Rifle")]
    public float danioRifle = 20f;
    public float retrocesoRifle = 0.5f;
    public float retrocesoRotacionRifle = 6f;
    public float cadenciaRifle = 6f;

    [Header("Disparo - Pistola")]
    public float danioPistola = 25f;
    public float retrocesoPistola = 0f;
    public float retrocesoRotacionPistola = 6f;
    public float cadenciaPistola = 4f;

    [Header("Disparo - General")]
    public Transform bocaDelArma;
    [Tooltip("Punta del cañon de la pistola (objeto vacio hijo del arma). Si queda vacio se usa Boca Del Arma")]
    public Transform bocaPistola;
    [Tooltip("Punta del cañon del rifle (objeto vacio hijo del arma). Si queda vacio se usa Boca Del Arma")]
    public Transform bocaRifle;
    public GameObject efectoDisparo;
    public GameObject efectoImpacto;
    public float alcance = 200f;
    public float dispersion = 1.5f;

    [Header("Disparo - Rayo laser")]
    [Tooltip("Material con el shader Custom/LaserBeam. Si queda vacio no se dibuja rayo")]
    public Material materialLaser;
    public Color colorLaser = new Color(0.2f, 0.9f, 1f, 1f);
    public float anchoLaser = 0.06f;
    [Tooltip("Cuanto tarda en desvanecerse el rayo (segundos)")]
    public float duracionLaser = 0.08f;

    [Header("Sonido")]
    public AudioClip sonidoDisparo;
    [Range(0f, 1f)] public float volumenDisparo = 1f;

    [Header("Armas (modelos en la mano; se buscan solos si quedan vacios)")]
    public GameObject armaRifle;
    public GameObject armaPistola;
    public bool tieneRifle = false;
    public bool tienePistola = false;

    // Arma en la mano publicada por el dueño. Arranca en 3 (desarmado) para que los demas
    // no lo vean con el rifle antes de que cambie de arma por primera vez.
    readonly NetworkVariable<int> redArma = new NetworkVariable<int>(3,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    PlayerNetworkMovement jugador;
    AudioSource fuenteAudio;

    int armaActual = 3;      // 1 Rifle, 2 Pistola, 3 desarmado
    int armaAplicada = -1;
    bool apuntando;
    bool recargando;
    float recargaHasta;
    float proximoDisparoPermitido;
    float recoilArma;
    Vector3 posReposoRifle, posReposoPistola;
    Quaternion rotReposoRifle, rotReposoPistola;

    public int ArmaActual => armaActual;
    public bool Apuntando => apuntando;

    // ------------------------- INICIO -------------------------

    public void Iniciar(PlayerNetworkMovement j)
    {
        jugador = j;
        BuscarArmas();
        ConfigurarAudio();
        AplicarArma(armaActual);
        GuardarPoseDeReposoDeLasArmas();
    }

    public override void OnNetworkSpawn()
    {
        if (IsLocalPlayer) redArma.Value = armaActual;   // avisa que arranca desarmado
        else AplicarArma(redArma.Value);
    }

    // Sonido de disparo en audio 3D: cada maquina lo reproduce una sola vez (el dueño en
    // Disparar, las remotas via el Rpc) y se escucha desde la posicion del cuerpo que dispara,
    // con volumen segun distancia. Pitch con leve variacion para no sonar identico cada tiro.
    void ConfigurarAudio()
    {
        fuenteAudio = gameObject.AddComponent<AudioSource>();
        fuenteAudio.playOnAwake = false;
        fuenteAudio.spatialBlend = 1f;   // totalmente 3D
        fuenteAudio.minDistance = 4f;
        fuenteAudio.maxDistance = 60f;
    }

    void BuscarArmas()
    {
        Transform raiz = jugador.animator != null ? jugador.animator.transform : transform;
        Transform rifle = null;
        foreach (Transform t in raiz.GetComponentsInChildren<Transform>(true))
        {
            if (rifle != null && t.IsChildOf(rifle)) continue;
            string n = t.name.ToLower();
            if (armaRifle == null && n.Contains("rifle"))
            {
                armaRifle = t.gameObject;
                rifle = t;
            }
            else if (armaPistola == null && n == "base" && t.GetComponentInChildren<Renderer>() != null)
            {
                armaPistola = t.gameObject;
            }
            else if (bocaDelArma == null && n.Contains("muzzle"))
            {
                bocaDelArma = t;
            }
        }
    }

    // true si el transform es parte de un modelo de arma (lo usan el cuerpo y la camara)
    public bool EsParteDeUnArma(Transform t)
    {
        if (armaRifle != null && t.IsChildOf(armaRifle.transform)) return true;
        if (armaPistola != null && t.IsChildOf(armaPistola.transform)) return true;
        return false;
    }

    // ------------------------- ARMA EN LA MANO -------------------------

    public void LeerCambioDeArma()
    {
        // Tecla 1 = slot 1 de la hotbar = pistola (arma 2)
        if (Keyboard.current.digit1Key.wasPressedThisFrame && tienePistola) CambiarArma(2);

        // Tecla 2 = slot 2 de la hotbar = rifle (arma 1)
        if (Keyboard.current.digit2Key.wasPressedThisFrame && tieneRifle) CambiarArma(1);

        if (Keyboard.current.digit3Key.wasPressedThisFrame) CambiarArma(3);
    }

    // Cambia el arma en la mano (1 rifle, 2 pistola, 3 desarmado) y lo publica por red
    public void CambiarArma(int nueva)
    {
        if (jugador.Health.Muerto || armaActual == nueva) return;
        armaActual = nueva;
        redArma.Value = nueva;
        recoilArma = 0f;
        DevolverArmasAReposo();
        AplicarArma(nueva);
    }

    void AplicarArma(int arma)
    {
        if (armaAplicada == arma) return;
        armaAplicada = arma;
        armaActual = arma;
        if (armaRifle != null) armaRifle.SetActive(arma == 1);
        if (armaPistola != null) armaPistola.SetActive(arma == 2);
        if (jugador.animator != null)
        {
            jugador.animator.SetInteger("Arma", arma == 3 ? 0 : arma);
        }
    }

    // Jugadores remotos: aplican el arma que llega por red y el retroceso visual
    public void ActualizarRemoto()
    {
        AplicarArma(redArma.Value);
        AplicarRecoilDelArma();
    }

    // ------------------------- APUNTAR / RECARGAR -------------------------

    public void LeerApuntar()
    {
        if (armaActual == 3) { apuntando = false; return; } // Desarmado: no puede apuntar
        apuntando = Mouse.current != null && Mouse.current.rightButton.isPressed && !recargando;
    }

    public void ForzarNoApuntar()
    {
        apuntando = false;
    }

    public void Recargar()
    {
        if (armaActual == 3) return; // Desarmado: no puede recargar
        if (recargando)
        {
            if (Time.time >= recargaHasta) recargando = false;
            return;
        }
        if (Keyboard.current.rKey.wasPressedThisFrame)
        {
            recargando = true;
            recargaHasta = Time.time + 1.8f;
        }
    }

    // ------------------------- RETROCESO DEL ARMA -------------------------

    void GuardarPoseDeReposoDeLasArmas()
    {
        if (armaRifle != null)
        {
            posReposoRifle = armaRifle.transform.localPosition;
            rotReposoRifle = armaRifle.transform.localRotation;
        }
        if (armaPistola != null)
        {
            posReposoPistola = armaPistola.transform.localPosition;
            rotReposoPistola = armaPistola.transform.localRotation;
        }
    }

    void DevolverArmasAReposo()
    {
        if (armaRifle != null)
        {
            armaRifle.transform.localPosition = posReposoRifle;
            armaRifle.transform.localRotation = rotReposoRifle;
        }
        if (armaPistola != null)
        {
            armaPistola.transform.localPosition = posReposoPistola;
            armaPistola.transform.localRotation = rotReposoPistola;
        }
    }

    public void AplicarRecoilDelArma()
    {
        if (recoilArma <= 0f) return;
        recoilArma = Mathf.Max(0f, recoilArma - Time.deltaTime * 3.5f);

        Transform activa;
        Vector3 posReposo;
        Quaternion rotReposo;
        if (armaActual == 2 && armaPistola != null)
        {
            activa = armaPistola.transform; posReposo = posReposoPistola; rotReposo = rotReposoPistola;
        }
        else if (armaRifle != null)
        {
            activa = armaRifle.transform; posReposo = posReposoRifle; rotReposo = rotReposoRifle;
        }
        else return;

        activa.localPosition = posReposo;
        Quaternion reposoMundo = activa.parent != null ? activa.parent.rotation * rotReposo : rotReposo;
        Vector3 ejeDerechaDelArma = reposoMundo * Vector3.right;
        float rotacionRecoilActual = armaActual == 2 ? retrocesoRotacionPistola : retrocesoRotacionRifle;

        Quaternion inclinadaMundo = Quaternion.AngleAxis(-recoilArma * rotacionRecoilActual, ejeDerechaDelArma) * reposoMundo;
        activa.localRotation = activa.parent != null
            ? Quaternion.Inverse(activa.parent.rotation) * inclinadaMundo
            : inclinadaMundo;
    }

    // ------------------------- DISPARO -------------------------

    public void Disparar()
    {
        if (armaActual == 3) return;
        if (Mouse.current == null) return;
        if (PauseMenuUI.Abierto) return;

        // La pistola dispara solo con click (una vez por apretada); el rifle es automático mientras se mantiene
        bool gatilloApretado = armaActual == 2
            ? Mouse.current.leftButton.wasPressedThisFrame
            : Mouse.current.leftButton.isPressed;

        if (!gatilloApretado) return;
        if (Time.time < proximoDisparoPermitido || recargando) return;

        float danioActual = armaActual == 2 ? danioPistola : danioRifle;
        float retrocesoActual = armaActual == 2 ? retrocesoPistola : retrocesoRifle;
        float cadenciaActual = armaActual == 2 ? cadenciaPistola : cadenciaRifle;

        proximoDisparoPermitido = Time.time + 1f / cadenciaActual;

        // el raycast sale desde la camara: dispara a donde apunta la pantalla (el crosshair)
        Transform camaraTransform = jugador.CamaraTransform;
        Vector3 origen = camaraTransform != null
            ? camaraTransform.position + camaraTransform.forward * 0.2f
            : PuntoDeDisparo();
        Vector3 direccion = camaraTransform != null ? camaraTransform.forward : transform.forward;

        // dispersion al disparar desde la cadera; apuntando el disparo es preciso
        if (!apuntando && camaraTransform != null)
        {
            float angulo = Random.Range(-dispersion, dispersion);
            direccion = Quaternion.AngleAxis(angulo, camaraTransform.up) * direccion;
        }

        // el rayo nace detras del cuerpo: se descarta cualquier impacto del propio jugador
        RaycastHit elegido = default;
        bool acerto = false;
        RaycastHit[] impactos = Physics.RaycastAll(origen, direccion, alcance, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(impactos, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit h in impactos)
        {
            if (jugador.DelPropioCuerpo(h.collider.transform)) continue;
            elegido = h;
            acerto = true;
            break;
        }

        if (acerto)
        {
            NetworkObject objetivo = elegido.collider.transform.root.GetComponent<NetworkObject>();
            if (objetivo != null && objetivo.IsPlayerObject)
            {
                DanioAJugadorServerRpc(danioActual, objetivo.OwnerClientId);
            }

            if (efectoImpacto != null)
                Instantiate(efectoImpacto, elegido.point, Quaternion.LookRotation(elegido.normal));
        }

        // efecto local inmediato en la boca del arma
        // (si el arma esta metida en una pared, la boca se acerca al lado del jugador)
        Vector3 posicionDeLaBoca = AjustarBocaPorPared(PuntoDeDisparo());
        CrearEfectoDisparo(posicionDeLaBoca, BocaActual());

        // rayo laser: de la boca del arma al punto de impacto (o al maximo alcance)
        Vector3 puntoFinal = acerto ? elegido.point : origen + direccion * alcance;
        CrearLaser(posicionDeLaBoca, puntoFinal);

        ReproducirDisparo();

        // el instante de disparo viaja por red para reproducirse en el remoto sin retardo
        DisparoEfectuadoClientRpc(posicionDeLaBoca, puntoFinal);

        // retroceso: el arma se inclina y vuelve; la vista se empuja hacia arriba
        // (rotacionVertical positivo inclina hacia abajo, por eso se resta)
        recoilArma = 1f;
        if (jugador.controlCamara != null)
            jugador.controlCamara.rotacionVertical -= apuntando ? retrocesoActual * 0.7f : retrocesoActual;
    }

    // Boca del arma que tenes en la mano (cada arma puede tener la suya)
    Transform BocaActual()
    {
        if (armaActual == 2 && bocaPistola != null) return bocaPistola;
        if (armaActual == 1 && bocaRifle != null) return bocaRifle;
        return bocaDelArma;
    }

    Vector3 PuntoDeDisparo()
    {
        Transform boca = BocaActual();
        if (boca != null) return boca.position;
        return transform.TransformPoint(new Vector3(0.25f, 1.35f, 0.7f));
    }

    // Si hay una pared entre el pecho del jugador y la boca del arma (arma metida en el muro),
    // devuelve un punto justo antes de la pared, del lado del jugador.
    // Usa la misma mascara que la camara (Capas Colision Camara en PlayerCameraRig).
    Vector3 AjustarBocaPorPared(Vector3 boca)
    {
        CharacterController controller = jugador.Controller;
        float alturaPecho = controller != null ? controller.height * 0.6f : 1.2f;
        Vector3 desde = transform.position + Vector3.up * alturaPecho;
        Vector3 hacia = boca - desde;
        float dist = hacia.magnitude;
        if (dist < 0.01f) return boca;

        Vector3 dir = hacia / dist;
        if (Physics.Raycast(desde, dir, out RaycastHit h, dist, jugador.CameraRig.capasColisionCamara,
                QueryTriggerInteraction.Ignore)
            && !jugador.DelPropioCuerpo(h.collider.transform))
        {
            return h.point - dir * 0.15f;   // mas que el Near Plane de la luz, para que la pared proyecte sombra
        }
        return boca;
    }

    // ------------------------- EFECTOS -------------------------

    // Crea el destello y lo destruye solo, aunque el prefab no tenga Stop Action en Destroy.
    // Si la posicion coincide con la boca, el destello nace como hijo de ella y sigue al arma
    // mientras te movés; si fue acercada por una pared, nace suelto en ese punto.
    void CrearEfectoDisparo(Vector3 posicion, Transform boca)
    {
        if (efectoDisparo == null) return;

        bool pegada = boca != null && boca.gameObject.activeInHierarchy
                      && Vector3.Distance(posicion, boca.position) < 0.02f;

        GameObject fx = pegada
            ? Instantiate(efectoDisparo, boca.position, boca.rotation, boca)
            : Instantiate(efectoDisparo, posicion, boca != null ? boca.rotation : Quaternion.identity);

        // Arma pegada a una pared: se quitan las chispas (Particle Systems hijos) para que no
        // atraviesen el muro. Quedan el destello y la luz, que son el sistema raiz.
        if (!pegada)
        {
            foreach (ParticleSystem ps in fx.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps.gameObject != fx) Destroy(ps.gameObject);
            }
        }

        Destroy(fx, 0.3f);
    }

    // Dibuja el rayo como una linea que se afina y desvanece rapido
    void CrearLaser(Vector3 desde, Vector3 hasta)
    {
        if (materialLaser == null) return;

        GameObject go = new GameObject("Laser");
        LineRenderer lr = go.AddComponent<LineRenderer>();
        lr.material = materialLaser;
        lr.useWorldSpace = true;
        lr.positionCount = 2;
        lr.SetPosition(0, desde);
        lr.SetPosition(1, hasta);
        lr.widthMultiplier = anchoLaser;
        lr.textureMode = LineTextureMode.Stretch;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.startColor = colorLaser;
        lr.endColor = colorLaser;

        Destroy(go, duracionLaser + 0.2f);   // respaldo por si la corrutina se corta
        StartCoroutine(DesvanecerLaser(lr));
    }

    IEnumerator DesvanecerLaser(LineRenderer lr)
    {
        float t = 0f;
        while (t < duracionLaser && lr != null)
        {
            float k = 1f - t / duracionLaser;
            Color c = colorLaser;
            c.a = colorLaser.a * k;
            lr.startColor = c;
            lr.endColor = c;
            lr.widthMultiplier = anchoLaser * k;
            t += Time.deltaTime;
            yield return null;
        }
        if (lr != null) Destroy(lr.gameObject);
    }

    void ReproducirDisparo()
    {
        if (sonidoDisparo == null || fuenteAudio == null) return;
        fuenteAudio.pitch = Random.Range(0.94f, 1.06f);
        fuenteAudio.PlayOneShot(sonidoDisparo, volumenDisparo);
    }

    // ------------------------- RED -------------------------

    // El cliente que dispara le avisa al SERVIDOR a quien golpeo; el servidor baja la salud
    // de la victima (autoridad del servidor: un cliente no puede escribir la salud de nadie)
    [ServerRpc]
    void DanioAJugadorServerRpc(float danio, ulong clientIdVictima, ServerRpcParams rpcParams = default)
    {
        ulong atacanteId = rpcParams.Receive.SenderClientId;

        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientIdVictima, out var cliente)
            && cliente.PlayerObject != null)
        {
            PlayerHealth victima = cliente.PlayerObject.GetComponent<PlayerHealth>();
            if (victima != null) victima.RecibirDanioEnServidor(danio, atacanteId);
        }
    }

    // Reproduce en las instancias remotas el instante de disparo del dueño.
    [Rpc(SendTo.ClientsAndHost)]
    void DisparoEfectuadoClientRpc(Vector3 posicionDeLaBoca, Vector3 puntoFinal)
    {
        if (IsLocalPlayer) return;
        recoilArma = 1f;

        // Usa la boca real de esta instancia (si existe) y la acerca si hay una pared de por medio
        Transform boca = BocaActual();
        Vector3 posBoca = AjustarBocaPorPared(boca != null ? boca.position : posicionDeLaBoca);

        CrearEfectoDisparo(posBoca, boca);
        CrearLaser(posBoca, puntoFinal);
        ReproducirDisparo();
    }
}