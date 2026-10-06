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

        private List<Vector4> asteroidPositionsAndSizes = new List<Vector4>(); // Posiciones (xyz) y tamanos (w)

        void Start()
        {
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
                // CeilToInt asegura al menos 1 si la densidad es mayor que 0.
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

                        // Guardar posicion y tamano
                        asteroidPositionsAndSizes.Add(new Vector4(finalPosition.x, finalPosition.y, finalPosition.z, asteroidSize));

                        // Crear el asteroide
                        SpawnAsteroid(finalPosition, asteroidSize);

                        currentAsteroidsCount++;

                        if (currentAsteroidsCount >= maxAsteroidsCount)
                            return;
                    }
                }
            }
        }

        // Comprueba que la nueva posicion no choque con otros asteroides
        bool IsPositionValidWithSize(Vector3 newPosition, float newSize)
        {
            foreach (var asteroidData in asteroidPositionsAndSizes)
            {
                Vector3 existingPosition = new Vector3(asteroidData.x, asteroidData.y, asteroidData.z);
                float existingSize = asteroidData.w;

                float distanceBetweenAsteroids = Vector3.Distance(newPosition, existingPosition);
                if (distanceBetweenAsteroids < (existingSize / 2 + newSize / 2 + spaceBetween))
                {
                    return false;
                }
            }
            return true;
        }

        void SpawnAsteroid(Vector3 position, float size)
        {
            GameObject asteroid = Instantiate(asteroidPrefab, position, Quaternion.identity);
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