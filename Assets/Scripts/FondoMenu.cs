using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class FondoMenu : MonoBehaviour
{
    public Transform camara;          // la cámara del menú
    public float velocidadGiro = 4f;  // grados por segundo
    public Transform centro;          // opcional: punto alrededor del cual gira

    IEnumerator Start()
    {
        if (!SceneManager.GetSceneByName("ArenaFondo").isLoaded)
        {
            var op = SceneManager.LoadSceneAsync("ArenaFondo", LoadSceneMode.Additive);
            yield return op;
        }
        SceneManager.SetActiveScene(SceneManager.GetSceneByName("ArenaFondo"));
    }

    void Update()
    {
        if (camara == null) return;
        if (centro != null)
            camara.RotateAround(centro.position, Vector3.up, velocidadGiro * Time.deltaTime);
        else
            camara.Rotate(Vector3.up, velocidadGiro * Time.deltaTime, Space.World);
    }
}