using System.Collections.Generic;
using UnityEngine;

namespace Asteroids
{
    public class AsteroidBeltSpawner : MonoBehaviour
    {
        [Tooltip("The central object which the asteroids orbit around.")]
        public GameObject sun;

        [Tooltip("The prefab used to represent asteroids.")]
        public GameObject asteroidPrefab;

        [Tooltip("The maximum radius of the asteroid belt orbit.")]
        public float maxOrbitRadius = 100f;

        [Tooltip("Total number of asteroids to spawn.")]
        public int maxAsteroidsCount = 2000;

        [Tooltip("Minimum size scale for asteroids.")]
        public float minSize = 0.5f;

        [Tooltip("Maximum size scale for asteroids.")]
        public float maxSize = 2.0f;

        [Tooltip("The curve controlling the horizontal spread of asteroids.")]
        public AnimationCurve horizontalSpreadCurve;

        [Tooltip("The curve controlling the vertical spread of asteroids.")]
        public AnimationCurve verticalSpreadCurve;

        [Tooltip("Minimum distance between spawned asteroids.")]
        public float spaceBetween = 2f;

        [Tooltip("Configuracion global del menu (cantidad de meteoritos elegida por el usuario).")]
        public MeteorSettings meteorSettings;

        [Tooltip("Carpeta de la Hierarchy donde se guardan los asteroides (si queda vacia se crea sola). Debe tener escala 1 y rotacion 0.")]
        public Transform carpetaAsteroides;

        // Grilla espacial: cada celda guarda las posiciones (xyz) y tamanos (w) de los asteroides
        // que caen en ella. Asi, comprobar si una posicion es valida solo mira a los vecinos
        // cercanos en vez de comparar contra todos los asteroides ya creados.
        private Dictionary<Vector3Int, List<Vector4>> grilla = new Dictionary<Vector3Int, List<Vector4>>();
        private float tamanoCelda;

        void Start()
        {
            // Leer la cantidad de asteroides elegida en el menu
            if (meteorSettings != null)
            {
                maxAsteroidsCount = meteorSettings.maxAsteroidsCount;
                Debug.Log($"[Arena] Generando {maxAsteroidsCount} asteroides");
            }
            else
            {
                Debug.LogWarning("[Arena] meteorSettings no asignado. Usando valor por defecto: " + maxAsteroidsCount);
            }

            // Si no hay Sun asignado, usamos este mismo objeto como centro
            if (sun == null)
            {
                Debug.LogWarning("AsteroidBeltSpawner: 'sun' no esta asignado. Se usa este objeto como centro.");
                sun = gameObject;
            }

            // Si las curvas estan vacias, usar una curva constante para que si se generen asteroides
            if (horizontalSpreadCurve == null || horizontalSpreadCurve.length == 0)
                horizontalSpreadCurve = AnimationCurve.Constant(0f, 1f, 0.25f);

            if (verticalSpreadCurve == null || verticalSpreadCurve.length == 0)
                verticalSpreadCurve = AnimationCurve.Constant(0f, 1f, 0.25f);

            // Si no se asigno una carpeta, se crea una en la raiz de la escena
            if (carpetaAsteroides == null)
            {
                GameObject carpeta = new GameObject("Asteroides");
                carpetaAsteroides = carpeta.transform;
            }

            // Una celda mide lo maximo que puede valer la separacion entre dos asteroides:
            // con eso alcanza revisar la celda propia y las 26 vecinas
            tamanoCelda = Mathf.Max(0.01f, maxSize + spaceBetween);

            SpawnAsteroids();

            // Solo destruir si es un objeto de la escena (plantilla), nunca un asset del Project
            if (asteroidPrefab != null && asteroidPrefab.scene.IsValid())
            {
                Destroy(asteroidPrefab);
            }
        }

        void SpawnAsteroids()
        {
            if (asteroidPrefab == null)
            {
                Debug.LogError("AsteroidBeltSpawner: 'asteroidPrefab' no esta asignado.");
                return;
            }

            int currentAsteroidsCount = 0;

            for (int i = 0; i < 360; i++)
            {
                // Valores de las curvas para el angulo actual (coordenadas normalizadas)
                float horizontalDensity = horizontalSpreadCurve.Evaluate(i / 360f);
                float verticalDensity = verticalSpreadCurve.Evaluate(i / 360f);

                // Cantidad de asteroides para este angulo segun la densidad horizontal.
                int asteroidsInThisSegment = Mathf.CeilToInt(horizontalDensity * (maxAsteroidsCount / 360f));

                for (int j = 0; j < asteroidsInThisSegment && currentAsteroidsCount < maxAsteroidsCount; j++)
                {
                    // Radio principal de la orbita
                    float orbitRadius = Random.Range(maxOrbitRadius * 0.8f, maxOrbitRadius);

                    // Desplazamiento horizontal respecto al radio de la orbita
                    float horizontalOffset = Random.Range(-horizontalDensity * orbitRadius, horizontalDensity * orbitRadius);

                    // Posicion en la orbita
                    Vector3 orbitPosition = Quaternion.Euler(0, i, 0) * new Vector3(orbitRadius + horizontalOffset, 0, 0);

                    // Cantidad de capas verticales para este angulo
                    int verticalAsteroidsInThisSegment = Mathf.RoundToInt(verticalDensity * 10);

                    for (int k = 0; k < verticalAsteroidsInThisSegment && currentAsteroidsCount < maxAsteroidsCount; k++)
                    {
                        // Desplazamiento vertical segun el valor de la curva
                        float verticalOffset = Mathf.Lerp(0, Random.Range(-maxOrbitRadius / 10f, maxOrbitRadius / 10f), verticalDensity);

                        // Posicion final del asteroide con desplazamiento vertical
                        Vector3 finalPosition = sun.transform.position + orbitPosition + new Vector3(0, verticalOffset, 0);

                        // Tamano aleatorio
                        float asteroidSize = Random.Range(minSize, maxSize);

                        // Comprobar interseccion con otros asteroides teniendo en cuenta su tamano
                        if (!IsPositionValidWithSize(finalPosition, asteroidSize))
                        {
                            continue;
                        }

                        // Guardar posicion y tamano en la grilla
                        RegistrarPosicion(finalPosition, asteroidSize);

                        // Crear el asteroide
                        SpawnAsteroid(finalPosition, asteroidSize);

                        currentAsteroidsCount++;

                        if (currentAsteroidsCount >= maxAsteroidsCount)
                            return;
                    }
                }
            }
        }

        // Celda de la grilla a la que pertenece una posicion
        Vector3Int Celda(Vector3 p)
        {
            return new Vector3Int(
                Mathf.FloorToInt(p.x / tamanoCelda),
                Mathf.FloorToInt(p.y / tamanoCelda),
                Mathf.FloorToInt(p.z / tamanoCelda));
        }

        // Comprueba que la nueva posicion no choque con otros asteroides.
        // Solo revisa la celda propia y las vecinas, no todos los asteroides.
        bool IsPositionValidWithSize(Vector3 newPosition, float newSize)
        {
            Vector3Int c = Celda(newPosition);

            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dz = -1; dz <= 1; dz++)
            {
                if (!grilla.TryGetValue(c + new Vector3Int(dx, dy, dz), out List<Vector4> lista))
                    continue;

                foreach (Vector4 d in lista)
                {
                    float distanceBetweenAsteroids = Vector3.Distance(newPosition, new Vector3(d.x, d.y, d.z));
                    if (distanceBetweenAsteroids < (d.w / 2 + newSize / 2 + spaceBetween))
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        // Guarda un asteroide en la celda que le corresponde
        void RegistrarPosicion(Vector3 pos, float size)
        {
            Vector3Int c = Celda(pos);
            if (!grilla.TryGetValue(c, out List<Vector4> lista))
            {
                lista = new List<Vector4>();
                grilla[c] = lista;
            }
            lista.Add(new Vector4(pos.x, pos.y, pos.z, size));
        }

        void SpawnAsteroid(Vector3 position, float size)
        {
            // El ultimo parametro es el padre: el asteroide queda dentro de la carpeta.
            // position sigue siendo de mundo con esta sobrecarga.
            GameObject asteroid = Instantiate(asteroidPrefab, position, Quaternion.identity, carpetaAsteroides);
            asteroid.SetActive(true);
            asteroid.transform.localScale = new Vector3(size, size, size);

            // Pasar el Sun a cada asteroide (el prefab del Project no puede guardarlo)
            var orbit = asteroid.GetComponent<AsteroidOrbitAndRotate>();
            if (orbit != null)
            {
                orbit.sun = sun.transform;
            }
        }
    }
}